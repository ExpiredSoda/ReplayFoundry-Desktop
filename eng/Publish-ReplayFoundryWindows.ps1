[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateRange(0, 65535)]
    [int]$BuildRevision = 0,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[^\s]+\.apps\.googleusercontent\.com$')]
    [string]$YouTubeClientId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string]$AdvancedInstallerUri,

    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [ValidateSet('Development', 'Production')]
    [string]$ReleaseChannel = 'Development',

    [ValidateSet('Unsigned', 'ArtifactSigning')]
    [string]$SigningMode = 'Unsigned',

    [ValidatePattern('^https://(brs|cus|eus|jpe|krc|ncus|neu|plc|scus|swn|wcus|weu|wus|wus2|wus3)\.codesigning\.azure\.net/?$')]
    [string]$ArtifactSigningEndpoint,

    [ValidatePattern('^(?!one)(?!.*--)[A-Za-z][A-Za-z0-9-]{1,22}[A-Za-z0-9]$')]
    [string]$ArtifactSigningAccountName,

    [ValidatePattern('^(?!.*--)[A-Za-z][A-Za-z0-9-]{3,98}[A-Za-z0-9]$')]
    [string]$ArtifactSigningCertificateProfileName,

    [ValidateSet('Default', 'InteractiveBrowser', 'AzureCli', 'Environment')]
    [string]$ArtifactSigningAuthenticationMode = 'InteractiveBrowser',

    [string]$SignToolPath,

    [string]$ArtifactSigningDlibPath,

    [ValidatePattern('^[A-Za-z0-9._:/-]{1,128}$')]
    [string]$SigningCorrelationId,

    [string]$SigningReportPath
)

$ErrorActionPreference = 'Stop'
$youTubeClientSecret = [Environment]::GetEnvironmentVariable(
    'REPLAYFOUNDRY_YOUTUBE_CLIENT_SECRET',
    [EnvironmentVariableTarget]::Process)
