[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[^\s]+\.apps\.googleusercontent\.com$')]
    [string]$YouTubeClientId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string]$AdvancedInstallerUri,

    [string]$UserReportEndpoint,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Base', 'Advanced')]
    [string]$Profile,

    [Parameter(Mandatory = $true)]
    [string]$RuntimePackBuildRoot,

    [ValidateSet('Embedded', 'Online')]
    [string]$AdvancedPayloadMode = 'Embedded',

    [string]$AdvancedCatalogPath,

    [string]$ArtifactRoot,

    [string]$InnoCompilerPath,

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

    [string]$InstallerDownloadUri
)

$ErrorActionPreference = 'Stop'
$versionMatch = [regex]::Match(
    $Version,
    '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)')
if (-not $versionMatch.Success) {
    throw "Version '$Version' does not contain a numeric three-part file version."
}
$fileVersionParts = @(
    $versionMatch.Groups['major'].Value,
    $versionMatch.Groups['minor'].Value,
    $versionMatch.Groups['patch'].Value,
    '0')
if (@($fileVersionParts | Where-Object { [uint64]$_ -gt 65535 }).Count -ne 0) {
    throw "Version '$Version' contains a Windows file-version component above 65535."
}
$fileVersion = $fileVersionParts -join '.'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $repoRoot "artifacts\installer\$Version"
}
$resolvedArtifacts = [System.IO.Path]::GetFullPath($ArtifactRoot)
$publishDirectory = Join-Path $resolvedArtifacts 'app'
$installerDirectory = Join-Path $resolvedArtifacts 'installer'
$reportsDirectory = Join-Path $resolvedArtifacts 'reports'
$brandingDirectory = Join-Path $resolvedArtifacts 'branding'
$publisher = 'Expired Soda Studios LLC'
$youtubeCredentialTargetHash = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($YouTubeClientId.Trim())))
$youtubeCredentialTargetName = "ReplayFoundry/YouTube/$($youtubeCredentialTargetHash.Substring(0, 20))"
$sourceStatus = @(git -C $repoRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the source working tree.' }
$sourceTreeDirty = $sourceStatus.Count -ne 0
if ($ReleaseChannel -eq 'Production' -and $SigningMode -ne 'ArtifactSigning') {
    throw 'Production installers require Microsoft Artifact Signing. Use Development for an explicitly unsigned proof installer.'
}
if ($ReleaseChannel -eq 'Production' -and $sourceTreeDirty) {
    throw 'Production installers require a clean source working tree so the signed payload matches its recorded commit.'
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
if ($ReleaseChannel -eq 'Production' -and [string]::IsNullOrWhiteSpace($InstallerDownloadUri)) {
    throw 'Production installers require the final pinned HTTPS -InstallerDownloadUri for the release index.'
}
if (-not [string]::IsNullOrWhiteSpace($InstallerDownloadUri)) {
    $downloadUri = $null
    if (-not [Uri]::TryCreate($InstallerDownloadUri, [UriKind]::Absolute, [ref]$downloadUri) -or
        $downloadUri.Scheme -ne [Uri]::UriSchemeHttps -or
        -not [string]::IsNullOrEmpty($downloadUri.UserInfo) -or
        -not [string]::IsNullOrEmpty($downloadUri.Query) -or
        -not [string]::IsNullOrEmpty($downloadUri.Fragment)) {
        throw 'Installer download URI must be one fixed HTTPS URL without credentials, query, or fragment.'
    }
    $InstallerDownloadUri = $downloadUri.AbsoluteUri
}

$runtimePackRoot = [System.IO.Path]::GetFullPath($RuntimePackBuildRoot)
$packIndexPath = Join-Path $runtimePackRoot 'runtime-pack-build-index.json'
if (-not (Test-Path -LiteralPath $packIndexPath -PathType Leaf)) {
    throw "Runtime pack build index was not found: $packIndexPath"
}
$packIndex = Get-Content -Raw -LiteralPath $packIndexPath | ConvertFrom-Json
$offerAdvancedAi = $Profile -eq 'Base' -and -not [string]::IsNullOrWhiteSpace($AdvancedCatalogPath)
if ($packIndex.profile -ne $Profile -and -not ($offerAdvancedAi -and $packIndex.profile -eq 'Advanced')) {
    throw "Runtime pack profile '$($packIndex.profile)' does not match installer profile '$Profile'."
}
foreach ($pack in $packIndex.packs) {
    if (-not (Test-Path -LiteralPath $pack.archive -PathType Leaf) -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $pack.archive).Hash -ne $pack.sha256) {
        throw "Runtime pack archive failed index verification: $($pack.packageId)"
    }
}
if ($Profile -eq 'Advanced' -and $AdvancedPayloadMode -eq 'Embedded') {
    # Inno Setup requires disk spanning above 4.2 GB, which produces an EXE plus
    # external BIN slices rather than the promised one-file installer.
    $embeddedPayloadCeiling = 4000000000L
    $embeddedPayloadBytes = [long](($packIndex.packs | Measure-Object -Property byteLength -Sum).Sum)
    if ($embeddedPayloadBytes -gt $embeddedPayloadCeiling) {
        throw "The verified Advanced payload is $embeddedPayloadBytes bytes and cannot be emitted as one Inno Setup EXE. Use -AdvancedPayloadMode Online with a pinned HTTPS catalog."
    }
}
if (($Profile -eq 'Advanced' -and $AdvancedPayloadMode -eq 'Online') -or $offerAdvancedAi) {
    if ([string]::IsNullOrWhiteSpace($AdvancedCatalogPath) -or
        -not (Test-Path -LiteralPath $AdvancedCatalogPath -PathType Leaf)) {
        throw 'An online Advanced AI offer requires -AdvancedCatalogPath.'
    }
    $AdvancedCatalogPath = [System.IO.Path]::GetFullPath($AdvancedCatalogPath)
}

$sourceCommit = (git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to identify the source commit.' }
if ($SigningMode -eq 'ArtifactSigning' -and [string]::IsNullOrWhiteSpace($SigningCorrelationId)) {
    $SigningCorrelationId = "ReplayFoundry-$Version-$Profile-$($sourceCommit.Substring(0, 12))"
}
$publishArguments = @{
    Version = $Version
    YouTubeClientId = $YouTubeClientId
    AdvancedInstallerUri = $AdvancedInstallerUri
    OutputDirectory = $publishDirectory
    ReleaseChannel = $ReleaseChannel
    SigningMode = $SigningMode
    ArtifactSigningAuthenticationMode = $ArtifactSigningAuthenticationMode
    SigningReportPath = (Join-Path $reportsDirectory 'application-signing-report.json')
}
if (-not [string]::IsNullOrWhiteSpace($UserReportEndpoint)) { $publishArguments.UserReportEndpoint = $UserReportEndpoint }
if (-not [string]::IsNullOrWhiteSpace($ArtifactSigningEndpoint)) { $publishArguments.ArtifactSigningEndpoint = $ArtifactSigningEndpoint }
if (-not [string]::IsNullOrWhiteSpace($ArtifactSigningAccountName)) { $publishArguments.ArtifactSigningAccountName = $ArtifactSigningAccountName }
if (-not [string]::IsNullOrWhiteSpace($ArtifactSigningCertificateProfileName)) { $publishArguments.ArtifactSigningCertificateProfileName = $ArtifactSigningCertificateProfileName }
if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) { $publishArguments.SignToolPath = $SignToolPath }
if (-not [string]::IsNullOrWhiteSpace($ArtifactSigningDlibPath)) { $publishArguments.ArtifactSigningDlibPath = $ArtifactSigningDlibPath }
if (-not [string]::IsNullOrWhiteSpace($SigningCorrelationId)) { $publishArguments.SigningCorrelationId = $SigningCorrelationId }
& (Join-Path $PSScriptRoot 'Publish-ReplayFoundryWindows.ps1') @publishArguments

if (-not [string]::IsNullOrWhiteSpace($AdvancedCatalogPath)) {
    $runtimeInstaller = Join-Path $publishDirectory 'Tools\RuntimeInstaller\ReplayFoundry.RuntimeInstaller.exe'
    & $runtimeInstaller verify-catalog --catalog $AdvancedCatalogPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The Advanced AI catalog failed the production runtime installer validation.'
    }
    $advancedCatalog = Get-Content -Raw -LiteralPath $AdvancedCatalogPath | ConvertFrom-Json
    if ($advancedCatalog.profile -ne 'Advanced') {
        throw "The Advanced AI offer requires an Advanced catalog, not '$($advancedCatalog.profile)'."
    }
}

