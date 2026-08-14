[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PythonHome,

    [Parameter(Mandatory = $true)]
    [string]$SitePackages,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$LicenseOverrideManifest
)

$ErrorActionPreference = 'Stop'
$pythonRoot = [IO.Path]::GetFullPath($PythonHome)
$packagesRoot = [IO.Path]::GetFullPath($SitePackages)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $pythonRoot -PathType Container) -or
    -not (Test-Path -LiteralPath $packagesRoot -PathType Container)) {
    throw 'PythonHome and SitePackages must be existing directories.'
}
if (Test-Path -LiteralPath $outputRoot) {
    if (@(Get-ChildItem -LiteralPath $outputRoot -Force).Count -gt 0) {
        throw "Notice output must be empty: $outputRoot"
    }
} else {
    New-Item -ItemType Directory -Path $outputRoot | Out-Null
}

$licenses = Join-Path $outputRoot 'licenses'
New-Item -ItemType Directory -Path $licenses | Out-Null
$overrides = @{}
if (-not [string]::IsNullOrWhiteSpace($LicenseOverrideManifest)) {
    $overridePath = [IO.Path]::GetFullPath($LicenseOverrideManifest)
    if (-not (Test-Path -LiteralPath $overridePath -PathType Leaf)) {
        throw "License override manifest does not exist: $overridePath"
    }
    $overrideRoot = Split-Path -Parent $overridePath
    $overrideDocument = Get-Content -Raw -LiteralPath $overridePath | ConvertFrom-Json
    if ($overrideDocument.schemaVersion -ne 'replayfoundry-python-license-overrides-1.0') {
        throw 'Unsupported Python license override schema.'
    }
    foreach ($override in $overrideDocument.overrides) {
        $key = "$($override.name.ToLowerInvariant())@$($override.version)"
        if ($overrides.ContainsKey($key)) { throw "Duplicate Python license override: $key" }
        if ($override.sourceUrl -notmatch '^https://') { throw "License override $key requires an HTTPS source URL." }
        $file = [IO.Path]::GetFullPath((Join-Path $overrideRoot $override.licenseFile))
        if (-not $file.StartsWith($overrideRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "License override $key escapes its manifest directory or is missing."
        }
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash
        if ($actualHash -ne $override.sha256) { throw "License override $key failed SHA-256 verification." }
        $overrides[$key] = [pscustomobject]@{
            File = $file
            Expression = $override.licenseExpression
            SourceUrl = $override.sourceUrl
        }
    }
}
$pythonLicense = Join-Path $pythonRoot 'LICENSE.txt'
if (-not (Test-Path -LiteralPath $pythonLicense -PathType Leaf)) {
    throw 'The Python distribution does not contain LICENSE.txt.'
}
Copy-Item -LiteralPath $pythonLicense -Destination (Join-Path $licenses 'CPython-LICENSE.txt')

$components = [Collections.Generic.List[object]]::new()
$components.Add([ordered]@{
    name = 'CPython'
    version = (& (Join-Path $pythonRoot 'python.exe') --version 2>&1).ToString().Replace('Python ', '').Trim()
    licenseExpression = 'PSF-2.0'
    sourceUrl = 'https://www.python.org/downloads/windows/'
    licenseFiles = @('licenses/CPython-LICENSE.txt')
})

foreach ($metadataDirectory in Get-ChildItem -LiteralPath $packagesRoot -Directory -Filter '*.dist-info' | Sort-Object Name) {
    $metadataPath = Join-Path $metadataDirectory.FullName 'METADATA'
    if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) { continue }
    $metadata = Get-Content -LiteralPath $metadataPath
    $nameLine = $metadata | Where-Object { $_ -like 'Name: *' } | Select-Object -First 1
    $versionLine = $metadata | Where-Object { $_ -like 'Version: *' } | Select-Object -First 1
    if ($null -eq $nameLine -or $null -eq $versionLine) { continue }
    $name = $nameLine.Substring(6).Trim()
    $version = $versionLine.Substring(9).Trim()
    $expressionLine = $metadata | Where-Object { $_ -like 'License-Expression: *' } | Select-Object -First 1
    $licenseLine = $metadata | Where-Object { $_ -like 'License: *' } | Select-Object -First 1
    $expression = if ($null -ne $expressionLine) {
        $expressionLine.Substring(20).Trim()
    } elseif ($null -ne $licenseLine -and $licenseLine.Substring(9).Trim().Length -lt 100) {
        $licenseLine.Substring(9).Trim()
    } else {
        'See bundled license files'
    }
    $componentFolderName = ($name + '-' + $version) -replace '[^A-Za-z0-9._-]', '_'
    $componentFolder = Join-Path $licenses $componentFolderName
    $licenseSources = [Collections.Generic.List[IO.FileInfo]]::new()
    $distLicenseDirectory = Join-Path $metadataDirectory.FullName 'licenses'
    if (Test-Path -LiteralPath $distLicenseDirectory -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $distLicenseDirectory -Recurse -File) { $licenseSources.Add($file) }
    }
    foreach ($candidateName in @('LICENSE', 'LICENSE.txt', 'COPYING', 'NOTICE')) {
        $candidate = Join-Path $metadataDirectory.FullName $candidateName
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $licenseSources.Add((Get-Item -LiteralPath $candidate)) }
    }
    if ($licenseSources.Count -eq 0) {
        $overrideKey = "$($name.ToLowerInvariant())@$version"
        if (-not $overrides.ContainsKey($overrideKey)) {
            throw "Python package $name $version has no retained license file. Supply a hash-pinned official license override before packaging."
        }
        $override = $overrides[$overrideKey]
        $licenseSources.Add((Get-Item -LiteralPath $override.File))
        $expression = $override.Expression
        $licenseSourceUrl = $override.SourceUrl
    } else {
        $licenseSourceUrl = "https://pypi.org/project/$name/$version/"
    }
    New-Item -ItemType Directory -Path $componentFolder | Out-Null
    $relativeLicenses = [Collections.Generic.List[string]]::new()
    $ordinal = 0
    foreach ($source in $licenseSources | Sort-Object FullName -Unique) {
        $ordinal++
        $destinationName = '{0:D2}-{1}' -f $ordinal,$source.Name
        Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $componentFolder $destinationName)
        $relativeLicenses.Add("licenses/$componentFolderName/$destinationName")
    }
    $components.Add([ordered]@{
        name = $name
        version = $version
        licenseExpression = $expression
        sourceUrl = $licenseSourceUrl
        licenseFiles = @($relativeLicenses)
    })
}

