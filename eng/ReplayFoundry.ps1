[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('help', 'restore', 'build', 'test', 'architecture', 'verify', 'run')]
    [string]$Command = 'help',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoRestore,
    [switch]$SkipPython,
    [string]$PythonExecutable,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $PSScriptRoot 'ReplayFoundry.Repository.psd1'
$repository = Import-PowerShellDataFile -LiteralPath $manifestPath

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$RelativePath)

    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $RelativePath))
}

function Resolve-RequiredRepositoryPath {
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = Resolve-RepositoryPath $RelativePath
    if (Test-Path -LiteralPath $path) { return $path }
    throw "Required repository path is missing: $RelativePath"
}

function Assert-RepositoryProfile {
    if ($repository.SchemaVersion -ne 1) {
        throw "Unsupported repository manifest schema version: $($repository.SchemaVersion)"
    }
    $requiredFiles = @(
        $repository.RepositorySolution
        $repository.ProductProjects
        $repository.ToolProjects
        $repository.TestSupportProjects
        $repository.DotNetTestProjects
        $repository.ArchitectureGuards
        $repository.PublicSource.RequiredFiles
    )
    foreach ($relativePath in $requiredFiles | Sort-Object -Unique) {
        [void](Resolve-RequiredRepositoryPath $relativePath)
    }
    $pythonTests = Resolve-RepositoryPath $repository.PythonTestRoot
    if (-not (Test-Path -LiteralPath $pythonTests -PathType Container)) {
        throw "Required Python test directory is missing: $($repository.PythonTestRoot)"
    }
}

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
    Invoke-External dotnet @(
        'build', (Resolve-RepositoryPath $repository.RepositorySolution),
        '-c', $Configuration, '--no-restore'
    )
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

function Invoke-ArchitectureGuards {
    foreach ($relativePath in $repository.ArchitectureGuards) {
        $path = Resolve-RequiredRepositoryPath $relativePath
        Write-Host "`n==> $([IO.Path]::GetFileName($relativePath))" -ForegroundColor Cyan
        Invoke-ArchitectureGuard $path
    }
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
    $project = Resolve-RequiredRepositoryPath $repository.ProductProjects[0]
    [string[]]$arguments = @(
        'run', '--project', $project,
        '-c', $Configuration, '--'
    )
    if ($null -ne $RemainingArguments) { $arguments += $RemainingArguments }
    Invoke-External dotnet $arguments
}

function Show-Help {
    @(
        'Replay Foundry production source console'
        ''
        '  .\eng\ReplayFoundry.ps1 build [-Configuration Debug|Release]'
        '  .\eng\ReplayFoundry.ps1 test [-SkipPython] [-PythonExecutable <path>]'
        '  .\eng\ReplayFoundry.ps1 architecture'
        '  .\eng\ReplayFoundry.ps1 verify [-PythonExecutable <path>]'
        '  .\eng\ReplayFoundry.ps1 run [-- <desktop arguments>]'
        ''
        'verify restores, builds, runs the production test suites, and applies every'
        'architecture, security, UI/UX, installer, and release guard in this source profile.'
    ) -join [Environment]::NewLine | Write-Host
}

Assert-RepositoryProfile
Push-Location $repositoryRoot
try {
    switch ($Command) {
        'restore' { Invoke-Restore }
        'build' { Invoke-CompilePipeline }
        'test' { Invoke-TestPipeline }
        'architecture' { Invoke-ArchitectureGuards }
        'verify' { Invoke-VerificationPipeline }
        'run' { Invoke-Desktop }
        default { Show-Help }
    }
} finally {
    Pop-Location
}
