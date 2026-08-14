[CmdletBinding(DefaultParameterSetName = 'Sign')]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string[]]$Path,

    [Parameter(Mandatory = $true, ParameterSetName = 'Sign')]
    [ValidatePattern('^https://(brs|cus|eus|jpe|krc|ncus|neu|plc|scus|swn|wcus|weu|wus|wus2|wus3)\.codesigning\.azure\.net/?$')]
    [string]$Endpoint,

    [Parameter(Mandatory = $true, ParameterSetName = 'Sign')]
    [ValidatePattern('^(?!one)(?!.*--)[A-Za-z][A-Za-z0-9-]{1,22}[A-Za-z0-9]$')]
    [string]$CodeSigningAccountName,

    [Parameter(Mandatory = $true, ParameterSetName = 'Sign')]
    [ValidatePattern('^(?!.*--)[A-Za-z][A-Za-z0-9-]{3,98}[A-Za-z0-9]$')]
    [string]$CertificateProfileName,

    [Parameter(ParameterSetName = 'Sign')]
    [ValidateSet('Default', 'InteractiveBrowser', 'AzureCli', 'Environment')]
    [string]$AuthenticationMode = 'InteractiveBrowser',

    [Parameter(ParameterSetName = 'Sign')]
    [ValidatePattern('^[A-Za-z0-9._:/-]{1,128}$')]
    [string]$CorrelationId,

    [Parameter(ParameterSetName = 'Verify', Mandatory = $true)]
    [switch]$VerifyOnly,

    [string]$SignToolPath,

    [Parameter(ParameterSetName = 'Sign')]
    [string]$DlibPath,

    [ValidateNotNullOrEmpty()]
    [string]$ExpectedPublisher = 'Expired Soda Studios LLC',

    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$minimumSignToolVersion = [Version]'10.0.2261.755'
$timestampAuthority = 'http://timestamp.acs.microsoft.com'
$artifactSigningHosts = @(
    'brs.codesigning.azure.net', 'cus.codesigning.azure.net', 'eus.codesigning.azure.net',
    'jpe.codesigning.azure.net', 'krc.codesigning.azure.net', 'ncus.codesigning.azure.net',
    'neu.codesigning.azure.net', 'plc.codesigning.azure.net', 'scus.codesigning.azure.net',
    'swn.codesigning.azure.net', 'wcus.codesigning.azure.net', 'weu.codesigning.azure.net',
    'wus.codesigning.azure.net', 'wus2.codesigning.azure.net', 'wus3.codesigning.azure.net')

. (Join-Path $PSScriptRoot 'Resolve-ReplayFoundryArtifactSigningClient.ps1')

function Resolve-SignTool([string]$ExplicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $resolved = [IO.Path]::GetFullPath($ExplicitPath)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "SignTool was not found: $resolved"
        }
        return $resolved
    }

    $kitRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem -LiteralPath $kitRoot -Directory -ErrorAction SilentlyContinue |
        ForEach-Object {
            $version = $null
            if ([Version]::TryParse($_.Name, [ref]$version)) {
                $path = Join-Path $_.FullName 'x64\signtool.exe'
                if (Test-Path -LiteralPath $path -PathType Leaf) {
                    [pscustomobject]@{ Path = $path; Version = $version }
                }
            }
        } |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw 'A Windows SDK x64 SignTool was not found.'
    }
    return $candidate.Path
}

function Get-FileVersion([string]$FilePath) {
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($FilePath)
    foreach ($raw in @($versionInfo.ProductVersion, $versionInfo.FileVersion)) {
        $numeric = [regex]::Match($raw ?? '', '\d+\.\d+\.\d+\.\d+').Value
        if (-not [string]::IsNullOrWhiteSpace($numeric) -and ([Version]$numeric).Major -ge 10) {
            return [Version]$numeric
        }
    }

    foreach ($segment in ([IO.Path]::GetFullPath($FilePath) -split '[\\/]')) {
        $parsed = $null
        if ([Version]::TryParse($segment, [ref]$parsed) -and $parsed.Major -ge 10) {
            return $parsed
        }
    }

    throw "Unable to read a supported Windows SDK version from $FilePath."
}

function Get-ExcludedCredentials([string]$Mode) {
    $all = @(
        'EnvironmentCredential',
        'WorkloadIdentityCredential',
        'ManagedIdentityCredential',
        'SharedTokenCacheCredential',
        'VisualStudioCredential',
        'VisualStudioCodeCredential',
        'AzureCliCredential',
        'AzurePowerShellCredential',
        'AzureDeveloperCliCredential',
        'InteractiveBrowserCredential'
    )
    switch ($Mode) {
        'Default' { return @() }
        'InteractiveBrowser' { return @($all | Where-Object { $_ -ne 'InteractiveBrowserCredential' }) }
        'AzureCli' { return @($all | Where-Object { $_ -ne 'AzureCliCredential' }) }
        'Environment' { return @($all | Where-Object { $_ -ne 'EnvironmentCredential' }) }
        default { throw "Unsupported authentication mode '$Mode'." }
    }
}