$inventory = [ordered]@{
    schemaVersion = 'replayfoundry-python-runtime-notices-1.0'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    components = @($components)
}
$inventoryPath = Join-Path $outputRoot 'third-party-components.json'
$inventory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $inventoryPath -Encoding utf8NoBOM
$inventoryHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $inventoryPath).Hash

$markdown = [Collections.Generic.List[string]]::new()
$markdown.Add('# Replay Foundry Qwen runtime third-party notices')
$markdown.Add('')
$markdown.Add('This inventory is generated from the exact installed CPython and wheel metadata used to assemble the visual runtime pack.')
$markdown.Add('')
foreach ($component in $components) {
    $markdown.Add("- **$($component.name) $($component.version)** — $($component.licenseExpression) — $($component.sourceUrl)")
    foreach ($licenseFile in $component.licenseFiles) {
        $markdown.Add(('  - `{0}`' -f $licenseFile))
    }
}
$markdown.Add('')
$markdown.Add(('Inventory SHA-256: `{0}`' -f $inventoryHash))
$markdown | Set-Content -LiteralPath (Join-Path $outputRoot 'THIRD-PARTY-NOTICES.md') -Encoding utf8NoBOM

Write-Host "Generated $($components.Count) component notice records under $outputRoot"
Write-Host "Inventory SHA-256: $inventoryHash"
