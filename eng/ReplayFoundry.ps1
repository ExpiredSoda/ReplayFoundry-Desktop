[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet(
        'help', 'restore', 'build', 'test', 'architecture', 'verify',
        'run', 'console', 'export')]
    [string]$Command = 'help',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoRestore,
    [switch]$SkipPython,
    [string]$PythonExecutable,
    [string]$Destination,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $PSScriptRoot 'ReplayFoundry.Repository.psd1'
$repository = Import-PowerShellDataFile -LiteralPath $manifestPath
$productionHostManifest = Import-PowerShellDataFile -LiteralPath `
    (Join-Path $repositoryRoot $repository.ProductionVisualHostManifest)
$isPublicSnapshot = Test-Path -LiteralPath `
    (Join-Path $repositoryRoot '.replayfoundry-public-source') -PathType Leaf

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$RelativePath)

    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $RelativePath))
}

function ConvertTo-RepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    return $Path.Replace('\', '/').TrimStart('/')
}

function Test-PublicExportPath {
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = ConvertTo-RepositoryPath $RelativePath
    if ($publicExcludedFiles.Contains($path)) { return $false }
    if ($publicExactFiles.Contains($path)) { return $true }
    foreach ($root in $publicExportRoots) {
        if ($path.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
            $path.StartsWith($root + '/', [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Use-PublicRepositoryProfile {
    $profileKeys = @(
        'ProductProjects', 'ToolProjects', 'TestSupportProjects',
        'DotNetTestProjects', 'ArchitectureGuards'
    )
    foreach ($key in $profileKeys) {
        $repository[$key] = @($repository[$key] | Where-Object {
            Test-PublicExportPath $_
        })
    }
}

function Assert-PublicSnapshotBoundary {
    foreach ($relativePath in $repository.PublicExport.ForbiddenRoots) {
        if (Test-Path -LiteralPath (Resolve-RepositoryPath $relativePath)) {
            throw "Public-source marker conflicts with development-only content: $relativePath"
        }
    }
    foreach ($relativePath in $repository.PublicExport.ExcludedFiles) {
        if (Test-Path -LiteralPath (Resolve-RepositoryPath $relativePath)) {
            throw "Public-source marker conflicts with an excluded file: $relativePath"
        }
    }
    $hostRoot = ConvertTo-RepositoryPath $productionHostManifest.SourceRoot
    $testRoot = ConvertTo-RepositoryPath $productionHostManifest.TestRoot
    foreach ($relativePath in @(
        @($productionHostManifest.ForbiddenSourceFiles | ForEach-Object {
            "$hostRoot/$_"
        })
        @($productionHostManifest.ForbiddenPublicTests | ForEach-Object {
            "$testRoot/$_"
        })
    )) {
        if (Test-Path -LiteralPath (Resolve-RepositoryPath $relativePath)) {
            throw "Public-source marker conflicts with developer-only visual-semantic content: $relativePath"
        }
    }
}

function Assert-RepositoryProfile {
    $requiredFiles = @(
        $repository.RepositorySolution
        $repository.ProductProjects
        $repository.ToolProjects
        $repository.TestSupportProjects
        $repository.DotNetTestProjects
        $repository.ArchitectureGuards
    )
    if ($isPublicSnapshot) {
        $requiredFiles += @($repository.PublicExport.RootFiles)
        $requiredFiles += @($repository.PublicExport.ExactFiles)
        $requiredFiles += @($repository.PublicExport.RequiredFiles)
        $hostRoot = ConvertTo-RepositoryPath $productionHostManifest.SourceRoot
        $testRoot = ConvertTo-RepositoryPath $productionHostManifest.TestRoot
        $requiredFiles += "$hostRoot/$($productionHostManifest.EntryPoint)"
        $requiredFiles += @($productionHostManifest.Assets | ForEach-Object {
            "$hostRoot/$_"
        })
        $requiredFiles += @($productionHostManifest.Modules | ForEach-Object {
            "$hostRoot/replayfoundry_visual_semantic/$_"
        })
        $requiredFiles += @($productionHostManifest.PublicTests | ForEach-Object {
            "$testRoot/$_"
        })
    }
    foreach ($relativePath in $requiredFiles | Sort-Object -Unique) {
        $path = Resolve-RepositoryPath $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required repository-profile file is missing: $relativePath"
        }
    }

    $pythonTests = Resolve-RepositoryPath $repository.PythonTestRoot
    if (-not (Test-Path -LiteralPath $pythonTests -PathType Container)) {
        throw "Required repository-profile directory is missing: $($repository.PythonTestRoot)"
    }
}

$publicExportRoots = @($repository.PublicExport.Roots | ForEach-Object {
    (ConvertTo-RepositoryPath $_).TrimEnd('/')
})
$publicExactFiles = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($relativePath in @(
    $repository.PublicExport.RootFiles
    $repository.PublicExport.ExactFiles
)) {
    [void]$publicExactFiles.Add((ConvertTo-RepositoryPath $relativePath))
}
$publicExcludedFiles = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($relativePath in $repository.PublicExport.ExcludedFiles) {
    [void]$publicExcludedFiles.Add((ConvertTo-RepositoryPath $relativePath))
}
if ($isPublicSnapshot) {
    Assert-PublicSnapshotBoundary
    Use-PublicRepositoryProfile
}
Assert-RepositoryProfile

function Invoke-External {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Restore {
    Invoke-External dotnet @('restore', (Resolve-RepositoryPath $repository.RepositorySolution))
}

function Invoke-Build {
    $arguments = @(
        'build', (Resolve-RepositoryPath $repository.RepositorySolution),
        '-c', $Configuration, '--no-restore'
    )
    Invoke-External dotnet $arguments
}

function Invoke-DotNetTests {
    foreach ($project in $repository.DotNetTestProjects) {
        $projectPath = Resolve-RequiredRepositoryPath $project
        Write-Host "`n==> $([IO.Path]::GetFileNameWithoutExtension($project))" -ForegroundColor Cyan
        Invoke-External dotnet @(
            'run', '--project', $projectPath,
            '--no-build', '-c', $Configuration
        )
    }
}

function Resolve-RequiredRepositoryPath {
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = Resolve-RepositoryPath $RelativePath
    if (Test-Path -LiteralPath $path) { return $path }
    throw "Required repository path is missing: $RelativePath"
}

function Resolve-Python {
    if (-not [string]::IsNullOrWhiteSpace($PythonExecutable)) {
        return $PythonExecutable
    }

    if (-not [string]::IsNullOrWhiteSpace($env:REPLAYFOUNDRY_PYTHON)) {
        return $env:REPLAYFOUNDRY_PYTHON
    }

    $pythonCandidates = @(Get-Command python -CommandType Application -ErrorAction SilentlyContinue)
    foreach ($python in $pythonCandidates) {
        $source = [string]$python.Source
        if ([string]::IsNullOrWhiteSpace($source) -or
            $source -match '[\\/]Microsoft[\\/]WindowsApps[\\/]python(?:3(?:\.\d+)?)?\.exe$') {
            continue
        }
        return $source
    }
    return $null
}

function Invoke-PythonTests {
    if ($SkipPython) { return }
    $python = Resolve-Python
    if ([string]::IsNullOrWhiteSpace($python)) {
        throw 'Python tests require -PythonExecutable or REPLAYFOUNDRY_PYTHON.'
    }

    $originalPythonPath = $env:PYTHONPATH
    try {
        Use-PinnedPythonPackages $python
        Invoke-External $python @(
            '-B', '-m', 'unittest', 'discover',
            '-s', (Resolve-RepositoryPath $repository.PythonTestRoot),
            '-p', 'test_*.py', '-v'
        )
    } finally {
        $env:PYTHONPATH = $originalPythonPath
    }
}

function Use-PinnedPythonPackages {
    param([Parameter(Mandatory)][string]$Executable)

    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { return }
    $runtimeRoot = Split-Path -Parent (Split-Path -Parent $Executable)
    $sitePackages = Join-Path $runtimeRoot 'site-packages'
    if (-not (Test-Path -LiteralPath $sitePackages -PathType Container)) { return }
    $env:PYTHONPATH = if ([string]::IsNullOrWhiteSpace($env:PYTHONPATH)) {
        $sitePackages
    } else {
        "$sitePackages$([IO.Path]::PathSeparator)$env:PYTHONPATH"
    }
}

function Invoke-ArchitectureGuards {
    foreach ($relativePath in $repository.ArchitectureGuards) {
        $path = Resolve-RequiredRepositoryPath $relativePath
        Write-Host "`n==> $([IO.Path]::GetFileName($relativePath))" -ForegroundColor Cyan
        Invoke-ArchitectureGuard $path
    }
}

function Invoke-ArchitectureGuard {
    param([Parameter(Mandatory)][string]$Path)

    $parameters = (Get-Command $Path).Parameters
    $hostExecutable = (Get-Process -Id $PID).Path
    $arguments = @('-NoProfile', '-File', $Path)
    if ($parameters.ContainsKey('RepositoryRoot')) {
        $arguments += @('-RepositoryRoot', $repositoryRoot)
    }

    Invoke-External $hostExecutable $arguments
}

function Invoke-CompilePipeline {
    if (-not $NoRestore) { Invoke-Restore }
    Invoke-Build
}

function Invoke-TestPipeline {
    Invoke-CompilePipeline
    Invoke-DotNetTests
    Invoke-PythonTests
}

function Invoke-VerificationPipeline {
    Invoke-TestPipeline
    Invoke-ArchitectureGuards
}

function Invoke-Desktop {
    $project = Resolve-RepositoryPath $repository.ProductProjects[0]
    [string[]]$arguments = @(
        'run', '--project', $project,
        '-c', $Configuration, '--'
    )
    if ($null -ne $RemainingArguments) {
        $arguments += $RemainingArguments
    }
    $overrideName = 'REPLAYFOUNDRY_ENABLE_QWEN_DEVELOPMENT_OVERRIDES'
    $previousOverride = [Environment]::GetEnvironmentVariable(
        $overrideName,
        'Process')
    try {
        if ($Configuration -eq 'Debug') {
            [Environment]::SetEnvironmentVariable(
                $overrideName,
                '1',
                'Process')
        }
        Invoke-External -Executable 'dotnet' -Arguments $arguments
    } finally {
        [Environment]::SetEnvironmentVariable(
            $overrideName,
            $previousOverride,
            'Process')
    }
}

function Invoke-DeveloperConsole {
    $project = $repository.ToolProjects |
        Where-Object { $_ -like '*ReplayFoundry.DeveloperTools.csproj' } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($project)) {
        throw 'The developer console is not included in this repository profile.'
    }
    $project = Resolve-RequiredRepositoryPath $project
    $arguments = @(
        'run', '--project', $project,
        '-c', $Configuration, '--'
    ) + @($RemainingArguments)
    Invoke-External dotnet $arguments
}

function Invoke-PublicExport {
    if ([string]::IsNullOrWhiteSpace($Destination)) {
        throw 'The export command requires -Destination <empty-directory>.'
    }

    $exporter = Join-Path $PSScriptRoot `
        'Export-ReplayFoundryProductionRepository.ps1'
    if (-not (Test-Path -LiteralPath $exporter -PathType Leaf)) {
        throw 'Production export is not included in this repository profile.'
    }
    & $exporter -Destination $Destination
}

function Show-Help {
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('Replay Foundry repository console')
    $lines.Add('')
    $lines.Add('  .\eng\ReplayFoundry.ps1 build [-Configuration Debug|Release]')
    $lines.Add('  .\eng\ReplayFoundry.ps1 test [-SkipPython] [-PythonExecutable <path>]')
    $lines.Add('  .\eng\ReplayFoundry.ps1 architecture')
    $lines.Add('  .\eng\ReplayFoundry.ps1 verify [-PythonExecutable <path>]')
    $lines.Add('  .\eng\ReplayFoundry.ps1 run [-- <desktop arguments>]')
    if ($repository.ToolProjects -like '*ReplayFoundry.DeveloperTools.csproj') {
        $lines.Add('  .\eng\ReplayFoundry.ps1 console [-- <developer-tool command>]')
    }
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot `
        'Export-ReplayFoundryProductionRepository.ps1') -PathType Leaf) {
        $lines.Add('  .\eng\ReplayFoundry.ps1 export -Destination <empty-directory>')
    }
    $lines.Add('')
    $lines.Add('verify is the complete local gate: restore, build, all .NET and Python tests,')
    $lines.Add('then every architecture, security, UI/UX, runtime, installer, and release guard.')
    $lines.Add('Debug run uses the checked-out visual host with the verified shared model/runtime.')
    ($lines -join [Environment]::NewLine) | Write-Host
}

Push-Location $repositoryRoot
try {
    switch ($Command) {
        'restore' { Invoke-Restore }
        'build' { Invoke-CompilePipeline }
        'test' { Invoke-TestPipeline }
        'architecture' { Invoke-ArchitectureGuards }
        'verify' { Invoke-VerificationPipeline }
        'run' { Invoke-Desktop }
        'console' { Invoke-DeveloperConsole }
        'export' { Invoke-PublicExport }
        default { Show-Help }
    }
} finally {
    Pop-Location
}
