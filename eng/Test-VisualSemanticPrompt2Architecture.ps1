param(
    [string] $RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"
$failures = [System.Collections.Generic.List[string]]::new()

function Fail([string] $message) {
    $failures.Add($message)
}

$old = @(
    "ReplayFoundry.Desktop\Media\Intelligence\VisualSemantic\VisualSemanticEditorialCanonicalization.cs",
    "ReplayFoundry.Desktop\Media\Intelligence\VisualSemantic\VisualSemanticEditorialEnums.cs",
    "ReplayFoundry.Desktop\Media\Intelligence\VisualSemantic\VisualSemanticEditorialEvidence.cs",
    "ReplayFoundry.Desktop\Media\Intelligence\VisualSemantic\VisualSemanticEditorialObservation.cs",
    "ReplayFoundry.Desktop\Media\Intelligence\VisualSemantic\VisualSemanticEditorialTruthTableValidator.cs",
    "ReplayFoundry.Desktop\Platform\VisualSemantic\Qwen3VlEditorialObservationParser.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticResearch\VisualSemanticPrompt2ConfigurationContracts.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticResearch\VisualSemanticPrompt2ConfigurationPreparer.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticResearch\VisualSemanticPrompt2GatePolicy.cs",
    "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial_contract.py"
)
foreach ($relative in $old) {
    if (Test-Path -LiteralPath (Join-Path $RepositoryRoot $relative)) {
        Fail "Old Prompt 2.0 path remains: $relative"
    }
}

$requiredDurabilityFiles = @(
    "ReplayFoundry.DeveloperTools\VisualSemanticProtocol\Qwen3VlEditorialAttemptCaseParser.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticProtocol\Qwen3VlEditorialPlanCaseReader.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticProtocol\Qwen3VlEditorialAttemptDiagnosticAnalyzer.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticProtocol\Qwen3VlEditorialAttemptRejectedException.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticProtocol\Qwen3VlEditorialExecutionArtifactPaths.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticProtocol\Qwen3VlEditorialContractPilotExecutor.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticResearch\Prompt2\VisualSemanticPrompt2AttemptRecoveryWriter.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticResearch\Prompt2\VisualSemanticPrompt2ExecutionArtifactWorkspace.cs",
    "ReplayFoundry.DeveloperTools\Commands\VisualSemanticPrompt2DurabilityCommandRouter.cs",
    "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial\pilot_command.py",
    "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial\pilot_protocol.py"
)
foreach ($relative in $requiredDurabilityFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $relative))) {
        Fail "Prompt 2.0 durability boundary is missing: $relative"
    }
}

$groups = @(
    @{
        Root = "ReplayFoundry.Desktop\Media\Intelligence\VisualSemantic\Editorial"
        Extension = "*.cs"
        Maximum = 500
    },
    @{
        Root = "ReplayFoundry.Desktop\Platform\VisualSemantic\Editorial"
        Extension = "*.cs"
        Maximum = 500
    },
    @{
        Root = "ReplayFoundry.DeveloperTools\VisualSemanticResearch\Prompt2"
        Extension = "*.cs"
        Maximum = 550
    },
    @{
        Root = "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial"
        Extension = "*.py"
        Maximum = 450
    }
)

foreach ($group in $groups) {
    $root = Join-Path $RepositoryRoot $group.Root
    if (-not (Test-Path -LiteralPath $root)) {
        Fail "Prompt 2.0 root is missing: $($group.Root)"
        continue
    }

    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse -Filter $group.Extension) {
        $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName)
        $nested = [System.IO.Path]::GetRelativePath($root, $file.FullName)
        $lines = (Get-Content -LiteralPath $file.FullName).Count
        $maximum = if ($file.Name -eq "VisualSemanticPrompt2ConfigurationPreparer.cs") {
            1100
        } else {
            $group.Maximum
        }
        if ($lines -gt $maximum) {
            Fail "$relative has $lines lines; maximum is $maximum."
        }
        if ($nested -match "[\\/]") {
            Fail "Prompt 2.0 source is nested too deeply: $relative"
        }
        if ($file.BaseName -match "(Manager|Managers|Helper|Helpers|Utils|Utilities|Everything)$") {
            Fail "Catch-all Prompt 2.0 name is prohibited: $relative"
        }
        $text = Get-Content -Raw -LiteralPath $file.FullName
        if ($file.Extension -eq ".cs" -and
            $file.Name -notin @(
                "Qwen3VlEditorialObservationParser.cs",
                "VisualSemanticPrompt2HostSourceHasher.cs"
            ) -and
            $text -match "\bpartial\s+(class|record|struct)\b") {
            Fail "Prompt 2.0 partial type is prohibited: $relative"
        }
    }
}

