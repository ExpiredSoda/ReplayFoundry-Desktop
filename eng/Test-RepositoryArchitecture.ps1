[CmdletBinding()]
param([string]$RepositoryRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..'
}

$root = [IO.Path]::GetFullPath($RepositoryRoot)
$failures = [Collections.Generic.List[string]]::new()
$manifest = Import-PowerShellDataFile -LiteralPath `
    (Join-Path $root 'eng\ReplayFoundry.Repository.psd1')

function Add-Failure([string]$Message) { $failures.Add($Message) }

function ConvertTo-RepositoryPath([string]$Path) {
    return $Path.Replace('\', '/').TrimStart('/')
}

function Get-RepositoryPath([string]$Path) {
    return ConvertTo-RepositoryPath `
        ([IO.Path]::GetRelativePath($root, [IO.Path]::GetFullPath($Path)))
}

function Assert-SameSet(
    [string]$Name,
    [string[]]$Expected,
    [string[]]$Actual) {
    $missing = @($Expected | Where-Object { $_ -notin $Actual })
    $extra = @($Actual | Where-Object { $_ -notin $Expected })
    if ($missing.Count -or $extra.Count) {
        Add-Failure "$Name differs. Missing=[$($missing -join ', ')]; Extra=[$($extra -join ', ')]."
    }
}

function Get-ManifestProjects {
    return @(
        $manifest.ProductProjects
        $manifest.ToolProjects
        $manifest.TestSupportProjects
        $manifest.DotNetTestProjects
    ) | ForEach-Object { ConvertTo-RepositoryPath $_ }
}

function Get-ExpectedSolutionFolder([string]$Project) {
    if ($Project -in $manifest.ProductProjects) { return '/Product/' }
    if ($Project -in $manifest.ToolProjects) { return '/Product Tools/' }
    if ($Project -match 'ReplayFoundry\.(Composition|Inspection|Preparation)Tests') {
        return '/Verification/Desktop/'
    }
    if ($Project -match 'ReplayFoundry\.RuntimePacks\.Tests') {
        return '/Verification/Runtime Packs/'
    }
    if ($Project -in $manifest.TestSupportProjects) {
        return '/Verification/Shared/'
    }
    return ''
}

if ($manifest.SchemaVersion -ne 1 -or
    -not $manifest.ContainsKey('PublicSource')) {
    Add-Failure 'The production source manifest is invalid.'
}
if (-not (Test-Path -LiteralPath `
    (Join-Path $root '.replayfoundry-public-source') -PathType Leaf)) {
    Add-Failure 'The production source marker is missing.'
}

foreach ($directory in @('src', 'tools', 'tests', 'docs', 'eng', 'installer')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $directory) -PathType Container)) {
        Add-Failure "Required repository directory is missing: $directory"
    }
}
foreach ($relative in $manifest.PublicSource.RequiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
        Add-Failure "Required production source file is missing: $relative"
    }
}
foreach ($relative in $manifest.PublicSource.ProjectRoots) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Container)) {
        Add-Failure "Required production project root is missing: $relative"
    }
}

$declaredProjects = @(Get-ManifestProjects)
$actualProjects = @(Get-ChildItem `
    (Join-Path $root 'src'),
    (Join-Path $root 'tools'),
    (Join-Path $root 'tests') `
    -Recurse -Filter '*.csproj' -File |
    ForEach-Object { Get-RepositoryPath $_.FullName })
Assert-SameSet 'Production project manifest' $declaredProjects $actualProjects

[xml]$solution = Get-Content -Raw -LiteralPath `
    (Join-Path $root $manifest.RepositorySolution)
$solutionProjects = @($solution.SelectNodes('//Project') |
    ForEach-Object { ConvertTo-RepositoryPath $_.Path })
Assert-SameSet 'Production solution project set' $declaredProjects $solutionProjects
foreach ($projectNode in $solution.SelectNodes('//Project')) {
    $project = ConvertTo-RepositoryPath $projectNode.Path
    $actualFolder = [string]$projectNode.ParentNode.GetAttribute('Name')
    $expectedFolder = Get-ExpectedSolutionFolder $project
    if ($actualFolder -ne $expectedFolder) {
        Add-Failure "Solution folder differs for $project. Expected='$expectedFolder'; Actual='$actualFolder'."
    }
}

foreach ($project in Get-ChildItem `
    (Join-Path $root 'src'),
    (Join-Path $root 'tools'),
    (Join-Path $root 'tests') `
    -Recurse -Filter '*.csproj' -File) {
    [xml]$projectDocument = Get-Content -Raw -LiteralPath $project.FullName
    foreach ($reference in $projectDocument.SelectNodes('//ProjectReference')) {
        $target = [IO.Path]::GetFullPath((Join-Path $project.DirectoryName $reference.Include))
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
            Add-Failure "Broken project reference in $($project.Name): $($reference.Include)"
        }
        $projectRelative = Get-RepositoryPath $project.FullName
        $targetRelative = Get-RepositoryPath $target
        if ($projectRelative.StartsWith('src/') -and
            $targetRelative -match '^(tools|tests)/') {
            Add-Failure "Product project depends on non-product code: $projectRelative -> $targetRelative"
        }
        if ($projectRelative.StartsWith('tools/') -and
            $targetRelative.StartsWith('tests/')) {
            Add-Failure "Product tool depends on test code: $projectRelative -> $targetRelative"
        }
    }
}

$actualEngineeringFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'eng') `
    -Recurse -File | ForEach-Object { Get-RepositoryPath $_.FullName })
Assert-SameSet 'Production engineering files' `
    @($manifest.PublicSource.EngineeringFiles) $actualEngineeringFiles

