param(
    [string] $RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"
$failures = [System.Collections.Generic.List[string]]::new()

function Fail([string] $message) {
    $failures.Add($message)
}

$groups = @(
    @{
        Path = "ReplayFoundry.Desktop\Platform\VisualSemantic\Editorial"
        Filter = "*.cs"
        Maximum = 500
    },
    @{
        Path = "ReplayFoundry.DeveloperTools\VisualSemanticResearch\Prompt2"
        Filter = "*.cs"
        Maximum = 550
    },
    @{
        Path = "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial"
        Filter = "*.py"
        Maximum = 450
    }
)

foreach ($group in $groups) {
    $root = Join-Path $RepositoryRoot $group.Path
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        Fail "Structured-decoding source root is missing: $($group.Path)"
        continue
    }
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Filter $group.Filter) {
        $relative = [System.IO.Path]::GetRelativePath(
            $RepositoryRoot,
            $file.FullName)
        $lines = (Get-Content -LiteralPath $file.FullName).Count
        $maximum =
            if ($file.Name -eq
                "VisualSemanticPrompt2ConfigurationPreparer.cs") {
                1100
            }
            else {
                $group.Maximum
            }
        if ($lines -gt $maximum) {
            Fail "$relative has $lines lines; maximum is $maximum."
        }
        if ($file.BaseName -match
            "(Manager|Managers|Helper|Helpers|Utils|Utilities|Common|Shared|Everything)$") {
            Fail "Catch-all structured-decoding name is prohibited: $relative"
        }
        $text = Get-Content -Raw -LiteralPath $file.FullName
        if ($file.Extension -eq ".cs" -and
            $file.Name -notin @(
                "Qwen3VlEditorialObservationParser.cs",
                "VisualSemanticPrompt2HostSourceHasher.cs"
            ) -and
            $text -match "\bpartial\s+(class|record|struct)\b") {
            Fail "Structured-decoding partial type is prohibited: $relative"
        }
    }
}

$required = @(
    "ReplayFoundry.Desktop\Platform\VisualSemantic\Editorial\Qwen3VlEditorialStructuredDecodingPolicy.cs",
    "ReplayFoundry.DeveloperTools\Commands\RunVisualSemanticPrompt23ConstrainedDevelopmentCommand.cs",
    "ReplayFoundry.DeveloperTools\Commands\EvaluateVisualSemanticPrompt23ConstrainedDevelopmentCommand.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticResearch\Prompt2\VisualSemanticPrompt23QualificationContracts.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticResearch\Prompt2\VisualSemanticPrompt23DevelopmentExecutionAuthorization.cs",
    "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial\structured_decoding_policy.py",
    "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial\constraint_schema.py",
    "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial\structured_decoding.py",
    "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial\constrained_pilot_command.py",
    "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial\constrained_development_command.py",
    "eng\visual-semantic-host\replayfoundry-visual-semantic-structured-decoding-policy-1.0.txt"
)
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $relative))) {
        Fail "Required structured-decoding boundary is missing: $relative"
    }
}

$preparer = Join-Path $RepositoryRoot (
    "ReplayFoundry.DeveloperTools\VisualSemanticResearch\Prompt2\" +
    "VisualSemanticPrompt2ConfigurationPreparer.cs")
if ((Get-Content -Raw -LiteralPath $preparer) -match
    "xgrammar|StructuredDecoding|LogitsProcessor") {
    Fail "Frozen Prompt 2 configuration preparer acquired decoding behavior."
}

$workflow = @(
    (Join-Path $RepositoryRoot "ReplayFoundry.Desktop\App.xaml.cs"),
    (Join-Path $RepositoryRoot "ReplayFoundry.Desktop\Features\Generate")
)
foreach ($path in $workflow) {
    $files =
        if ((Get-Item -LiteralPath $path).PSIsContainer) {
            Get-ChildItem -LiteralPath $path -Recurse -File |
                Where-Object { $_.Extension -in ".cs", ".xaml" }
        }
        else {
            @(Get-Item -LiteralPath $path)
        }
    foreach ($file in $files) {
        if ((Get-Content -Raw -LiteralPath $file.FullName) -match
            "XGrammar|StructuredDecoding|Prompt23Qualification|ConstrainedPilot") {
            Fail "Structured-decoding research leaked into App/Generate: $($file.FullName)"
        }
    }
}

