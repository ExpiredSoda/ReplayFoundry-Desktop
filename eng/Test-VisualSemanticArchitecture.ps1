param()

$ErrorActionPreference = "Stop"

$repositoryRoot =
    [System.IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot ".."))
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Failure {
    param([string]$Message)

    $failures.Add($Message)
}

function Get-SourceFiles {
    param(
        [string[]]$Paths,
        [string[]]$Extensions = @("*.cs")
    )

    foreach ($path in $Paths) {
        $fullPath = Join-Path $repositoryRoot $path

        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            Get-Item -LiteralPath $fullPath
            continue
        }

        foreach ($extension in $Extensions) {
            Get-ChildItem `
                -LiteralPath $fullPath `
                -Recurse `
                -File `
                -Filter $extension `
                -ErrorAction Stop
        }
    }
}

$desktopProject =
    Join-Path $repositoryRoot `
        "ReplayFoundry.Desktop\ReplayFoundry.Desktop.csproj"
$desktopProjectText =
    Get-Content -Raw -LiteralPath $desktopProject

if ($desktopProjectText -match
    '<ProjectReference[^>]+ReplayFoundry\.DeveloperTools') {
    Add-Failure `
        "ReplayFoundry.Desktop must not reference ReplayFoundry.DeveloperTools."
}

$coreAndPlatform =
    Get-SourceFiles -Paths @(
        "ReplayFoundry.Desktop\Media\Intelligence\VisualSemantic",
        "ReplayFoundry.Desktop\Platform\VisualSemantic"
    )

foreach ($file in $coreAndPlatform) {
    $text = Get-Content -Raw -LiteralPath $file.FullName

    if ($text -match
        'using\s+System\.Windows|System\.Windows\.(Controls|Media|Threading)|\b(Window|UserControl|MessageBox)\b') {
        Add-Failure `
            "WPF dependency is prohibited in visual-semantic core/platform: $($file.FullName)"
    }
}

$workflowTargets =
    Get-SourceFiles `
        -Paths @(
            "ReplayFoundry.Desktop\Features\Generate\GenerateViewModel.cs",
            "ReplayFoundry.Desktop\Features\Generate\Workflow\GenerationModels.cs",
            "ReplayFoundry.Desktop\Features\Generate\Workflow\GenerationPreflightRunner.cs",
            "ReplayFoundry.Desktop\Features\Generate\CompositionReview"
        ) `
        -Extensions @("*.cs", "*.xaml")

foreach ($file in $workflowTargets) {
    $text = Get-Content -Raw -LiteralPath $file.FullName

    if ($text -match
        'Qwen3Vl|IVisualSemanticProvider|Media\.Intelligence\.VisualSemantic|VisualSemanticResearch') {
        Add-Failure `
            "Visual-semantic provider/research dependency is prohibited in App/Generate UI workflow: $($file.FullName)"
    }
}

$compositionRoot =
    Join-Path $repositoryRoot "ReplayFoundry.Desktop\App.xaml.cs"
$compositionRootText =
    Get-Content -Raw -LiteralPath $compositionRoot

# The composition root may instantiate a qualified provider behind production
# interfaces. Research workflows and Prompt 2 execution types must still stay
# outside the application composition boundary.
if ($compositionRootText -match 'VisualSemanticResearch|Prompt2') {
    Add-Failure `
        "Visual-semantic research dependency is prohibited in App.xaml.cs."
}

$desktopSource =
    Get-ChildItem `
        -LiteralPath (Join-Path $repositoryRoot "ReplayFoundry.Desktop") `
        -Recurse `
        -File `
        -Filter "*.cs" |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }

foreach ($file in $desktopSource) {
    if ($file.Name -eq "AssemblyInfo.cs") {
        continue
    }

    $text = Get-Content -Raw -LiteralPath $file.FullName

    if ($text -match
        'using\s+ReplayFoundry\.DeveloperTools|ReplayFoundry\.DeveloperTools\.') {
        Add-Failure `
            "DeveloperTools type leaked into Desktop production source: $($file.FullName)"
    }
}

$removedAggregates = @(
    "ReplayFoundry.Desktop\Media\Intelligence\VisualSemantic\VisualSemanticContracts.cs",
    "ReplayFoundry.DeveloperTools\VisualSemanticResearch\VisualSemanticSamplingAuditAuthorization.cs"
)

foreach ($relativePath in $removedAggregates) {
    if (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath)) {
        Add-Failure "Removed aggregate source returned: $relativePath"
    }
}

$boundedRefactorFiles =
    @(
        Get-SourceFiles -Paths @(
            "ReplayFoundry.Desktop\Media\Intelligence\VisualSemantic",
            "ReplayFoundry.Desktop\Platform\VisualSemantic",
            "ReplayFoundry.DeveloperTools\VisualSemanticResearch"
        )
    ) |
    Where-Object {
        $_.Name -match
            '^(VisualSemantic(Enums|Warnings|Observation|InputManifest|CompositionMetadata|TranscriptContext|Request|BatchRequest|Result|ExecutionManifest|BatchResult|ContractText|EditorialEnums|EditorialEvidence|EditorialObservation|EditorialCanonicalization|EditorialTruthTableValidator|Prompt2ConfigurationContracts|Prompt2GatePolicy)|IVisualSemanticProvider|Qwen3Vl(BatchResultParser|EditorialObservationParser|ParsedResults|ObservationBatchParser|GenerationManifestParser|ExecutionTimingParser|CaseExecutionTimingParser|ObservationCaseParser|ObservationCanonicalizer|StrictJsonPrimitives|VisualSemanticProvider|BatchProcessExecutor|InitializationCoordinator|RuntimeIntegrityVerifier|FailureArtifactReader|AttemptResultCoordinator|ProviderWarningFactory|ProcessOutputReader|ResultMapper)|VisualSemanticResearch(Evaluation|EvaluationContracts|IdentityValidation|TimingValidation|AuditValidation|StratumBuilder|AblationEvaluator|RepeatabilityEvaluator|GenerationMetricCalculator|GateEvaluator|MetricCalculator|CloseoutContracts|CloseoutReader|CloseoutWriter)|VisualSemanticSamplingAudit(AuthorizationContracts|AuthorizationReader|RuntimeCompatibilityEvaluator|StrictArtifactReader|ParityProjector|StrictJsonHelpers)|VisualSemanticExecutionTimingAuthorizationValidator)\.cs$'
    }

foreach ($file in $boundedRefactorFiles) {
    $lineCount =
        (Get-Content -LiteralPath $file.FullName).Count

    if ($lineCount -gt 700) {
        Add-Failure `
            "Refactored visual-semantic file exceeds 700 physical lines ($lineCount): $($file.FullName)"
    }
}

$providerAttemptParserBoundaries = @{
    "ReplayFoundry.Desktop\Platform\VisualSemantic\Qwen3VlProviderAttemptBatchParser.cs" = @(
        "Qwen3VlProviderAttemptBatchParser", 190)
    "ReplayFoundry.Desktop\Platform\VisualSemantic\Qwen3VlProviderAttemptJsonReader.cs" = @(
        "Qwen3VlProviderAttemptJsonReader", 350)
    "ReplayFoundry.Desktop\Platform\VisualSemantic\Qwen3VlProviderCaseAttemptParser.cs" = @(
        "Qwen3VlProviderCaseAttemptParser", 250)
    "ReplayFoundry.Desktop\Platform\VisualSemantic\Qwen3VlProviderAttemptFailureParser.cs" = @(
        "Qwen3VlProviderAttemptFailureParser", 100)
}

foreach ($entry in $providerAttemptParserBoundaries.GetEnumerator()) {
    $fullPath = Join-Path $repositoryRoot $entry.Key
    $lines = Get-Content -LiteralPath $fullPath
    $text = $lines -join "`n"

    if ($lines.Count -gt $entry.Value[1]) {
        Add-Failure (
            "Provider-attempt parser boundary exceeds its focused line " +
            "ceiling of $($entry.Value[1]): $($entry.Key) has $($lines.Count) lines.")
    }

    if ($text -match '\bpartial\s+(class|record|struct)\b') {
        Add-Failure (
            "Provider-attempt parsing must use explicit collaborators, not " +
            "partial fragments: $($entry.Key)")
    }

    if ($text -notmatch
        "\b(class|record|struct)\s+$([regex]::Escape($entry.Value[0]))\b") {
        Add-Failure (
            "Provider-attempt parser boundary does not declare " +
            "$($entry.Value[0]): $($entry.Key)")
    }
}

$providerAttemptFacade =
    Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot (
            "ReplayFoundry.Desktop\Platform\VisualSemantic\" +
            "Qwen3VlProviderAttemptBatchParser.cs"))

