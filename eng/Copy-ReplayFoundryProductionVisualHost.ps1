[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,

    [Parameter(Mandatory = $true)]
    [string]$DestinationRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$source = [IO.Path]::GetFullPath($SourceRoot)
$destination = [IO.Path]::GetFullPath($DestinationRoot)
$packageSource = Join-Path $source 'replayfoundry_visual_semantic'
$entryPoint = Join-Path $source 'qwen3_vl_batch_host.py'
if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $packageSource 'cli.py') -PathType Leaf)) {
    throw 'The production visual host source is incomplete.'
}
if ($destination.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
    $destination.StartsWith($source + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The production visual host destination must be outside its source tree.'
}
if (Test-Path -LiteralPath $destination) {
    if (-not (Test-Path -LiteralPath $destination -PathType Container) -or
        @(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) {
        throw 'The production visual host destination must be new or empty.'
    }
} else {
    New-Item -ItemType Directory -Path $destination | Out-Null
}

$hostManifestPath = Join-Path $PSScriptRoot 'ReplayFoundry.ProductionVisualHost.psd1'
$hostManifest = Import-PowerShellDataFile -LiteralPath $hostManifestPath
if ($hostManifest.SchemaVersion -ne 1 -or
    -not $hostManifest.EntryPoint.Equals(
        'qwen3_vl_batch_host.py',
        [StringComparison]::Ordinal)) {
    throw 'The production visual-host manifest is invalid.'
}
$runtimeAssets = @($hostManifest.Assets)
$runtimeModules = @($hostManifest.Modules)
$forbiddenFiles = @($hostManifest.ForbiddenSourceFiles)
foreach ($asset in $runtimeAssets) {
    $path = Join-Path $source $asset
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The production visual host asset is missing: $asset"
    }
}

Copy-Item -LiteralPath $entryPoint -Destination $destination
$packageDestination = Join-Path $destination 'replayfoundry_visual_semantic'
foreach ($relative in $runtimeModules) {
    $file = Join-Path $packageSource $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "The production visual host module is missing: $relative"
    }
    $target = Join-Path $packageDestination $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file -Destination $target
}
foreach ($relative in $forbiddenFiles) {
    if (Test-Path -LiteralPath (Join-Path $destination $relative)) {
        throw "A developer-only module entered the production host: $relative"
    }
}
foreach ($asset in $runtimeAssets) {
    Copy-Item -LiteralPath (Join-Path $source $asset) -Destination $destination
}
[IO.File]::WriteAllText(
    (Join-Path $destination 'replayfoundry-production-host.txt'),
    "Replay Foundry packaged production host$([Environment]::NewLine)",
    [Text.UTF8Encoding]::new($false))

Write-Output "Production visual host assembled at $destination"