$python = Join-Path $RepositoryRoot (
    "eng\visual-semantic-host\replayfoundry_visual_semantic\editorial")
foreach ($definition in @(
    "class StructuredDecodingSession\b",
    "def build_editorial_schema\(",
    "def run_editorial_constrained_contract_pilot\(",
    "def run_editorial_constrained_development\("
)) {
    $matches = Get-ChildItem -LiteralPath $python -File -Filter "*.py" |
        Select-String -Pattern $definition
    if ($matches.Count -ne 1) {
        Fail "Expected one Python definition for '$definition'; found $($matches.Count)."
    }
}

$developmentProtocol = Get-Content -Raw -LiteralPath (
    Join-Path $RepositoryRoot (
        "ReplayFoundry.DeveloperTools\VisualSemanticProtocol\" +
        "Qwen3VlEditorialDevelopmentProtocol.cs"))
foreach ($command in @(
    '"run-editorial-development"',
    '"run-editorial-constrained-development"'
)) {
    $commandCount = [regex]::Matches(
        $developmentProtocol,
        [regex]::Escape($command)).Count
    if ($commandCount -ne 1) {
        Fail "Historical and constrained Development commands must be explicit: $command"
    }
}

$hostDevelopment = Get-Content -Raw -LiteralPath (
    Join-Path $python "constrained_development_command.py")
if ($hostDevelopment -match
    "(?i)(proxy.?label|expected.?disposition|quality.?metric|holdout)") {
    Fail "Constrained Development host acquired labels, metrics, or Holdout access."
}
if ($hostDevelopment -match "(?i)unconstrained.?fallback\s*=\s*true" -or
    $hostDevelopment -match "(?i)semantic.?repair\s*=\s*true") {
    Fail "Constrained Development host permits fallback or semantic repair."
}

$constrainedArguments = Get-Content -Raw -LiteralPath (
    Join-Path $RepositoryRoot (
        "ReplayFoundry.DeveloperTools\Commands\" +
        "RunVisualSemanticPrompt23ConstrainedDevelopmentArguments.cs"))
if ($constrainedArguments -match "(?i)(label|holdout)") {
    Fail "Constrained Development provider arguments acquired labels or Holdout."
}

foreach ($relative in @(
    "ReplayFoundry.DeveloperTools\Commands\RunVisualSemanticPrompt23ConstrainedDevelopmentCommand.cs",
    "ReplayFoundry.DeveloperTools\Commands\EvaluateVisualSemanticPrompt23ConstrainedDevelopmentCommand.cs",
    "ReplayFoundry.DeveloperTools.Tests\VisualSemanticConstrainedDevelopmentTests.cs"
)) {
    $path = Join-Path $RepositoryRoot $relative
    $maximum = if ($relative -like "*Tests.cs") { 700 } else { 550 }
    $lines = (Get-Content -LiteralPath $path).Count
    if ($lines -gt $maximum) {
        Fail "$relative has $lines lines; maximum is $maximum."
    }
}

$generation = Join-Path $RepositoryRoot (
    "eng\visual-semantic-host\replayfoundry_visual_semantic\generation.py")
if ((Get-Content -LiteralPath $generation).Count -gt 550) {
    Fail "generation.py grew beyond 550 lines."
}

if ($failures.Count -gt 0) {
    Write-Error (
        "Structured-decoding architecture guard failed:`n- " +
        ($failures -join "`n- "))
    exit 1
}

Write-Host (
    "Structured-decoding architecture guard passed: shallow folders, " +
    "bounded files, no Desktop leak, and one decoding/schema implementation.")