if ($providerAttemptFacade -notmatch
    'Qwen3VlProviderCaseAttemptParser\.Parse') {
    Add-Failure `
        "Provider-attempt facade must delegate case parsing to its focused collaborator."
}

$qwenCollaboratorBoundaries = @{
    "Qwen3VlBatchResultParser.cs" = @("Qwen3VlBatchResultParser", 180)
    "Qwen3VlObservationBatchParser.cs" = @("Qwen3VlObservationBatchParser", 210)
    "Qwen3VlObservationCaseParser.cs" = @("Qwen3VlObservationCaseParser", 210)
    "Qwen3VlObservationCanonicalizer.cs" = @("Qwen3VlObservationCanonicalizer", 400)
    "Qwen3VlGenerationManifestParser.cs" = @("Qwen3VlGenerationManifestParser", 310)
    "Qwen3VlExecutionTimingParser.cs" = @("Qwen3VlExecutionTimingParser", 275)
    "Qwen3VlCaseExecutionTimingParser.cs" = @("Qwen3VlCaseExecutionTimingParser", 550)
    "Qwen3VlSamplingAuditParser.cs" = @("Qwen3VlSamplingAuditParser", 250)
    "Qwen3VlSamplingAuditCaseParser.cs" = @("Qwen3VlSamplingAuditCaseParser", 330)
    "Qwen3VlSamplingAuditMetadataParser.cs" = @("Qwen3VlSamplingAuditMetadataParser", 350)
    "Qwen3VlSamplingAuditReconciler.cs" = @("Qwen3VlSamplingAuditReconciler", 250)
    "Qwen3VlHostFailureEnvelopeParser.cs" = @("Qwen3VlHostFailureParser", 170)
    "Qwen3VlHostFailurePayloadParser.cs" = @("Qwen3VlHostFailurePayloadParser", 410)
    "Qwen3VlHostFailureGenerationParser.cs" = @("Qwen3VlHostFailureGenerationParser", 340)
    "Qwen3VlBatchProcessExecutor.cs" = @("Qwen3VlBatchProcessExecutor", 410)
    "Qwen3VlInitializationCoordinator.cs" = @("Qwen3VlInitializationCoordinator", 410)
    "Qwen3VlRuntimeIntegrityVerifier.cs" = @("Qwen3VlRuntimeIntegrityVerifier", 240)
    "Qwen3VlFailureArtifactReader.cs" = @("Qwen3VlFailureArtifactReader", 180)
    "Qwen3VlAttemptResultCoordinator.cs" = @("Qwen3VlAttemptResultCoordinator", 300)
}

$qwenPlatformRoot =
    Join-Path $repositoryRoot `
        "ReplayFoundry.Desktop\Platform\VisualSemantic"

foreach ($entry in $qwenCollaboratorBoundaries.GetEnumerator()) {
    $fullPath = Join-Path $qwenPlatformRoot $entry.Key
    if (-not (Test-Path -LiteralPath $fullPath)) {
        $developerProtocolRoot = Join-Path $repositoryRoot `
            "ReplayFoundry.DeveloperTools\VisualSemanticProtocol"
        $fullPath = Join-Path $developerProtocolRoot $entry.Key
    }
    $lines = Get-Content -LiteralPath $fullPath
    $text = $lines -join "`n"

    if ($lines.Count -gt $entry.Value[1]) {
        Add-Failure (
            "Qwen collaborator exceeds its focused line ceiling of " +
            "$($entry.Value[1]): $($entry.Key) has $($lines.Count) lines.")
    }

    if ($text -match '\bpartial\s+(class|record|struct)\b') {
        Add-Failure (
            "Qwen parsing/execution must use explicit collaborators, not " +
            "partial aggregates: $($entry.Key)")
    }

    if ($text -notmatch
        "\b(class|record|struct)\s+$([regex]::Escape($entry.Value[0]))\b") {
        Add-Failure (
            "Qwen collaborator does not declare $($entry.Value[0]): " +
            "$($entry.Key)")
    }
}

$qwenBatchFacade =
    Get-Content -Raw -LiteralPath (
        Join-Path $qwenPlatformRoot "Qwen3VlBatchResultParser.cs")

if ($qwenBatchFacade -notmatch
        'Qwen3VlObservationBatchParser\.Parse' -or
    $qwenBatchFacade -notmatch
        'Qwen3VlEditorialObservationParser\.Parse') {
    Add-Failure `
        "Qwen batch facade must delegate observation and editorial parsing."
}

$qwenProcessExecutor =
    Get-Content -Raw -LiteralPath (
        Join-Path $qwenPlatformRoot "Qwen3VlBatchProcessExecutor.cs")

if ($qwenProcessExecutor -notmatch
        'Qwen3VlInitializationCoordinator' -or
    $qwenProcessExecutor -notmatch
        'Qwen3VlRuntimeIntegrityVerifier' -or
    $qwenProcessExecutor -notmatch
        'Qwen3VlFailureArtifactReader') {
    Add-Failure `
        "Qwen process orchestration must retain explicit initialization, integrity, and failure collaborators."
}

# Explicit v0.7A architecture decision: the atomic, retained-JSON-only
# configuration preparer keeps validation, parity projection, staging, and
# artifact hashing in one transaction boundary. It is not a workflow manager,
# has no process/provider dependency, and is separately capped here rather
# than hidden with partial classes.
$prompt2Preparer =
    Join-Path $repositoryRoot (
        "ReplayFoundry.DeveloperTools\VisualSemanticResearch\Prompt2\" +
        "VisualSemanticPrompt2ConfigurationPreparer.cs")
$prompt2PreparerLines =
    (Get-Content -LiteralPath $prompt2Preparer).Count
$prompt2PreparerText =
    Get-Content -Raw -LiteralPath $prompt2Preparer

if ($prompt2PreparerLines -gt 1100) {
    Add-Failure (
        "Prompt 2 atomic configuration preparer exceeds its v0.7A " +
        "1,100-line architecture decision: $prompt2PreparerLines lines.")
}

if ($prompt2PreparerText -match
    'IProcessRunner|IVisualSemanticProvider|Qwen3VlVisualSemanticProvider|' +
    'IMediaProbe|IMediaEvidenceAnalyzer|System\.Diagnostics\.Process|' +
    'ProcessStartInfo') {
    Add-Failure `
        "Prompt 2 configuration preparation must remain retained-JSON-only."
}

$pythonEntry =
    Join-Path $repositoryRoot `
        "eng\visual-semantic-host\qwen3_vl_batch_host.py"
$pythonEntryLines =
    Get-Content -LiteralPath $pythonEntry
$pythonEntryText = $pythonEntryLines -join "`n"

if ($pythonEntryLines.Count -gt 80 -or
    $pythonEntryText -match
        '^def\s+_|^class\s+|def\s+_(process|audit|infer|parse|generate)' -or
    $pythonEntryText -notmatch
        'from replayfoundry_visual_semantic\.cli import main') {
    Add-Failure `
        "The Python host entry point must remain a thin delegation-only executable."
}

$pythonPackage =
    Join-Path $repositoryRoot `
        "eng\visual-semantic-host\replayfoundry_visual_semantic"
$pythonFiles =
    Get-ChildItem `
        -LiteralPath $pythonPackage `
        -File `
        -Filter "*.py"
$uniquePythonDefinitions = @(
    "_canonical_json_sha256",
    "_canonicalize_provider_collections",
    "_bind_trusted_identity",
    "_verify_actual_pts_sampling",
    "_validate_qwen_sampling_structure"
)

foreach ($definition in $uniquePythonDefinitions) {
    $count =
        @(
            $pythonFiles |
                Select-String `
                    -Pattern "^def $([regex]::Escape($definition))\("
        ).Count

    if ($count -ne 1) {
        Add-Failure `
            "Expected exactly one Python definition for '$definition'; found $count."
    }
}

$providerPath =
    Join-Path $repositoryRoot `
        "ReplayFoundry.Desktop\Platform\VisualSemantic\Qwen3VlVisualSemanticProvider.cs"
$providerText =
    Get-Content -Raw -LiteralPath $providerPath

if ($providerText -notmatch
    'Qwen3VlVisualSemanticProvider\s*:\s*\r?\n\s*IVisualSemanticProvider') {
    Add-Failure `
        "Qwen3VlVisualSemanticProvider must remain behind IVisualSemanticProvider."
}

$closeoutFiles =
    Get-SourceFiles -Paths @(
        "ReplayFoundry.DeveloperTools\Commands\CloseoutVisualSemanticResearchCommand.cs",
        "ReplayFoundry.DeveloperTools\VisualSemanticResearch\VisualSemanticResearchCloseoutReader.cs"
    )

foreach ($file in $closeoutFiles) {
    $text = Get-Content -Raw -LiteralPath $file.FullName

    if ($text -match
        'IProcessRunner|IMediaProbe|IMediaEvidenceAnalyzer|IVisualSemanticProvider|Qwen3VlVisualSemanticProvider|MomentFinder|FFmpeg|ffprobe|whisper') {
        Add-Failure `
            "Research closeout must remain retained-JSON-only: $($file.FullName)"
    }
}

$prompt2WorkflowPatterns =
    'Prompt2|EditorialObservation|EditorialDisposition'

foreach ($file in $workflowTargets) {
    $text = Get-Content -Raw -LiteralPath $file.FullName

    if ($text -match $prompt2WorkflowPatterns) {
        Add-Failure `
            "Prompt 2 must remain outside App/Generate UI workflow: $($file.FullName)"
    }
}

if ($failures.Count -gt 0) {
    Write-Error (
        "Visual-semantic architecture guard failed:`n- " +
        ($failures -join "`n- "))
    exit 1
}

Write-Host (
    "Visual-semantic architecture guard passed: " +
    "$($coreAndPlatform.Count) core/platform files, " +
    "$($workflowTargets.Count) App/Generate boundary files, and " +
    "$($pythonFiles.Count) Python package modules inspected.")