if ([string]::IsNullOrWhiteSpace($youTubeClientSecret)) {
    throw 'Set REPLAYFOUNDRY_YOUTUBE_CLIENT_SECRET for the Google Desktop OAuth client before publishing.'
}
$repoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'src\ReplayFoundry.Desktop\ReplayFoundry.Desktop.csproj'
$userReportEndpointOutput = @(
    & dotnet msbuild $project -nologo `
        -getProperty:ReplayFoundryUserReportEndpoint)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to resolve the fixed Replay Foundry user-report endpoint.'
}
$userReportEndpoint = ($userReportEndpointOutput -join '').Trim()
if ([string]::IsNullOrWhiteSpace($userReportEndpoint)) {
    throw 'The desktop project does not identify its fixed official Replay Foundry user-report endpoint.'
}
$publisher = 'Expired Soda Studios LLC'
$dataChannel = if ($ReleaseChannel -eq 'Production') {
    'Production'
} else {
    'Development'
}
$sourceStatus = @(git -C $repoRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the source working tree.' }
$sourceTreeDirty = $sourceStatus.Count -ne 0
if ($ReleaseChannel -eq 'Production' -and $SigningMode -ne 'ArtifactSigning') {
    throw 'Production publishing requires Microsoft Artifact Signing. Use Development for an explicitly unsigned proof build.'
}
if ($ReleaseChannel -eq 'Production' -and $sourceTreeDirty) {
    throw 'Production publishing requires a clean source working tree so the signed payload matches its recorded commit.'
}
if ($SigningMode -eq 'ArtifactSigning') {
    foreach ($required in @(
        @{ Name = 'ArtifactSigningEndpoint'; Value = $ArtifactSigningEndpoint },
        @{ Name = 'ArtifactSigningAccountName'; Value = $ArtifactSigningAccountName },
        @{ Name = 'ArtifactSigningCertificateProfileName'; Value = $ArtifactSigningCertificateProfileName })) {
        if ([string]::IsNullOrWhiteSpace($required.Value)) {
            throw "$($required.Name) is required for Artifact Signing."
        }
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\publish\ReplayFoundry-$Version-win-x64"
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $resolvedOutput) {
    $existing = @(Get-ChildItem -LiteralPath $resolvedOutput -Force)
    if ($existing.Count -gt 0) {
        throw "Publish output already contains files: $resolvedOutput. Choose a new output directory."
    }
} else {
    New-Item -ItemType Directory -Path $resolvedOutput | Out-Null
}

$commit = (git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to identify the source commit.'
}

$arguments = @(
    'publish', $project,
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $resolvedOutput,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:PublishReadyToRun=false',
    '-p:DebugSymbols=false',
    '-p:DebugType=None',
    "-p:Version=$Version",
    "-p:FileVersion=$($Version.Split('-')[0]).$BuildRevision",
    "-p:ReplayFoundryDataChannel=$dataChannel",
    "-p:ReplayFoundryYouTubeClientId=$YouTubeClientId",
    "-p:ReplayFoundryAdvancedInstallerUri=$AdvancedInstallerUri"
)

$updateConfiguration = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'ReplayFoundry.Updates.psd1')
$updateChannel = if ($Version.Contains('-')) { 'beta' } else { 'stable' }
if ($ReleaseChannel -eq 'Production') {
    if ($BuildRevision -lt 1) { throw 'Updater-enabled production releases require a distinct positive BuildRevision.' }
    $arguments += @('-p:ReplayFoundryUpdatesEnabled=true',
        "-p:ReplayFoundryUpdatePublicKey=$($updateConfiguration.PublicKey)",
        "-p:ReplayFoundryUpdateChannel=$updateChannel")
}

try {
    $previousBuildSecret = $env:ReplayFoundryYouTubeClientSecret
    $env:ReplayFoundryYouTubeClientSecret = $youTubeClientSecret
    & dotnet @arguments
    $publishExitCode = $LASTEXITCODE
} finally {
    $env:ReplayFoundryYouTubeClientSecret = $previousBuildSecret
}
if ($publishExitCode -ne 0) {
    throw "Replay Foundry publish failed with exit code $publishExitCode."
}

$winSparkleSdk = & (Join-Path $PSScriptRoot 'Resolve-ReplayFoundryWinSparkle.ps1')
Copy-Item -LiteralPath (Join-Path $winSparkleSdk 'x64\Release\WinSparkle.dll') -Destination (Join-Path $resolvedOutput 'WinSparkle.dll')
$notices = Join-Path $resolvedOutput 'ThirdParty'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $winSparkleSdk 'COPYING') -Destination (Join-Path $notices 'WinSparkle-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $winSparkleSdk 'COPYING.expat') -Destination (Join-Path $notices 'WinSparkle-Expat-LICENSE.txt')

# ONNX Runtime's NuGet package currently copies tiny linker import libraries
# into publish output. They are build-time inputs, not Windows runtime payloads.
# The output directory was required to be empty before this run, so these
# generated files are safe and unambiguous to omit from the release.
Get-ChildItem -LiteralPath $resolvedOutput -File -Filter '*.lib' |
    Remove-Item -Force

$application = Join-Path $resolvedOutput 'ReplayFoundry.Desktop.exe'
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw 'The self-contained Replay Foundry executable was not produced.'
}

$runtimeInstallerOutput = Join-Path $resolvedOutput 'Tools\RuntimeInstaller'
& dotnet publish (Join-Path $repoRoot 'tools\ReplayFoundry.RuntimeInstaller\ReplayFoundry.RuntimeInstaller.csproj') `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $runtimeInstallerOutput `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:ReplayFoundryDataChannel=$dataChannel `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw "Replay Foundry runtime installer publish failed with exit code $LASTEXITCODE."
}
$runtimeInstaller = Join-Path $runtimeInstallerOutput 'ReplayFoundry.RuntimeInstaller.exe'
if (-not (Test-Path -LiteralPath $runtimeInstaller -PathType Leaf)) {
    throw 'The runtime maintenance executable was not produced.'
}

