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

function Read-PngPixels([string]$PathValue) {
    $stream = [IO.File]::OpenRead($PathValue)
    try {
        $decoder = [Windows.Media.Imaging.BitmapDecoder]::Create(
            $stream,
            [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $converted = [Windows.Media.Imaging.FormatConvertedBitmap]::new(
            $decoder.Frames[0],
            [Windows.Media.PixelFormats]::Bgra32,
            $null,
            0)
        $stride = $converted.PixelWidth * 4
        $pixels = [byte[]]::new($stride * $converted.PixelHeight)
        $converted.CopyPixels($pixels, $stride, 0)
        return [pscustomobject]@{
            Width = $converted.PixelWidth
            Height = $converted.PixelHeight
            Stride = $stride
            Pixels = $pixels
        }
    }
    finally { $stream.Dispose() }
}

function Convert-HexColorToBgra([string]$HexColor) {
    if ($HexColor -cnotmatch '^#[0-9A-F]{6}$') {
        throw "Expected an uppercase #RRGGBB color, received '$HexColor'."
    }
    return [byte[]]@(
        [Convert]::ToByte($HexColor.Substring(5, 2), 16),
        [Convert]::ToByte($HexColor.Substring(3, 2), 16),
        [Convert]::ToByte($HexColor.Substring(1, 2), 16),
        255)
}

function Assert-PngRegionColor(
    [pscustomobject]$Image,
    [int]$X,
    [int]$Y,
    [int]$Width,
    [int]$Height,
    [string]$HexColor,
    [string]$Label) {
    if ($X -lt 0 -or $Y -lt 0 -or $Width -lt 1 -or $Height -lt 1 -or
        ($X + $Width) -gt $Image.Width -or ($Y + $Height) -gt $Image.Height) {
        throw "$Label uses an invalid pixel region."
    }
    $bgra = Convert-HexColorToBgra $HexColor
    $difference = [ReplayFoundryInstallerBrandingPixelAssertions]::FindFirstDifference(
        $Image.Pixels,
        $Image.Stride,
        $X,
        $Y,
        $Width,
        $Height,
        $bgra[0],
        $bgra[1],
        $bgra[2],
        $bgra[3])
    if ($null -ne $difference) {
        throw "$Label must remain $HexColor throughout; first unexpected pixel: $difference"
    }
}

function Assert-PngPixelNotColor(
    [pscustomobject]$Image,
    [int]$X,
    [int]$Y,
    [string]$HexColor,
    [string]$Label) {
    if ($X -lt 0 -or $Y -lt 0 -or $X -ge $Image.Width -or $Y -ge $Image.Height) {
        throw "$Label uses an invalid pixel coordinate."
    }
    $bgra = Convert-HexColorToBgra $HexColor
    $offset = ($Y * $Image.Stride) + ($X * 4)
    if ($Image.Pixels[$offset] -eq $bgra[0] -and
        $Image.Pixels[$offset + 1] -eq $bgra[1] -and
        $Image.Pixels[$offset + 2] -eq $bgra[2] -and
        $Image.Pixels[$offset + 3] -eq $bgra[3]) {
        throw "$Label must contain visible canonical decoration rather than $HexColor."
    }
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
if (-not ('ReplayFoundryInstallerBrandingPixelAssertions' -as [type])) {
    Add-Type -TypeDefinition @'
public static class ReplayFoundryInstallerBrandingPixelAssertions
{
    public static string FindFirstDifference(
        byte[] pixels,
        int stride,
        int x,
        int y,
        int width,
        int height,
        byte blue,
        byte green,
        byte red,
        byte alpha)
    {
        for (var row = y; row < y + height; row++)
        {
            var offset = (row * stride) + (x * 4);
            for (var column = x; column < x + width; column++, offset += 4)
            {
                if (pixels[offset] != blue ||
                    pixels[offset + 1] != green ||
                    pixels[offset + 2] != red ||
                    pixels[offset + 3] != alpha)
                {
                    return string.Format(
                        "({0},{1}) was #{2:X2}{3:X2}{4:X2} with alpha {5}",
                        column,
                        row,
                        pixels[offset + 2],
                        pixels[offset + 1],
                        pixels[offset],
                        pixels[offset + 3]);
                }
            }
        }
        return null;
    }
}
'@
}

try {
    $first = Join-Path $testRoot 'first'
    $second = Join-Path $testRoot 'second'
    $generator = Join-Path $root 'eng\New-ReplayFoundryInstallerBranding.ps1'
    & $generator -OutputDirectory $first
    & $generator -OutputDirectory $second

    $manifestPath = Join-Path $first 'installer-branding-manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.schemaVersion -cne 'replayfoundry-installer-branding-1.1') {
        throw 'Installer branding manifest schema is invalid.'
    }
    $canonicalLogo = Join-Path $root 'src\ReplayFoundry.Desktop\Assets\Branding\ReplayFoundry-App-Icon-1024.png'
    if ($manifest.source.relativePath -cne 'src/ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png' -or
        $manifest.source.width -ne 1024 -or
        $manifest.source.height -ne 1024 -or
        $manifest.source.sha256 -cne (Get-FileHash -Algorithm SHA256 -LiteralPath $canonicalLogo).Hash) {
        throw 'Installer branding did not bind to the canonical ReplayFoundry logo hash.'
    }
    $expectedOutputs = @(
        @{ Role = 'WizardBackImageFile'; File = 'installer-wizard-background.png'; Width = 1988; Height = 1440 },
        @{ Role = 'WizardImageFile'; File = 'installer-wizard-hero.png'; Width = 656; Height = 1256 },
        @{ Role = 'WizardSmallImageFile'; File = 'installer-wizard-small.png'; Width = 256; Height = 256 })
    if (@($manifest.outputs).Count -ne $expectedOutputs.Count) {
        throw 'Installer branding manifest must seal exactly the background, hero, and small images.'
    }
    foreach ($expected in $expectedOutputs) {
        $firstPath = Join-Path $first $expected.File
        $secondPath = Join-Path $second $expected.File
        if (-not (Test-Path -LiteralPath $firstPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $secondPath -PathType Leaf)) {
            throw "$($expected.Role) was not generated in both deterministic passes."
        }
        $size = Read-PngSize $firstPath
        if ($size.Width -ne $expected.Width -or $size.Height -ne $expected.Height) {
            throw "$($expected.Role) has the wrong pixel size."
        }
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $firstPath).Hash
        if ($hash -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $secondPath).Hash) {
            throw "$($expected.Role) generation is not deterministic."
        }
        $entry = @($manifest.outputs | Where-Object role -ceq $expected.Role)
        if ($entry.Count -ne 1 -or
            $entry[0].fileName -cne $expected.File -or
            $entry[0].width -ne $expected.Width -or
            $entry[0].height -ne $expected.Height -or
            $entry[0].sha256 -cne $hash) {
            throw "$($expected.Role) is not sealed by the branding manifest."
        }
    }
    $secondManifestPath = Join-Path $second 'installer-branding-manifest.json'
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash -cne
        (Get-FileHash -Algorithm SHA256 -LiteralPath $secondManifestPath).Hash) {
        throw 'Installer branding manifest generation is not deterministic.'
    }
    if ((1988 * 360) -ne (1440 * 497)) {
        throw 'Installer background must retain the official 497:360 aspect ratio.'
    }
    if ((656 * 314) -ne (1256 * 164)) {
        throw 'Installer hero must retain the official 164:314 WizardImageFile aspect ratio.'
    }
    $expectedPalette = [ordered]@{
        ink = '#071014'
        blue = '#1599C8'
        cyan = '#58D6FF'
        yellow = '#FFC85A'
    }
    $paletteProperties = @($manifest.palette.PSObject.Properties)
    if ($paletteProperties.Count -ne $expectedPalette.Count) {
        throw 'Installer branding manifest contains an unexpected palette shape.'
    }
    foreach ($palette in $expectedPalette.GetEnumerator()) {
        if ([string]$manifest.palette.($palette.Key) -cne $palette.Value) {
            throw "Installer branding palette $($palette.Key) must be exactly $($palette.Value)."
        }
    }

    $background = Read-PngPixels (Join-Path $first 'installer-wizard-background.png')
    # Native wizard copy and controls occupy the large middle and footer area.
    # This inclusive x=146..1905, y=100..1439 contract prevents artwork from
    # returning behind page text, progress, or Back/Next/Cancel controls.
    Assert-PngRegionColor $background 146 100 1760 1340 $expectedPalette.ink `
        'Installer background content and footer safe zone'
    Assert-PngRegionColor $background 100 90 1 1 $expectedPalette.yellow `
        'Installer background canonical yellow registration mark'
    Assert-PngPixelNotColor $background 200 72 $expectedPalette.ink `
        'Installer background left edge rail'
    Assert-PngPixelNotColor $background 1910 200 $expectedPalette.ink `
        'Installer background right edge rail'

    $hero = Read-PngPixels (Join-Path $first 'installer-wizard-hero.png')
    Assert-PngRegionColor $hero 90 750 1 1 $expectedPalette.blue `
        'Installer hero canonical blue step mark'
    Assert-PngRegionColor $hero 90 870 1 1 $expectedPalette.cyan `
        'Installer hero canonical cyan step mark'
    Assert-PngRegionColor $hero 90 994 1 1 $expectedPalette.yellow `
        'Installer hero canonical yellow step mark'

    $small = Read-PngPixels (Join-Path $first 'installer-wizard-small.png')
    Assert-PngRegionColor $small 0 0 256 26 $expectedPalette.ink `
        'Installer small image top breathing room'
    Assert-PngRegionColor $small 0 230 256 26 $expectedPalette.ink `
        'Installer small image bottom breathing room'
    Assert-PngRegionColor $small 0 26 26 204 $expectedPalette.ink `
        'Installer small image left breathing room'
    Assert-PngRegionColor $small 230 26 26 204 $expectedPalette.ink `
        'Installer small image right breathing room'

    $installerScript = Get-Content -Raw -LiteralPath (Join-Path $root 'installer\ReplayFoundry.iss')
    foreach ($pattern in @(
        'WizardStyle=modern dark windows11 hidebevels includetitlebar',
        '#define MyAppName "Replay Foundry"',
        '#ifndef WizardImagePath',
        '#error WizardImagePath must be supplied by Build-ReplayFoundryInstaller.ps1',
        'DisableWelcomePage=no',
        'UsePreviousTasks=no',
        'WizardSizePercent=120,120',
        'WizardKeepAspectRatio=yes',
        'WizardImageStretch=yes',
        'WizardBackColor=#071014',
        'WizardBackImageFile={#WizardBackImagePath}',
        'WizardBackImageOpacity=255',
        'WizardImageFile={#WizardImagePath}',
        'WizardImageBackColor=#071014',
        'WizardSmallImageFile={#WizardSmallImagePath}',
        'WelcomeLabel1=Install [name]',
        'WelcomeLabel2=Turn gameplay recordings into clips ready to share. Replay Foundry works locally, so your source videos stay on this PC.%n%nSetup will guide you through the few choices that follow.',
        'WizardLicense=Review the license',
        'LicenseLabel=Review the license terms for Replay Foundry before continuing.',
        'LicenseLabel3=Review the Replay Foundry license terms. You must accept them to continue.',
        'WizardSelectTasks=Choose what to add',
        'SelectTasksDesc=Shortcuts and local AI',
        'SelectTasksLabel2=Choose any optional additions, then select Next.',
        'WizardReady=Ready to install',
        'ReadyLabel1=[name] is ready to install on this PC.',
        'ReadyLabel2a=Review your choices, then select Install.',
        'ReadyLabel2b=Select Install to continue.',
        'WizardInstalling=Installing [name]',
        'InstallingLabel=Keep this window open while Replay Foundry and its local tools are prepared.',
        'FinishedHeadingLabel=[name] is ready',
        'FinishedLabelNoIcons=Setup finished installing [name].',
        'FinishedLabel=Setup finished installing [name]. Select Finish to open it.',
        'Name: "desktopicon"; Description: "Add a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked',
        'Name: "advancedai"; Description: "Add Advanced AI (about 12.7 GB download)"; GroupDescription: "Optional local tools (visual AI needs a compatible 16 GB NVIDIA GPU):"; Flags: unchecked',
        'WizardSetBackImage([], True, True, 255)',
        'WizardForm.WizardBitmapImage.Visible := False',
        'WizardForm.WizardBitmapImage2.Visible := False',
        'WizardForm.WizardSmallBitmapImage.Visible := False',
        'HighContrastActive',
        'WizardForm.ProgressGauge.Style := npbstMarquee',
        'WizardForm.ProgressGauge.Style := npbstNormal')) {
        if ($installerScript.IndexOf($pattern, [StringComparison]::Ordinal) -lt 0) {
            throw "Installer script is missing branded/high-contrast behavior: $pattern"
        }
    }
    $installerBuilder = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\Build-ReplayFoundryInstaller.ps1')
    foreach ($pattern in @(
        "`$wizardImagePath = Join-Path `$brandingDirectory 'installer-wizard-hero.png'",
        '"/DWizardImagePath=$wizardImagePath"')) {
        if ($installerBuilder.IndexOf($pattern, [StringComparison]::Ordinal) -lt 0) {
            throw "Installer build is missing hero-image wiring: $pattern"
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
AppName=Replay Foundry Branding Smoke
AppVersion=0.0.0
DefaultDirName={tmp}\ReplayFoundryBrandingSmoke
PrivilegesRequired=lowest
Uninstallable=no
DisableWelcomePage=no
UsePreviousTasks=no
LicenseFile=$root\LICENSE.txt
OutputDir=$compileOutput
OutputBaseFilename=ReplayFoundry-Branding-Smoke
SetupIconFile=$root\src\ReplayFoundry.Desktop\Assets\Icons\Application\ReplayFoundry.ico
WizardStyle=modern dark windows11 hidebevels includetitlebar
WizardSizePercent=120,120
WizardKeepAspectRatio=yes
WizardImageStretch=yes
WizardBackColor=#071014
WizardBackImageFile=$first\installer-wizard-background.png
WizardBackImageOpacity=255
WizardImageFile=$first\installer-wizard-hero.png
WizardImageBackColor=#071014
WizardSmallImageFile=$first\installer-wizard-small.png
WizardSmallImageBackColor=#071014
[Messages]
WelcomeLabel1=Install [name]
WelcomeLabel2=Turn gameplay recordings into clips ready to share. Replay Foundry works locally, so your source videos stay on this PC.%n%nSetup will guide you through the few choices that follow.
WizardLicense=Review the license
LicenseLabel=Review the license terms for Replay Foundry before continuing.
LicenseLabel3=Review the Replay Foundry license terms. You must accept them to continue.
WizardSelectTasks=Choose what to add
SelectTasksDesc=Shortcuts and local AI
SelectTasksLabel2=Choose any optional additions, then select Next.
WizardReady=Ready to install
ReadyLabel1=[name] is ready to install on this PC.
ReadyLabel2a=Review your choices, then select Install.
ReadyLabel2b=Select Install to continue.
WizardInstalling=Installing [name]
InstallingLabel=Keep this window open while Replay Foundry and its local tools are prepared.
FinishedHeadingLabel=[name] is ready
FinishedLabelNoIcons=Setup finished installing [name].
FinishedLabel=Setup finished installing [name]. Select Finish to open it.
[Tasks]
Name: "desktopicon"; Description: "Add a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
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
    WizardForm.WizardBitmapImage.Visible := False;
    WizardForm.WizardBitmapImage2.Visible := False;
    WizardForm.WizardSmallBitmapImage.Visible := False;
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

        $optionalRoot = Join-Path $testRoot 'optional-advanced'
        $optionalPublish = Join-Path $optionalRoot 'app'
        $optionalPacks = Join-Path $optionalRoot 'packs'
        $optionalArchives = Join-Path $optionalPacks 'archives'
        $optionalOutput = Join-Path $optionalRoot 'output'
        [IO.Directory]::CreateDirectory($optionalPublish) | Out-Null
        [IO.Directory]::CreateDirectory($optionalArchives) | Out-Null
        [IO.Directory]::CreateDirectory($optionalOutput) | Out-Null
        Copy-Item -LiteralPath (Join-Path $root '.gitignore') `
            -Destination (Join-Path $optionalPublish 'payload.txt')
        Copy-Item -LiteralPath (Join-Path $root '.gitignore') `
            -Destination (Join-Path $optionalArchives 'replayfoundry-media-tools.zip')
        $optionalCatalog = Join-Path $optionalRoot 'advanced-runtime-catalog.json'
        Copy-Item -LiteralPath (Join-Path $root '.gitignore') -Destination $optionalCatalog
        $installerDefinition = Join-Path $root 'installer\ReplayFoundry.iss'
        & $compiler /Qp `
            '/DMyAppVersion=1.0.0-beta.2' `
            '/DMyAppFileVersion=1.0.0.0' `
            "/DPublishDir=$optionalPublish" `
            "/DRepoRoot=$root" `
            "/DInstallerOutputDir=$optionalOutput" `
            '/DInstallerProfile=Base' `
            "/DRuntimePackBuildRoot=$optionalPacks" `
            '/DAdvancedPayloadMode=Online' `
            "/DAdvancedCatalogPath=$optionalCatalog" `
            '/DOfferAdvancedAi=1' `
            "/DWizardBackImagePath=$first\installer-wizard-background.png" `
            "/DWizardImagePath=$first\installer-wizard-hero.png" `
            "/DWizardSmallImagePath=$first\installer-wizard-small.png" `
            '/DYouTubeCredentialTargetName=ReplayFoundry/YouTube/0123456789ABCDEF0123' `
            $installerDefinition
        if ($LASTEXITCODE -ne 0 -or
            -not (Test-Path -LiteralPath (Join-Path $optionalOutput 'ReplayFoundry-1.0.0-beta.2-Base-win-x64-setup.exe') -PathType Leaf)) {
            throw 'The supported Inno compiler rejected the optional Advanced AI installer path.'
        }
        Write-Host "Inno compile smoke passed: $compiler"
    }

    Write-Host 'Installer branding guard passed: canonical source, deterministic sealed images, safe composition zones, aspect ratios, palette, presentation contract, high contrast, and compiler support verified.'
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTest)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force
    }
}
