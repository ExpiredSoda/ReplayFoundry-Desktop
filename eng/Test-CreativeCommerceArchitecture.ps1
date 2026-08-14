$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$creativePackRoot = Join-Path $repositoryRoot 'ReplayFoundry.Desktop\Features\Studio\CreativePacks'

if (-not (Test-Path -LiteralPath $creativePackRoot -PathType Container)) {
    throw 'The Studio creative-pack contract boundary is missing.'
}

$creativeSource = Get-ChildItem -LiteralPath $creativePackRoot -Filter '*.cs' -File |
    Sort-Object Name |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }
$joinedSource = $creativeSource -join [Environment]::NewLine

$forbiddenSourcePatterns = @(
    'HttpClient',
    'ProcessStartInfo',
    'ReplayFoundry\.RuntimePacks',
    'Lemon\s*Squeezy',
    'Paddle',
    'Stripe',
    'Ko-fi',
    '\.(exe|dll|cmd|bat|ps1|py|onnx|safetensors)"'
)

foreach ($pattern in $forbiddenSourcePatterns) {
    if ($joinedSource -match $pattern) {
        throw "Studio creative-pack contracts contain forbidden runtime, vendor, or executable coupling: $pattern"
    }
}

$generationSource = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'ReplayFoundry.Desktop\Features\Generate') -Recurse -Filter '*.cs' -File |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }
if (($generationSource -join [Environment]::NewLine) -match 'StudioCreativePack') {
    throw 'Generation must not depend on optional Studio creative packs.'
}

Write-Output 'Creative-commerce architecture passed: core generation is free and Studio packs are passive and provider-neutral. Website commerce and support flows are independently built and tested in the canonical website project.'