$actualWorkflowFiles = @(Get-ChildItem `
    -LiteralPath (Join-Path $root '.github\workflows') `
    -Recurse -File | ForEach-Object { Get-RepositoryPath $_.FullName })
Assert-SameSet 'Production workflow files' `
    @($manifest.PublicSource.WorkflowFiles) $actualWorkflowFiles

$actualDevelopmentDocs = @(Get-ChildItem `
    -LiteralPath (Join-Path $root 'docs\development') `
    -Recurse -File | ForEach-Object { Get-RepositoryPath $_.FullName })
Assert-SameSet 'Production development documentation' `
    @($manifest.PublicSource.DevelopmentDocs) $actualDevelopmentDocs

$hostManifest = Import-PowerShellDataFile -LiteralPath `
    (Join-Path $root $manifest.ProductionVisualHostManifest)
$hostSourceRoot = (ConvertTo-RepositoryPath $hostManifest.SourceRoot).TrimEnd('/')
$hostTestRoot = (ConvertTo-RepositoryPath $hostManifest.TestRoot).TrimEnd('/')
$expectedHostSource = @(
    "$hostSourceRoot/$($hostManifest.EntryPoint)"
    @($hostManifest.Assets | ForEach-Object { "$hostSourceRoot/$_" })
    @($hostManifest.Modules | ForEach-Object {
        "$hostSourceRoot/replayfoundry_visual_semantic/$_"
    })
) | ForEach-Object { ConvertTo-RepositoryPath $_ } | Sort-Object -Unique
$expectedHostTests = @($hostManifest.PublicTests | ForEach-Object {
    ConvertTo-RepositoryPath "$hostTestRoot/$_"
}) | Sort-Object -Unique
$actualHostSource = @(Get-ChildItem -LiteralPath (Join-Path $root $hostSourceRoot) `
    -Recurse -File | ForEach-Object { Get-RepositoryPath $_.FullName })
$actualHostTests = @(Get-ChildItem -LiteralPath (Join-Path $root $hostTestRoot) `
    -Recurse -File | ForEach-Object { Get-RepositoryPath $_.FullName })
Assert-SameSet 'Production visual host source' $expectedHostSource $actualHostSource
Assert-SameSet 'Production visual host tests' $expectedHostTests $actualHostTests

foreach ($workflow in $manifest.PublicSource.WorkflowFiles) {
    $text = Get-Content -Raw -LiteralPath (Join-Path $root $workflow)
    foreach ($match in [regex]::Matches(
        $text,
        '(?m)^\s*uses:\s*(?<reference>[^\s#]+)')) {
        $reference = $match.Groups['reference'].Value.Trim('"', "'")
        if ($reference.StartsWith('./') -or $reference.StartsWith('docker://')) {
            continue
        }
        if ($reference -notmatch '^[^@\s]+@[0-9A-Fa-f]{40}$') {
            Add-Failure "Workflow action is not pinned to a full commit SHA: $workflow -> $reference"
        }
    }
}

if ($failures.Count) {
    Write-Error ("Repository architecture guard failed:`n- " + ($failures -join "`n- "))
    exit 1
}

Write-Host 'Repository architecture guard passed: the production project, solution, engineering, workflow, and visual-host surfaces match their exact manifests.'