& (Join-Path $PSScriptRoot 'New-ReplayFoundryInstallerBranding.ps1') `
    -OutputDirectory $brandingDirectory
$brandingManifestPath = Join-Path $brandingDirectory 'installer-branding-manifest.json'
$brandingManifest = Get-Content -Raw -LiteralPath $brandingManifestPath | ConvertFrom-Json
$wizardBackImagePath = Join-Path $brandingDirectory 'installer-wizard-background.png'
$wizardSmallImagePath = Join-Path $brandingDirectory 'installer-wizard-small.png'
foreach ($brandingOutput in $brandingManifest.outputs) {
    $brandingPath = Join-Path $brandingDirectory $brandingOutput.fileName
    if (-not (Test-Path -LiteralPath $brandingPath -PathType Leaf) -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $brandingPath).Hash -ne $brandingOutput.sha256) {
        throw "Generated installer branding failed manifest verification: $($brandingOutput.role)"
    }
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $candidates = [Collections.Generic.List[string]]::new()
    foreach ($candidate in @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) { $candidates.Add($candidate) }
    }
    foreach ($uninstallRoot in @(
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')) {
        foreach ($entry in Get-ItemProperty $uninstallRoot -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like 'Inno Setup version *' -and -not [string]::IsNullOrWhiteSpace($_.InstallLocation) }) {
            $candidates.Add((Join-Path $entry.InstallLocation 'ISCC.exe'))
        }
    }
    $InnoCompilerPath = $candidates |
        Select-Object -Unique |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or
    -not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
    throw 'A supported official Inno Setup compiler was not found. Install current Inno Setup 6.7 or 7, or pass -InnoCompilerPath.'
}

New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null
$script = Join-Path $repoRoot 'installer\ReplayFoundry.iss'
$innoArguments = @(
    "/DMyAppVersion=$Version",
    "/DMyAppFileVersion=$fileVersion",
    "/DPublishDir=$publishDirectory",
    "/DRepoRoot=$repoRoot",
    "/DInstallerOutputDir=$installerDirectory",
    "/DInstallerProfile=$Profile",
    "/DRuntimePackBuildRoot=$runtimePackRoot",
    "/DAdvancedPayloadMode=$AdvancedPayloadMode",
    "/DAdvancedCatalogPath=$AdvancedCatalogPath",
    "/DOfferAdvancedAi=$([int]$offerAdvancedAi)",
    "/DWizardBackImagePath=$wizardBackImagePath",
    "/DWizardSmallImagePath=$wizardSmallImagePath",
    "/DYouTubeCredentialTargetName=$youtubeCredentialTargetName"
)
if ($SigningMode -eq 'ArtifactSigning') {
    $signerScript = Join-Path $PSScriptRoot 'Invoke-ReplayFoundryArtifactSigning.ps1'
    $powerShellHost = (Get-Process -Id $PID).Path
    if ([IO.Path]::GetFileName($powerShellHost) -ne 'pwsh.exe') {
        throw 'Artifact-signed installer builds require PowerShell 7 (pwsh.exe).'
    }
    foreach ($commandPath in @($powerShellHost, $signerScript, $SignToolPath, $ArtifactSigningDlibPath)) {
        if (-not [string]::IsNullOrWhiteSpace($commandPath) -and $commandPath.Contains('$', [StringComparison]::Ordinal)) {
            throw "Artifact Signing command paths cannot contain '$': $commandPath"
        }
    }
    $signCommand = '$q' + $powerShellHost + '$q -NoLogo -NoProfile -NonInteractive -File $q' +
        $signerScript + '$q -Path $f -Endpoint $q' + $ArtifactSigningEndpoint +
        '$q -CodeSigningAccountName $q' + $ArtifactSigningAccountName +
        '$q -CertificateProfileName $q' + $ArtifactSigningCertificateProfileName +
        '$q -AuthenticationMode ' + $ArtifactSigningAuthenticationMode +
        ' -CorrelationId $q' + $SigningCorrelationId + '$q'
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        $signCommand += ' -SignToolPath $q' + [IO.Path]::GetFullPath($SignToolPath) + '$q'
    }
    if (-not [string]::IsNullOrWhiteSpace($ArtifactSigningDlibPath)) {
        $signCommand += ' -DlibPath $q' + [IO.Path]::GetFullPath($ArtifactSigningDlibPath) + '$q'
    }
    $innoArguments += '/DReplayFoundrySignToolName=replayfoundry-artifact-signing'
    $innoArguments += '/Sreplayfoundry-artifact-signing=' + $signCommand
}
$innoArguments += $script
& $InnoCompilerPath @innoArguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installer = Get-ChildItem -LiteralPath $installerDirectory -Filter '*.exe' |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($null -eq $installer) {
    throw 'The installer executable was not produced.'
}

if ($SigningMode -eq 'ArtifactSigning') {
    $verificationArguments = @{
        Path = $installer.FullName
        VerifyOnly = $true
        ExpectedPublisher = $publisher
        ReportPath = (Join-Path $reportsDirectory 'installer-signing-report.json')
    }
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) { $verificationArguments.SignToolPath = $SignToolPath }
    & (Join-Path $PSScriptRoot 'Invoke-ReplayFoundryArtifactSigning.ps1') @verificationArguments | Out-Null
}

$installerSignature = Get-AuthenticodeSignature -LiteralPath $installer.FullName
$appManifestPath = Join-Path $publishDirectory 'release-manifest.json'
$installerManifest = [ordered]@{
    schemaVersion = 'replayfoundry-installer-release-manifest-1.1'
    productVersion = $Version
    releaseChannel = $ReleaseChannel
    profile = $Profile
    sourceCommit = $sourceCommit
    sourceTreeDirty = $sourceTreeDirty
    publisher = $publisher
    installer = [ordered]@{
        fileName = $installer.Name
        downloadUri = if ([string]::IsNullOrWhiteSpace($InstallerDownloadUri)) { $null } else { $InstallerDownloadUri }
        byteLength = $installer.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer.FullName).Hash
    }
    appReleaseManifest = [ordered]@{
        fileName = 'release-manifest.json'
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $appManifestPath).Hash
    }
    runtimePackBuildIndexSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $packIndexPath).Hash
    installerBrandingManifestSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $brandingManifestPath).Hash
    installerBranding = $brandingManifest
    advancedCatalogSha256 = if ([string]::IsNullOrWhiteSpace($AdvancedCatalogPath)) { $null } else { (Get-FileHash -Algorithm SHA256 -LiteralPath $AdvancedCatalogPath).Hash }
    advancedAi = [ordered]@{
        offering = if ($offerAdvancedAi) { 'Optional' } elseif ($Profile -eq 'Advanced') { 'Required' } else { 'Unavailable' }
        selectedByDefault = $Profile -eq 'Advanced'
        payloadMode = if ([string]::IsNullOrWhiteSpace($AdvancedCatalogPath)) { $AdvancedPayloadMode } else { 'Online' }
    }
    signing = [ordered]@{
        mode = $SigningMode
        required = $ReleaseChannel -eq 'Production'
        status = $installerSignature.Status.ToString()
        signerSubject = if ($null -eq $installerSignature.SignerCertificate) { $null } else { $installerSignature.SignerCertificate.Subject }
        signerThumbprint = if ($null -eq $installerSignature.SignerCertificate) { $null } else { $installerSignature.SignerCertificate.Thumbprint }
        timestampSubject = if ($null -eq $installerSignature.TimeStamperCertificate) { $null } else { $installerSignature.TimeStamperCertificate.Subject }
        timestampAuthority = if ($SigningMode -eq 'ArtifactSigning') { 'http://timestamp.acs.microsoft.com' } else { $null }
    }
    signingReports = [ordered]@{
        application = if (Test-Path -LiteralPath (Join-Path $reportsDirectory 'application-signing-report.json')) {
            (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $reportsDirectory 'application-signing-report.json')).Hash
        } else { $null }
        installer = if (Test-Path -LiteralPath (Join-Path $reportsDirectory 'installer-signing-report.json')) {
            (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $reportsDirectory 'installer-signing-report.json')).Hash
        } else { $null }
    }
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
$installerManifestPath = Join-Path $resolvedArtifacts 'installer-release-manifest.json'
$installerManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $installerManifestPath -Encoding utf8NoBOM
Write-Host "Installer: $($installer.FullName)"
Write-Host "Installer SHA-256: $((Get-FileHash -Algorithm SHA256 -LiteralPath $installer.FullName).Hash)"
Write-Host "Release manifest: $installerManifestPath"
if ($SigningMode -eq 'Unsigned') {
    Write-Warning 'This is an explicitly unsigned Development installer. It is not eligible for public release.'
}
