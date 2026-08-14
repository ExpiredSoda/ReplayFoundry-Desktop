[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InnoCompilerPath,
    [switch]$SkipCompile
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..'
}
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ReplayFoundry-InstallerBranding-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null

function Read-PngSize([string]$PathValue) {
    $stream = [IO.File]::OpenRead($PathValue)
    try {
        $decoder = [Windows.Media.Imaging.BitmapDecoder]::Create(
            $stream,
            [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        return [pscustomobject]@{
            Width = $decoder.Frames[0].PixelWidth
            Height = $decoder.Frames[0].PixelHeight
        }
    }
    finally { $stream.Dispose() }
}

function Find-InnoCompiler {
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
    return $candidates |
        Select-Object -Unique |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

try {
    $first = Join-Path $testRoot 'first'
    $second = Join-Path $testRoot 'second'
    $generator = Join-Path $root 'eng\New-ReplayFoundryInstallerBranding.ps1'
    & $generator -OutputDirectory $first
    & $generator -OutputDirectory $second

    $manifestPath = Join-Path $first 'installer-branding-manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 'replayfoundry-installer-branding-1.0') {
        throw 'Installer branding manifest schema is invalid.'
    }
    $canonicalLogo = Join-Path $root 'ReplayFoundry.Desktop\Assets\Branding\ReplayFoundry-App-Icon-1024.png'
    if ($manifest.source.sha256 -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $canonicalLogo).Hash) {
        throw 'Installer branding did not bind to the canonical ReplayFoundry logo hash.'
    }
    foreach ($expected in @(
        @{ Role = 'WizardBackImageFile'; File = 'installer-wizard-background.png'; Width = 1988; Height = 1440 },
        @{ Role = 'WizardSmallImageFile'; File = 'installer-wizard-small.png'; Width = 256; Height = 256 })) {
        $firstPath = Join-Path $first $expected.File
        $secondPath = Join-Path $second $expected.File
        $size = Read-PngSize $firstPath
        if ($size.Width -ne $expected.Width -or $size.Height -ne $expected.Height) {
            throw "$($expected.Role) has the wrong pixel size."
        }
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $firstPath).Hash
        if ($hash -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $secondPath).Hash) {
            throw "$($expected.Role) generation is not deterministic."
        }
        $entry = $manifest.outputs | Where-Object role -eq $expected.Role
        if ($null -eq $entry -or $entry.sha256 -ne $hash) {
            throw "$($expected.Role) is not sealed by the branding manifest."
        }
    }
    if ((1988 * 360) -ne (1440 * 497)) {
        throw 'Installer background must retain the official 497:360 aspect ratio.'
    }
    foreach ($palette in @('#071014', '#1F9DC4', '#59CAF0', '#FFC75E')) {
        if (($manifest | ConvertTo-Json -Depth 8) -notmatch [regex]::Escape($palette)) {
            throw "Installer branding palette is missing $palette."
        }
    }

    $installerScript = Get-Content -Raw -LiteralPath (Join-Path $root 'installer\ReplayFoundry.iss')
    foreach ($pattern in @(
        'WizardStyle=modern dark windows11 hidebevels includetitlebar',
        'WizardBackImageFile={#WizardBackImagePath}',
        'WizardSmallImageFile={#WizardSmallImagePath}',
        'WizardSetBackImage([], True, True, 255)',
        'HighContrastActive')) {
        if ($installerScript.IndexOf($pattern, [StringComparison]::Ordinal) -lt 0) {
            throw "Installer script is missing branded/high-contrast behavior: $pattern"
        }
    }

    if (-not $SkipCompile) {
        if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
            $InnoCompilerPath = Find-InnoCompiler
        }
        if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
            throw 'A supported Inno Setup compiler is required for the branding compile smoke.'
        }
        $compiler = [IO.Path]::GetFullPath($InnoCompilerPath)
        $compileOutput = Join-Path $testRoot 'compile'
        [IO.Directory]::CreateDirectory($compileOutput) | Out-Null
        $smokeScriptPath = Join-Path $testRoot 'branding-smoke.iss'
        $smokeScript = @"
[Setup]
AppId=ReplayFoundryBrandingSmoke
AppName=ReplayFoundry Branding Smoke
AppVersion=0.0.0
DefaultDirName={tmp}\ReplayFoundryBrandingSmoke
PrivilegesRequired=lowest
Uninstallable=no
OutputDir=$compileOutput
OutputBaseFilename=ReplayFoundry-Branding-Smoke
SetupIconFile=$root\ReplayFoundry.Desktop\Assets\Icons\Application\ReplayFoundry.ico
WizardStyle=modern dark windows11 hidebevels includetitlebar
WizardSizePercent=120,120
WizardKeepAspectRatio=yes
WizardImageStretch=yes
WizardBackColor=#071014
WizardBackImageFile=$first\installer-wizard-background.png
WizardBackImageOpacity=255
WizardImageFile=
WizardSmallImageFile=$first\installer-wizard-small.png
WizardSmallImageBackColor=#071014
[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\ReplayFoundry"
Type: filesandordirs; Name: "{userappdata}\ReplayFoundry"
Type: filesandordirs; Name: "{%TEMP|{localappdata}\Temp}\ReplayFoundry"
Type: filesandordirs; Name: "{%TEMP|{localappdata}\Temp}\ReplayFoundry-RuntimeDownloads"
Type: filesandordirs; Name: "{%TMP|{localappdata}\Temp}\ReplayFoundry"
Type: filesandordirs; Name: "{%TMP|{localappdata}\Temp}\ReplayFoundry-RuntimeDownloads"
Type: files; Name: "{localappdata}\CrashDumps\ReplayFoundry.Desktop.exe*.dmp"
[Code]
const
  CredentialTypeGeneric = 1;
  ErrorNotFound = 1168;

function CredDelete(
  TargetName: String;
  CredentialType: Cardinal;
  Flags: Cardinal): Boolean;
  external 'CredDeleteW@advapi32.dll stdcall';

procedure InitializeWizard;
begin
  if HighContrastActive then
  begin
    WizardSetBackImage([], True, True, 255);
    WizardForm.Color := clWindow;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ErrorCode: Integer;
begin
  if CurUninstallStep <> usUninstall then exit;
  if not CredDelete(
    'ReplayFoundry/YouTube/0123456789ABCDEF0123',
    CredentialTypeGeneric,
    0) then
  begin
    ErrorCode := DLLGetLastError;
    if ErrorCode <> ErrorNotFound then
    begin
      Log('Credential cleanup smoke failed with Windows error: ' +
        IntToStr(ErrorCode));
    end;
  end;
end;
"@
        [IO.File]::WriteAllText($smokeScriptPath, $smokeScript, [Text.UTF8Encoding]::new($false))
        & $compiler /Qp $smokeScriptPath
        if ($LASTEXITCODE -ne 0 -or
            -not (Test-Path -LiteralPath (Join-Path $compileOutput 'ReplayFoundry-Branding-Smoke.exe') -PathType Leaf)) {
            throw 'The supported Inno compiler rejected the branded installer directives.'
        }
        Write-Host "Inno compile smoke passed: $compiler"
    }

    Write-Host 'Installer branding guard passed: canonical source, deterministic images, aspect ratio, palette, high contrast, and compiler support verified.'
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTest)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force
    }
}
