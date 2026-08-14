[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [switch]$RequireArtifactSigningClient,
    [string]$ArtifactSigningDlibPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..'
}
$root = [IO.Path]::GetFullPath($RepositoryRoot)

$scripts = @(
    'eng\Publish-ReplayFoundryWindows.ps1',
    'eng\Build-ReplayFoundryInstaller.ps1',
    'eng\New-ReplayFoundryInstallerBranding.ps1',
    'eng\Test-InstallerBranding.ps1',
    'eng\Invoke-ReplayFoundryArtifactSigning.ps1',
    'eng\Resolve-ReplayFoundryArtifactSigningClient.ps1'
)
foreach ($relative in $scripts) {
    $path = Join-Path $root $relative
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -ne 0) {
        throw "$relative has PowerShell parser errors: $($errors.Message -join '; ')"
    }
}

$installer = Get-Content -Raw -LiteralPath (Join-Path $root 'installer\ReplayFoundry.iss')
foreach ($required in @(
    '#define MyAppPublisher "Expired Soda Studios LLC"',
    '#ifndef MyAppFileVersion',
    'AppPublisherURL=https://replayfoundry.com/',
    'AppSupportURL=https://replayfoundry.com/support',
    'AppUpdatesURL=https://replayfoundry.com/download',
    'WizardStyle=modern dark windows11 hidebevels includetitlebar',
    'WizardBackImageFile={#WizardBackImagePath}',
    'WizardSmallImageFile={#WizardSmallImagePath}',
    'VersionInfoVersion={#MyAppFileVersion}',
    'HighContrastActive',
    'SignedUninstaller=yes',
    'Type: files; Name: "{autoprograms}\Replay Foundry.lnk"',
    'Type: files; Name: "{autodesktop}\Replay Foundry.lnk"',
    'Type: filesandordirs; Name: "{localappdata}\ReplayFoundry"',
    'Type: filesandordirs; Name: "{userappdata}\ReplayFoundry"',
    'Type: filesandordirs; Name: "{%TEMP|{localappdata}\Temp}\ReplayFoundry"',
    'Type: filesandordirs; Name: "{%TEMP|{localappdata}\Temp}\ReplayFoundry-RuntimeDownloads"',
    'Type: filesandordirs; Name: "{%TMP|{localappdata}\Temp}\ReplayFoundry"',
    'Type: filesandordirs; Name: "{%TMP|{localappdata}\Temp}\ReplayFoundry-RuntimeDownloads"',
    'Type: files; Name: "{localappdata}\CrashDumps\ReplayFoundry.Desktop.exe*.dmp"',
    "external 'CredDeleteW@advapi32.dll stdcall';",
    "CredDelete('{#YouTubeCredentialTargetName}', CredentialTypeGeneric, 0)",
    "if not CopyFile(ExpandConstant('{srcexe}'), ExpandConstant('{localappdata}\ReplayFoundry\Installers\ReplayFoundry-{#InstallerProfile}-Setup.exe'), False) then",
    "RaiseException('Unable to retain the current ReplayFoundry installer for repair.');")) {
    if (-not $installer.Contains($required, [StringComparison]::Ordinal)) {
        throw "Installer is missing release metadata: $required"
    }
}
if ($installer.Contains(
        "CopyFile(ExpandConstant('{srcexe}'), ExpandConstant('{localappdata}\ReplayFoundry\Installers\ReplayFoundry-{#InstallerProfile}-Setup.exe'), True)",
        [StringComparison]::Ordinal)) {
    throw 'The retained installer copy must overwrite the prior profile installer during an in-place upgrade.'
}
if ($installer -match '\[LEGAL PUBLISHER NAME\]|YOUR-DOMAIN\.example') {
    throw 'Installer still contains public-release placeholders.'
}
foreach ($unsafeTarget in @(
    'Type: filesandordirs; Name: "{localappdata}"',
    'Type: filesandordirs; Name: "{userappdata}"',
    'Type: filesandordirs; Name: "{%TEMP}"',
    'Type: filesandordirs; Name: "{%TMP}"',
    'Type: filesandordirs; Name: "{userprofile}"',
    'Type: filesandordirs; Name: "{userprofile}\Videos"',
    'Type: filesandordirs; Name: "{userprofile}\Videos\ReplayFoundry"')) {
    if ($installer.Contains($unsafeTarget, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer cleanup escaped a focused ReplayFoundry-owned root: $unsafeTarget"
    }
}

$productionRejected = $false
try {
    & (Join-Path $root 'eng\Build-ReplayFoundryInstaller.ps1') `
        -Version 0.0.0 `
        -YouTubeClientId test.apps.googleusercontent.com `
        -AdvancedInstallerUri https://replayfoundry.com/download `
        -Profile Base `
        -RuntimePackBuildRoot (Join-Path $root 'missing-test-runtime-packs') `
        -ArtifactRoot (Join-Path ([IO.Path]::GetTempPath()) 'ReplayFoundry-ReleaseGuard-NeverCreated') `
        -ReleaseChannel Production `
        -SigningMode Unsigned
} catch {
    $productionRejected = $_.Exception.Message -match 'Production installers require Microsoft Artifact Signing'
}
if (-not $productionRejected) {
    throw 'Unsigned Production installer was not rejected before build or packaging.'
}

$missingSignerRejected = $false
try {
    & (Join-Path $root 'eng\Build-ReplayFoundryInstaller.ps1') `
        -Version 0.0.0 `
        -YouTubeClientId test.apps.googleusercontent.com `
        -AdvancedInstallerUri https://replayfoundry.com/download `
        -Profile Base `
        -RuntimePackBuildRoot (Join-Path $root 'missing-test-runtime-packs') `
        -ArtifactRoot (Join-Path ([IO.Path]::GetTempPath()) 'ReplayFoundry-ReleaseGuard-NeverCreated') `
        -ReleaseChannel Development `
        -SigningMode ArtifactSigning
} catch {
    $missingSignerRejected = $_.Exception.Message -match 'ArtifactSigningEndpoint is required'
}
if (-not $missingSignerRejected) {
    throw 'An Artifact Signing build without a complete account identity was not rejected before build or packaging.'
}

$trackedSensitive = @(git -C $root ls-files '*.pfx' '*.p12' '*.pvk' '*.key' '*artifact-signing-metadata*.json')
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect tracked release files.' }
if ($trackedSensitive.Count -ne 0) {
    throw "Signing credentials or environment metadata must not be tracked: $($trackedSensitive -join ', ')"
}

$publisherScript = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\Publish-ReplayFoundryWindows.ps1')
$installerScript = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\Build-ReplayFoundryInstaller.ps1')
$brandingScript = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\New-ReplayFoundryInstallerBranding.ps1')
foreach ($scriptText in @($publisherScript, $installerScript)) {
    if ($scriptText -notmatch 'status --porcelain=v1 --untracked-files=all' -or
        $scriptText -notmatch 'Production.*clean source working tree' -or
        $scriptText -notmatch 'sourceTreeDirty') {
        throw 'Production release scripts must reject a dirty checkout and record Development source state.'
    }
}
if ($installerScript -notmatch 'Get-Process -Id \$PID' -or
    $installerScript -notmatch 'require PowerShell 7 \(pwsh\.exe\)') {
    throw 'Inno Setup signing must invoke the exact PowerShell 7 host used by the release build.'
}
foreach ($requiredInstallerBuildPattern in @(
    'New-ReplayFoundryInstallerBranding\.ps1',
    'installer-branding-manifest\.json',
    'ReplayFoundry/YouTube/\$\(\$youtubeCredentialTargetHash\.Substring\(0, 20\)\)',
    '/DYouTubeCredentialTargetName=\$youtubeCredentialTargetName',
    '/DMyAppFileVersion=\$fileVersion',
    '\$fileVersionParts = @\(',
    'Programs\\Inno\\ISCC\.exe',
    'Inno Setup 7\\ISCC\.exe')) {
    if ($installerScript -notmatch $requiredInstallerBuildPattern) {
        throw "Installer build is missing branding/compiler compatibility: $requiredInstallerBuildPattern"
    }
}
if (($brandingScript + $installerScript + $installer) -match 'InnoLicense(Key)?|CommercialLicense(Key)?') {
    throw 'Installer branding/build code must never accept or persist an Inno commercial license key.'
}

$signTool = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') `
    -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '[\\/]x64[\\/]signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $signTool) { throw 'Windows SDK x64 SignTool is unavailable.' }
$minimumSignToolVersion = [Version]'10.0.2261.755'
$signToolProductVersion = [regex]::Match($signTool.VersionInfo.ProductVersion ?? '', '\d+\.\d+\.\d+\.\d+').Value
if ([string]::IsNullOrWhiteSpace($signToolProductVersion) -or
    [Version]$signToolProductVersion -lt $minimumSignToolVersion) {
    throw "Windows SDK SignTool $signToolProductVersion does not meet the Artifact Signing minimum $minimumSignToolVersion."
}

if ($RequireArtifactSigningClient) {
    . (Join-Path $root 'eng\Resolve-ReplayFoundryArtifactSigningClient.ps1')
    $dlib = Resolve-ReplayFoundryArtifactSigningDlib $ArtifactSigningDlibPath
    Write-Output "Artifact Signing dlib SHA-256: $((Get-FileHash -Algorithm SHA256 -LiteralPath $dlib.FullName).Hash)"
}

Write-Output "Release engineering guard passed with SignTool $signToolProductVersion."
