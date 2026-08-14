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

$sourcePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png'))
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
$smallPath = Join-Path $outputRoot 'installer-wizard-small.png'
$manifestPath = Join-Path $outputRoot 'installer-branding-manifest.json'

$ink = New-Brush '#071014'
$cyan = New-Brush '#59CAF0'
$blue = New-Brush '#1F9DC4'
$yellow = New-Brush '#FFC75E'
$cyanQuiet = New-TransparentBrush 42 89 202 240
$cyanTrace = New-TransparentBrush 25 31 157 196
$yellowQuiet = New-TransparentBrush 76 255 199 94

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

    # Restrained edge geometry keeps the center readable for native wizard
    # controls. No text or font rendering enters the deterministic image.
    $drawing.DrawRectangle($cyanTrace, $null, [Windows.Rect]::new(72, 72, 860, 2))
    $drawing.DrawRectangle($cyanQuiet, $null, [Windows.Rect]::new(72, 72, 2, 250))
    $drawing.DrawRectangle($yellow, $null, [Windows.Rect]::new(72, 88, 74, 12))
    $drawing.DrawRectangle($blue, $null, [Windows.Rect]::new(72, $backgroundHeight - 92, 330, 20))
    $drawing.DrawRectangle($cyan, $null, [Windows.Rect]::new(402, $backgroundHeight - 92, 112, 20))
    $drawing.DrawRectangle($yellowQuiet, $null, [Windows.Rect]::new($backgroundWidth - 82, 170, 10, 210))
    $drawing.DrawRectangle($cyanTrace, $null, [Windows.Rect]::new($backgroundWidth - 540, $backgroundHeight - 76, 468, 2))

    # The logo is never recolored, cropped, skewed, or reconstructed. It is
    # drawn as one square image so its established pixel geometry and aspect
    # remain authoritative.
    $logoSize = 500
    $drawing.DrawImage(
        $sourceBitmap,
        [Windows.Rect]::new(
            $backgroundWidth - $logoSize - 112,
            $backgroundHeight - $logoSize - 112,
            $logoSize,
            $logoSize))
}
finally { $drawing.Close() }
Save-VisualPng $background $backgroundWidth $backgroundHeight $backgroundPath

$smallSize = 256
$small = [Windows.Media.DrawingVisual]::new()
[Windows.Media.RenderOptions]::SetBitmapScalingMode(
    $small,
    [Windows.Media.BitmapScalingMode]::HighQuality)
$smallDrawing = $small.RenderOpen()
try {
    $smallDrawing.DrawRectangle($ink, $null, [Windows.Rect]::new(0, 0, $smallSize, $smallSize))
    $smallDrawing.DrawImage($sourceBitmap, [Windows.Rect]::new(18, 18, 220, 220))
    $smallDrawing.DrawRectangle($cyan, $null, [Windows.Rect]::new(18, 238, 150, 4))
    $smallDrawing.DrawRectangle($yellow, $null, [Windows.Rect]::new(168, 238, 28, 4))
}
finally { $smallDrawing.Close() }
Save-VisualPng $small $smallSize $smallSize $smallPath

$manifest = [ordered]@{
    schemaVersion = 'replayfoundry-installer-branding-1.0'
    source = [ordered]@{
        relativePath = 'ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png'
        width = $sourceBitmap.PixelWidth
        height = $sourceBitmap.PixelHeight
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash
    }
    palette = [ordered]@{
        ink = '#071014'
        blue = '#1F9DC4'
        cyan = '#59CAF0'
        yellow = '#FFC75E'
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
Write-Host "Installer small image: $smallPath"
Write-Host "Installer branding manifest: $manifestPath"
