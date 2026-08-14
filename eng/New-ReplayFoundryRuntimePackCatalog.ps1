[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimePackBuildRoot,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string]$BaseUri,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string[]]$ApprovedRedirectHosts = @()
)

$ErrorActionPreference = 'Stop'
$buildRoot = [IO.Path]::GetFullPath($RuntimePackBuildRoot)
$indexPath = Join-Path $buildRoot 'runtime-pack-build-index.json'
if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Runtime-pack build index not found: $indexPath"
}
$index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json -DateKind String
if ($index.profile -notin @('Base', 'Advanced')) {
    throw "Unsupported runtime-pack profile '$($index.profile)'."
}
$createdAtUtc = [DateTimeOffset]::Parse(
    [string]$index.createdAtUtc,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::RoundtripKind)
if ($createdAtUtc.Offset -ne [TimeSpan]::Zero) {
    throw 'Runtime-pack build index createdAtUtc must use UTC.'
}
$base = [Uri]::new($BaseUri.TrimEnd('/') + '/')
$hosts = @($ApprovedRedirectHosts | ForEach-Object { $_.Trim().ToLowerInvariant() })
if ($hosts | Where-Object { $_ -notmatch '^[a-z0-9.-]+$' }) {
    throw 'Approved redirect hosts must be DNS host names.'
}

$catalogPacks = foreach ($pack in $index.packs) {
    $archive = [IO.Path]::GetFullPath($pack.archive)
    if (-not $archive.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $archive -PathType Leaf)) {
        throw "Pack archive is missing or outside the build root: $($pack.packageId)"
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash
    if ($actualHash -ne $pack.sha256 -or (Get-Item -LiteralPath $archive).Length -ne $pack.byteLength) {
        throw "Pack archive does not match the build index: $($pack.packageId)"
    }
    $manifestPath = Join-Path $buildRoot "packs\$($pack.packageId)\runtime-pack-manifest.json"
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.manifestHash -ne $pack.manifestHash -or $manifest.identity.packageId -ne $pack.packageId) {
        throw "Pack manifest does not match the build index: $($pack.packageId)"
    }
    [ordered]@{
        packageId = $pack.packageId
        kind = $manifest.identity.kind
        semanticVersion = $manifest.identity.semanticVersion
        downloadUrl = [Uri]::new($base, [IO.Path]::GetFileName($archive)).AbsoluteUri
        byteLength = [long]$pack.byteLength
        sha256 = $actualHash
        manifestHash = $manifest.manifestHash
        approvedRedirectHosts = $hosts
    }
}

$catalog = [ordered]@{
    schemaVersion = 'replayfoundry-runtime-pack-catalog-1.1'
    profile = $index.profile
    packs = @($catalogPacks)
    createdAtUtc = $createdAtUtc.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedOutput) -Force | Out-Null
$catalog | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8NoBOM
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedOutput).Hash
Set-Content -LiteralPath ($resolvedOutput + '.sha256') -Value "$hash  $([IO.Path]::GetFileName($resolvedOutput))" -Encoding ascii
Write-Host "Catalog: $resolvedOutput"
Write-Host "Catalog SHA-256: $hash"