$preparer = Join-Path $RepositoryRoot (
    "ReplayFoundry.DeveloperTools\VisualSemanticResearch\Prompt2\" +
    "VisualSemanticPrompt2ConfigurationPreparer.cs")
$preparerText = Get-Content -Raw -LiteralPath $preparer
if ($preparerText -match "IProcessRunner|ProcessStartInfo|Qwen3Vl.*Executor|IVisualSemanticProvider") {
    Fail "Frozen Prompt 2.0 configuration preparer acquired execution behavior."
}

$attemptParser = Join-Path $RepositoryRoot (
    "ReplayFoundry.DeveloperTools\VisualSemanticProtocol\" +
    "Qwen3VlEditorialAttemptParser.cs")
$attemptParserLines = (Get-Content -LiteralPath $attemptParser).Count
if ($attemptParserLines -gt 250) {
    Fail "Prompt 2.0 root/set parser regrew past 250 lines."
}

$executor = Join-Path $RepositoryRoot (
    "ReplayFoundry.DeveloperTools\VisualSemanticProtocol\" +
    "Qwen3VlEditorialDevelopmentPlanExecutor.cs")
$executorText = Get-Content -Raw -LiteralPath $executor
if (
    $executorText -match 'workspace\.(AttemptBatchPath|OutputBatchPath)' -or
    $executorText -notmatch 'Qwen3VlEditorialExecutionArtifactPaths'
) {
    Fail "Prompt 2.0 host attempt paths are not caller-owned."
}

$workflow = @(
    (Join-Path $RepositoryRoot "ReplayFoundry.Desktop\App.xaml.cs"),
    (Join-Path $RepositoryRoot "ReplayFoundry.Desktop\Features\Generate")
)
foreach ($path in $workflow) {
    $files = if ((Get-Item -LiteralPath $path).PSIsContainer) {
        Get-ChildItem -LiteralPath $path -Recurse -File |
            Where-Object { $_.Extension -in ".cs", ".xaml" }
    } else {
        @(Get-Item -LiteralPath $path)
    }
    foreach ($file in $files) {
        $text = Get-Content -Raw -LiteralPath $file.FullName
        if ($text -match "Prompt2|Qwen3VlEditorial") {
            Fail "Prompt 2.0 leaked into App/Generate: $($file.FullName)"
        }
    }
}

$desktopProject = Get-Content -Raw -LiteralPath (
    Join-Path $RepositoryRoot "ReplayFoundry.Desktop\ReplayFoundry.Desktop.csproj")
if ($desktopProject -match "ReplayFoundry\.DeveloperTools") {
    Fail "Desktop references DeveloperTools."
}

$pythonFiles = Get-ChildItem -LiteralPath (
    Join-Path $RepositoryRoot "eng\visual-semantic-host\replayfoundry_visual_semantic") `
    -Recurse -File -Filter "*.py"
$definitions = @(
    "def parse_and_canonicalize_editorial_output",
    "def validate_editorial_plan",
    "def run_editorial_development",
    "def validate_pilot_plan",
    "def run_editorial_contract_pilot",
    "def authorize_sampling"
)
foreach ($definition in $definitions) {
    $matches = $pythonFiles | Select-String -SimpleMatch $definition
    if ($matches.Count -ne 1) {
        Fail "Expected one Python definition for '$definition'; found $($matches.Count)."
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Prompt 2.0 architecture guard failed:`n- " + ($failures -join "`n- "))
    exit 1
}

Write-Host "Prompt 2.0 architecture guard passed."
