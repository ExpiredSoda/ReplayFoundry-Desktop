[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Read-BitmapFrame([string]$PathValue) {
    $stream = [IO.File]::OpenRead($PathValue)
    try {
        $decoder = [Windows.Media.Imaging.BitmapDecoder]::Create(
            $stream,
            [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $frame = $decoder.Frames[0]
        $frame.Freeze()
        return $frame
    }
    finally {
        $stream.Dispose()
    }
}

function New-Brush([string]$HexColor) {
    $color = [Windows.Media.ColorConverter]::ConvertFromString($HexColor)
    $brush = [Windows.Media.SolidColorBrush]::new($color)
    $brush.Freeze()
    return $brush
}

function New-TransparentBrush([byte]$Alpha, [byte]$Red, [byte]$Green, [byte]$Blue) {
    $brush = [Windows.Media.SolidColorBrush]::new(
        [Windows.Media.Color]::FromArgb($Alpha, $Red, $Green, $Blue))
    $brush.Freeze()
    return $brush
}

function Save-VisualPng(
    [Windows.Media.DrawingVisual]$Visual,
    [int]$Width,
    [int]$Height,
    [string]$PathValue) {
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Width,
        $Height,
        96,
        96,
        [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($Visual)
    $bitmap.Freeze()

    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($PathValue)
    try { $encoder.Save($stream) }
    finally { $stream.Dispose() }
}

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$sourcePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'src/ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png'))
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Canonical ReplayFoundry logo was not found: $sourcePath"
}
$sourceBitmap = Read-BitmapFrame $sourcePath
if ($sourceBitmap.PixelWidth -ne 1024 -or $sourceBitmap.PixelHeight -ne 1024) {
    throw 'Installer branding requires the canonical 1024 by 1024 ReplayFoundry logo.'
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$backgroundPath = Join-Path $outputRoot 'installer-wizard-background.png'
$heroPath = Join-Path $outputRoot 'installer-wizard-hero.png'
$smallPath = Join-Path $outputRoot 'installer-wizard-small.png'
$manifestPath = Join-Path $outputRoot 'installer-branding-manifest.json'

$ink = New-Brush '#071014'
$blue = New-Brush '#1599C8'
$cyan = New-Brush '#58D6FF'
$yellow = New-Brush '#FFC85A'
$blueQuiet = New-TransparentBrush 42 21 153 200
$cyanQuiet = New-TransparentBrush 42 88 214 255
$cyanTrace = New-TransparentBrush 25 88 214 255
$yellowQuiet = New-TransparentBrush 76 255 200 90

# The official WizardBackImageFile area keeps a 497:360 aspect ratio. Four
# exact logical units provide a crisp source even at high desktop DPI.
$backgroundWidth = 1988
$backgroundHeight = 1440
$background = [Windows.Media.DrawingVisual]::new()
[Windows.Media.RenderOptions]::SetBitmapScalingMode(
    $background,
    [Windows.Media.BitmapScalingMode]::HighQuality)
$drawing = $background.RenderOpen()
try {
    $drawing.DrawRectangle($ink, $null, [Windows.Rect]::new(0, 0, $backgroundWidth, $backgroundHeight))

    # Restrained edge geometry keeps every native content and footer region
    # clear. No text or font rendering enters the deterministic image.
    $drawing.DrawRectangle($cyanTrace, $null, [Windows.Rect]::new(72, 72, 860, 2))
    $drawing.DrawRectangle($cyanQuiet, $null, [Windows.Rect]::new(72, 72, 2, 250))
    $drawing.DrawRectangle($yellow, $null, [Windows.Rect]::new(72, 88, 74, 12))
    $drawing.DrawRectangle($cyanTrace, $null, [Windows.Rect]::new($backgroundWidth - 420, 72, 348, 2))
    $drawing.DrawRectangle($yellowQuiet, $null, [Windows.Rect]::new($backgroundWidth - 82, 170, 10, 210))
}
finally { $drawing.Close() }
Save-VisualPng $background $backgroundWidth $backgroundHeight $backgroundPath

# Welcome and completion pages have a dedicated 164:314 image area. This
# four-times source keeps the canonical mark crisp while the lower signal rail
# evokes clip selection without relying on text or overlapping native controls.
$heroWidth = 656
$heroHeight = 1256
$hero = [Windows.Media.DrawingVisual]::new()
[Windows.Media.RenderOptions]::SetBitmapScalingMode(
    $hero,
    [Windows.Media.BitmapScalingMode]::HighQuality)
$heroDrawing = $hero.RenderOpen()
try {
    $heroDrawing.DrawRectangle($ink, $null, [Windows.Rect]::new(0, 0, $heroWidth, $heroHeight))

    # A compact corner frame establishes the same industrial rhythm as the
    # application without crowding the hero's focal point.
    $heroDrawing.DrawRectangle($cyanTrace, $null, [Windows.Rect]::new(48, 48, 560, 2))
    $heroDrawing.DrawRectangle($cyanQuiet, $null, [Windows.Rect]::new(48, 48, 2, 210))
    $heroDrawing.DrawRectangle($yellow, $null, [Windows.Rect]::new(48, 64, 88, 10))
    $heroDrawing.DrawRectangle($yellowQuiet, $null, [Windows.Rect]::new(606, 48, 2, 138))

    # The logo remains a single, unmodified square image inside an inset
    # frame. Its size survives downscaling without dominating the page.
    $heroDrawing.DrawRectangle($cyanTrace, $null, [Windows.Rect]::new(96, 156, 464, 464))
    $heroDrawing.DrawRectangle($ink, $null, [Windows.Rect]::new(100, 160, 456, 456))
    $heroDrawing.DrawRectangle($cyan, $null, [Windows.Rect]::new(96, 156, 84, 6))
    $heroDrawing.DrawRectangle($yellow, $null, [Windows.Rect]::new(488, 614, 72, 6))
    $heroDrawing.DrawImage($sourceBitmap, [Windows.Rect]::new(142, 202, 372, 372))

    # A quiet three-beat signal rail suggests detected gameplay moments. The
    # varying lengths keep the panel directional instead of ornamental noise.
    $heroDrawing.DrawRectangle($cyanTrace, $null, [Windows.Rect]::new(96, 692, 464, 2))
    $heroDrawing.DrawRectangle($blueQuiet, $null, [Windows.Rect]::new(96, 692, 2, 360))

    $heroDrawing.DrawRectangle($blue, $null, [Windows.Rect]::new(86, 742, 24, 24))
    $heroDrawing.DrawRectangle($blue, $null, [Windows.Rect]::new(140, 749, 278, 10))
    $heroDrawing.DrawRectangle($cyanTrace, $null, [Windows.Rect]::new(430, 749, 130, 10))

    $heroDrawing.DrawRectangle($cyan, $null, [Windows.Rect]::new(86, 866, 24, 24))
    $heroDrawing.DrawRectangle($cyan, $null, [Windows.Rect]::new(140, 873, 356, 10))
    $heroDrawing.DrawRectangle($cyanTrace, $null, [Windows.Rect]::new(508, 873, 52, 10))

    $heroDrawing.DrawRectangle($yellow, $null, [Windows.Rect]::new(86, 990, 24, 24))
    $heroDrawing.DrawRectangle($blue, $null, [Windows.Rect]::new(140, 997, 220, 10))
    $heroDrawing.DrawRectangle($cyanTrace, $null, [Windows.Rect]::new(372, 997, 188, 10))

    $heroDrawing.DrawRectangle($blue, $null, [Windows.Rect]::new(48, 1160, 350, 8))
    $heroDrawing.DrawRectangle($cyan, $null, [Windows.Rect]::new(398, 1160, 150, 8))
    $heroDrawing.DrawRectangle($yellow, $null, [Windows.Rect]::new(548, 1160, 60, 8))
}
finally { $heroDrawing.Close() }
Save-VisualPng $hero $heroWidth $heroHeight $heroPath

$smallSize = 256
$small = [Windows.Media.DrawingVisual]::new()
[Windows.Media.RenderOptions]::SetBitmapScalingMode(
    $small,
    [Windows.Media.BitmapScalingMode]::HighQuality)
$smallDrawing = $small.RenderOpen()
try {
    $smallDrawing.DrawRectangle($ink, $null, [Windows.Rect]::new(0, 0, $smallSize, $smallSize))
    $smallDrawing.DrawImage($sourceBitmap, [Windows.Rect]::new(26, 26, 204, 204))
}
finally { $smallDrawing.Close() }
Save-VisualPng $small $smallSize $smallSize $smallPath

$manifest = [ordered]@{
    schemaVersion = 'replayfoundry-installer-branding-1.1'
    source = [ordered]@{
        relativePath = 'src/ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png'
        width = $sourceBitmap.PixelWidth
        height = $sourceBitmap.PixelHeight
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash
    }
    palette = [ordered]@{
        ink = '#071014'
        blue = '#1599C8'
        cyan = '#58D6FF'
        yellow = '#FFC85A'
    }
    outputs = @(
        [ordered]@{
            role = 'WizardBackImageFile'
            fileName = [IO.Path]::GetFileName($backgroundPath)
            width = $backgroundWidth
            height = $backgroundHeight
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $backgroundPath).Hash
        },
        [ordered]@{
            role = 'WizardImageFile'
            fileName = [IO.Path]::GetFileName($heroPath)
            width = $heroWidth
            height = $heroHeight
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $heroPath).Hash
        },
        [ordered]@{
            role = 'WizardSmallImageFile'
            fileName = [IO.Path]::GetFileName($smallPath)
            width = $smallSize
            height = $smallSize
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $smallPath).Hash
        })
}
[IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

Write-Host "Installer background: $backgroundPath"
Write-Host "Installer hero: $heroPath"
Write-Host "Installer small image: $smallPath"
Write-Host "Installer branding manifest: $manifestPath"