$ownedBinaries = @($application, $runtimeInstaller)
if ($SigningMode -eq 'ArtifactSigning') {
    if ([string]::IsNullOrWhiteSpace($SigningReportPath)) {
        $SigningReportPath = Join-Path (Split-Path -Parent $resolvedOutput) 'reports\application-signing-report.json'
    }
    $signingArguments = @{
        Path = $ownedBinaries
        Endpoint = $ArtifactSigningEndpoint
        CodeSigningAccountName = $ArtifactSigningAccountName
        CertificateProfileName = $ArtifactSigningCertificateProfileName
        AuthenticationMode = $ArtifactSigningAuthenticationMode
        ExpectedPublisher = $publisher
        ReportPath = $SigningReportPath
    }
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) { $signingArguments.SignToolPath = $SignToolPath }
    if (-not [string]::IsNullOrWhiteSpace($ArtifactSigningDlibPath)) { $signingArguments.DlibPath = $ArtifactSigningDlibPath }
    if (-not [string]::IsNullOrWhiteSpace($SigningCorrelationId)) { $signingArguments.CorrelationId = $SigningCorrelationId }
    & (Join-Path $PSScriptRoot 'Invoke-ReplayFoundryArtifactSigning.ps1') @signingArguments | Out-Null
}

$signatureFiles = @($ownedBinaries | ForEach-Object {
    $signature = Get-AuthenticodeSignature -LiteralPath $_
    [ordered]@{
        path = [System.IO.Path]::GetRelativePath($resolvedOutput, $_).Replace('\', '/')
        status = $signature.Status.ToString()
        signerSubject = if ($null -eq $signature.SignerCertificate) { $null } else { $signature.SignerCertificate.Subject }
        signerThumbprint = if ($null -eq $signature.SignerCertificate) { $null } else { $signature.SignerCertificate.Thumbprint }
        timestampSubject = if ($null -eq $signature.TimeStamperCertificate) { $null } else { $signature.TimeStamperCertificate.Subject }
    }
})
if ($SigningMode -eq 'ArtifactSigning' -and @($signatureFiles | Where-Object { $_.status -ne 'Valid' }).Count -ne 0) {
    throw 'One or more publisher-owned binaries failed post-signing Authenticode verification.'
}

$files = Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse |
    Where-Object { $_.Name -ne 'release-manifest.json' } |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($resolvedOutput, $_.FullName).Replace('\', '/')
            size = $_.Length
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
        }
    }

$manifest = [ordered]@{
    schemaVersion = 'replayfoundry-release-manifest-1.1'
    productVersion = $Version
    fileVersion = "$($Version.Split('-')[0]).$BuildRevision"
    updates = [ordered]@{
        enabled = $ReleaseChannel -eq 'Production'
        channel = $updateChannel
        feed = "$($updateConfiguration.FeedRoot)/$updateChannel.xml"
        publicKey = $updateConfiguration.PublicKey
        sdkVersion = $updateConfiguration.SdkVersion
        sdkSha256 = $updateConfiguration.SdkSha256
    }
    releaseChannel = $ReleaseChannel
    dataChannel = $dataChannel
    sourceCommit = $commit
    sourceTreeDirty = $sourceTreeDirty
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    singleFile = $true
    trimmed = $false
    youtubeClientIdSha256 = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.Encoding]::UTF8.GetBytes($YouTubeClientId)))
    advancedInstallerUri = $AdvancedInstallerUri
    userReportEndpoint = $userReportEndpoint
    signing = [ordered]@{
        mode = $SigningMode
        required = $ReleaseChannel -eq 'Production'
        expectedPublisher = $publisher
        timestampAuthority = if ($SigningMode -eq 'ArtifactSigning') { 'http://timestamp.acs.microsoft.com' } else { $null }
        files = @($signatureFiles)
    }
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    files = @($files)
}
$manifestPath = Join-Path $resolvedOutput 'release-manifest.json'
$manifest | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
& (Join-Path $PSScriptRoot 'Test-ReleaseDataBoundary.ps1') `
    -Profile Publish `
    -Path $resolvedOutput

Write-Host "Published Replay Foundry $Version to $resolvedOutput"
Write-Host "Application SHA-256: $((Get-FileHash -Algorithm SHA256 -LiteralPath $application).Hash)"
Write-Host "Manifest: $manifestPath"
