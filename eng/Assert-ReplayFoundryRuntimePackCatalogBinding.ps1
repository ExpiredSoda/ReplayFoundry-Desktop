[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimePackBuildRoot,

    [Parameter(Mandatory = $true)]
    [string]$CatalogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$buildRoot = [IO.Path]::GetFullPath($RuntimePackBuildRoot)
$catalogFile = [IO.Path]::GetFullPath($CatalogPath)
$indexPath = Join-Path $buildRoot 'runtime-pack-build-index.json'
if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $catalogFile -PathType Leaf)) {
    throw 'Catalog binding requires an existing build index and catalog.'
}
$index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
$catalog = Get-Content -Raw -LiteralPath $catalogFile | ConvertFrom-Json
if ($index.profile -ne 'Advanced' -or $catalog.profile -ne 'Advanced') {
    throw 'An online Advanced AI catalog must bind to an Advanced runtime-pack build index.'
}
$indexPacks = @($index.packs)
$catalogPacks = @($catalog.packs)
if ($indexPacks.Count -ne $catalogPacks.Count) {
    throw 'The Advanced AI catalog pack set does not match its runtime-pack build index.'
}

$indexIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($pack in $indexPacks) {
    $packageId = [string]$pack.packageId
    if ($packageId -notmatch '^[a-z0-9][a-z0-9.-]{0,127}$' -or
        -not $indexIds.Add($packageId)) {
        throw "The runtime-pack build index contains an invalid or duplicate package ID: $packageId"
    }
}
$catalogIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($pack in $catalogPacks) {
    $packageId = [string]$pack.packageId
    if ($packageId -notmatch '^[a-z0-9][a-z0-9.-]{0,127}$' -or
        -not $catalogIds.Add($packageId)) {
        throw "The Advanced AI catalog contains an invalid or duplicate package ID: $packageId"
    }
}
if (-not $indexIds.SetEquals($catalogIds)) {
    throw 'The Advanced AI catalog package IDs do not exactly match the build index.'
}

foreach ($pack in $indexPacks) {
    $packageId = [string]$pack.packageId
    $matches = @($catalogPacks | Where-Object {
        [string]$_.packageId -ceq $packageId
    })
    if ($matches.Count -ne 1) {
        throw "The Advanced AI catalog must contain exactly one '$packageId' entry."
    }
    $catalogPack = $matches[0]
    $manifestPath = Join-Path $buildRoot "packs\$packageId\runtime-pack-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "The indexed runtime-pack manifest is missing: $packageId"
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $matchesIndex =
        [long]$catalogPack.byteLength -eq [long]$pack.byteLength -and
        [string]$catalogPack.sha256 -ceq [string]$pack.sha256 -and
        [string]$catalogPack.manifestHash -ceq [string]$pack.manifestHash
    $matchesManifest =
        [string]$manifest.identity.packageId -ceq $packageId -and
        [string]$catalogPack.kind -ceq [string]$manifest.identity.kind -and
        [string]$catalogPack.semanticVersion -ceq [string]$manifest.identity.semanticVersion -and
        [string]$catalogPack.manifestHash -ceq [string]$manifest.manifestHash
    if (-not $matchesIndex -or -not $matchesManifest) {
        throw "The Advanced AI catalog entry does not match the indexed '$packageId' payload."
    }
}

Write-Output 'Advanced AI catalog exactly matches its runtime-pack build index.'