function Get-SignatureRecord([string]$Target, [string]$Publisher) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Target
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode verification failed for '$Target': $($signature.Status) $($signature.StatusMessage)"
    }
    if ($null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch [regex]::Escape($Publisher)) {
        throw "The signer for '$Target' does not match '$Publisher'."
    }
    if ($null -eq $signature.TimeStamperCertificate) {
        throw "The signature for '$Target' has no trusted timestamp."
    }
    return [ordered]@{
        path = $Target
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $Target).Hash
        status = $signature.Status.ToString()
        signerSubject = $signature.SignerCertificate.Subject
        signerThumbprint = $signature.SignerCertificate.Thumbprint
        timestampSubject = $signature.TimeStamperCertificate.Subject
        timestampThumbprint = $signature.TimeStamperCertificate.Thumbprint
    }
}

$resolvedSignTool = Resolve-SignTool $SignToolPath
$signToolVersion = Get-FileVersion $resolvedSignTool
if ($signToolVersion -lt $minimumSignToolVersion) {
    throw "SignTool $signToolVersion is older than the Artifact Signing minimum $minimumSignToolVersion."
}

$targets = @($Path | ForEach-Object {
    $target = [IO.Path]::GetFullPath($_)
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "Signing target was not found: $target"
    }
    $extension = [IO.Path]::GetExtension($target).ToLowerInvariant()
    $isInnoTemporaryUninstaller =
        $extension -eq '.tmp' -and
        [IO.Path]::GetFileName($target).Equals('uninst.e32.tmp', [StringComparison]::OrdinalIgnoreCase)
    if ($extension -notin @('.exe', '.dll', '.msi', '.msix', '.cab') -and
        -not $isInnoTemporaryUninstaller) {
        throw "Unsupported Authenticode target: $target"
    }
    $target
})
if ($targets.Count -ne (@($targets | Select-Object -Unique)).Count) {
    throw 'Signing targets must be unique.'
}

$resolvedDlib = $null
$metadataDirectory = $null
$metadataPath = $null
$records = @()
try {
    if (-not $VerifyOnly) {
        $endpointUri = $null
        if (-not [Uri]::TryCreate($Endpoint, [UriKind]::Absolute, [ref]$endpointUri) -or
            $endpointUri.Scheme -ne [Uri]::UriSchemeHttps -or
            $endpointUri.Host -notin $artifactSigningHosts -or
            -not [string]::IsNullOrEmpty($endpointUri.UserInfo) -or
            -not [string]::IsNullOrEmpty($endpointUri.Query) -or
            -not [string]::IsNullOrEmpty($endpointUri.Fragment)) {
            throw 'Artifact Signing endpoint must be one official regional https://*.codesigning.azure.net endpoint without credentials, query, or fragment.'
        }
        $resolvedDlib = (Resolve-ReplayFoundryArtifactSigningDlib $DlibPath).FullName
        $metadataDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ReplayFoundry-ArtifactSigning-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $metadataDirectory | Out-Null
        $metadataPath = Join-Path $metadataDirectory 'metadata.json'
        $metadata = [ordered]@{
            Endpoint = $endpointUri.GetLeftPart([UriPartial]::Authority)
            CodeSigningAccountName = $CodeSigningAccountName
            CertificateProfileName = $CertificateProfileName
        }
        if (-not [string]::IsNullOrWhiteSpace($CorrelationId)) { $metadata.CorrelationId = $CorrelationId }
        $excluded = @(Get-ExcludedCredentials $AuthenticationMode)
        if ($excluded.Count -gt 0) { $metadata.ExcludeCredentials = $excluded }
        $metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $metadataPath -Encoding utf8NoBOM

        foreach ($target in $targets) {
            & $resolvedSignTool sign /v /fd SHA256 /tr $timestampAuthority /td SHA256 /dlib $resolvedDlib /dmdf $metadataPath $target
            if ($LASTEXITCODE -ne 0) { throw "Artifact Signing failed for '$target' with exit code $LASTEXITCODE." }
        }
    }

    foreach ($target in $targets) {
        & $resolvedSignTool verify /pa /all /v $target
        if ($LASTEXITCODE -ne 0) { throw "SignTool verification failed for '$target' with exit code $LASTEXITCODE." }
        $records += Get-SignatureRecord $target $ExpectedPublisher
    }
} finally {
    if ($null -ne $metadataDirectory -and (Test-Path -LiteralPath $metadataDirectory)) {
        [IO.Directory]::Delete($metadataDirectory, $true)
    }
}

$report = [ordered]@{
    schemaVersion = 'replayfoundry-artifact-signing-report-1.0'
    mode = if ($VerifyOnly) { 'VerifyOnly' } else { 'ArtifactSigning' }
    expectedPublisher = $ExpectedPublisher
    timestampAuthority = $timestampAuthority
    signToolPath = $resolvedSignTool
    signToolVersion = $signToolVersion.ToString()
    dlibSha256 = if ($null -eq $resolvedDlib) { $null } else { (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedDlib).Hash }
    signedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    files = @($records)
}
if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $resolvedReport = [IO.Path]::GetFullPath($ReportPath)
    $reportParent = Split-Path -Parent $resolvedReport
    if (-not (Test-Path -LiteralPath $reportParent)) { New-Item -ItemType Directory -Path $reportParent | Out-Null }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedReport -Encoding utf8NoBOM
}
$report
