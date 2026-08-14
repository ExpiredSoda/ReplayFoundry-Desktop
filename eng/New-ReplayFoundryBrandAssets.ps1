param(
    [string]$Source = "ReplayFoundry.Desktop/Assets/Branding/ReplayFoundry-App-Icon-1024.png",
    [string]$IconOutput = "ReplayFoundry.Desktop/Assets/Icons/Application/ReplayFoundry.ico",
    [string]$AppleTouchOutput = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Resolve-RepoPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathFullyQualified($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

function Read-BitmapFrame {
    param([string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create(
            $stream,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $frame = $decoder.Frames[0]
        $frame.Freeze()
        return $frame
    }
    finally {
        $stream.Dispose()
    }
}

function New-ScaledPngBytes {
    param(
        [System.Windows.Media.Imaging.BitmapSource]$Bitmap,
        [int]$Size
    )

    $visual = [System.Windows.Media.DrawingVisual]::new()
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode(
        $visual,
        [System.Windows.Media.BitmapScalingMode]::Fant)
    $drawing = $visual.RenderOpen()
    try {
        $drawing.DrawImage($Bitmap, [System.Windows.Rect]::new(0, 0, $Size, $Size))
    }
    finally {
        $drawing.Close()
    }

    $render = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Size,
        $Size,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $render.Render($visual)
    $render.Freeze()

    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($render))
    $memory = [System.IO.MemoryStream]::new()
    try {
        $encoder.Save($memory)
        return $memory.ToArray()
    }
    finally {
        $memory.Dispose()
    }
}

$sourcePath = Resolve-RepoPath $Source
$iconPath = Resolve-RepoPath $IconOutput
if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Brand source image does not exist: $sourcePath"
}

$bitmap = Read-BitmapFrame $sourcePath
if ($bitmap.PixelWidth -ne $bitmap.PixelHeight -or $bitmap.PixelWidth -lt 256) {
    throw "Brand source must be a square image at least 256 pixels wide."
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    [pscustomobject]@{
        Size = $size
        Bytes = New-ScaledPngBytes -Bitmap $bitmap -Size $size
    }
}

[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($iconPath)) | Out-Null
$iconStream = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }

    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame.Bytes)
    }
}
finally {
    $writer.Dispose()
}

Write-Host "Generated ReplayFoundry.ico with $($sizes.Count) PNG frames: $($sizes -join ', ')"

if (-not [string]::IsNullOrWhiteSpace($AppleTouchOutput)) {
    $appleTouchPath = Resolve-RepoPath $AppleTouchOutput
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($appleTouchPath)) | Out-Null
    [System.IO.File]::WriteAllBytes(
        $appleTouchPath,
        (New-ScaledPngBytes -Bitmap $bitmap -Size 180))
    Write-Host "Generated apple-touch-icon.png at 180x180: $appleTouchPath"
}
