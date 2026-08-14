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

function Get-Text {
    param([string]$RelativePath)

    return Get-Content `
        -Raw `
        -LiteralPath (Join-Path $repositoryRoot $RelativePath)
}

$viewModelPath =
    "ReplayFoundry.Desktop\Features\Generate\GenerateViewModel.cs"
$viewModelFullPath =
    Join-Path $repositoryRoot $viewModelPath
$viewModelLines =
    Get-Content -LiteralPath $viewModelFullPath
$viewModelText =
    $viewModelLines -join "`n"

# The 825-line ceiling is the explicit v0.6A architecture decision. It keeps
# the live binding projection and high-level stage sequencing together while
# removing 663 lines (44.6%) from the 1,488-line baseline aggregate.
if ($viewModelLines.Count -gt 825) {
    Add-Failure (
        "GenerateViewModel exceeds the v0.6A 825-line guardrail: " +
        "$($viewModelLines.Count) lines.")
}

if ($viewModelText -match
    '\bpartial\s+(sealed\s+)?class\s+GenerateViewModel\b') {
    Add-Failure `
        "GenerateViewModel must not use partial files to conceal an aggregate type."
}

$generateViewModelFiles =
    Get-ChildItem `
        -LiteralPath (
            Join-Path $repositoryRoot `
                "ReplayFoundry.Desktop\Features\Generate") `
        -Recurse `
        -File `
        -Filter "*GenerateViewModel*.cs"

if ($generateViewModelFiles.Count -ne 1) {
    Add-Failure (
        "Expected exactly one GenerateViewModel source file; found " +
        "$($generateViewModelFiles.Count).")
}

$prohibitedViewModelPatterns = @{
    "WPF control/window access" =
        'System\.Windows\.(Controls|Media)|\b(Window|UserControl|MessageBox)\b'
    "static application access" =
        'Application\.Current'
    "process/media implementation access" =
        'IProcessRunner|ProcessStartInfo|IMediaProbe|IMediaEvidenceAnalyzer'
    "research/AI tooling access" =
        'Qwen|Python|Whisper|ReplayFoundry\.DeveloperTools'
    "raw cancellation-source ownership" =
        'CancellationTokenSource'
    "replaced selected-source fields" =
        '_selectedSources|_selectedPaths'
    "replaced retained-artifact fields" =
        '_generationSetupOptions|_compositionReviewResult'
    "replaced invalidation helpers" =
        'InvalidateSourceDependentState|InvalidateGenerationSetup|' +
        'InvalidateCompositionReview|InvalidateEvidenceAnalysis'
}

foreach ($entry in $prohibitedViewModelPatterns.GetEnumerator()) {
    if ($viewModelText -match $entry.Value) {
        Add-Failure (
            "GenerateViewModel contains prohibited " +
            "$($entry.Key).")
    }
}

if ($viewModelText -match
    '_(sourcePreparation|evidenceAnalysis)Coordinator\s*\.\s*Invalidate\s*\(') {
    Add-Failure `
        "GenerateViewModel must route dependency invalidation through GenerationWorkflowSessionState."
}

$stateOwnerPaths = @(
    "ReplayFoundry.Desktop\Features\Generate\SourceSelection\GenerationSourceSelectionState.cs",
    "ReplayFoundry.Desktop\Features\Generate\Workflow\GenerationWorkflowSessionState.cs",
    "ReplayFoundry.Desktop\Features\Generate\Workflow\GenerationOperationController.cs"
)

foreach ($stateOwnerPath in $stateOwnerPaths) {
    $text = Get-Text $stateOwnerPath

    if ($text -match
        'System\.Windows|\b(Window|UserControl|MessageBox)\b|' +
        'Application\.Current|IProcessRunner|ProcessStartInfo|' +
        'Qwen|Python|Whisper|ReplayFoundry\.DeveloperTools') {
        Add-Failure `
            "Focused Generate state owner contains a prohibited UI/process/research dependency: $stateOwnerPath"
    }

    if ($text -match '\bShow(Dialog)?\s*\(') {
        Add-Failure `
            "Focused Generate state owner must not open a dialog: $stateOwnerPath"
    }
}

$workflowCollaborators = @{
    "ReplayFoundry.Desktop\Features\Generate\Workflow\GenerateWorkflowCoordinator.cs" =
        "GenerateWorkflowCoordinator"
    "ReplayFoundry.Desktop\Features\Generate\Workflow\GenerateWorkflowPreparationStage.cs" =
        "GenerateWorkflowPreparationStage"
    "ReplayFoundry.Desktop\Features\Generate\Workflow\GenerateWorkflowEvidenceStage.cs" =
        "GenerateWorkflowEvidenceStage"
    "ReplayFoundry.Desktop\Features\Generate\Workflow\GenerateWorkflowExecutionStage.cs" =
        "GenerateWorkflowExecutionStage"
    "ReplayFoundry.Desktop\Features\Generate\Workflow\GenerateWorkflowFailurePresentation.cs" =
        "GenerateWorkflowFailureHandler"
}

foreach ($entry in $workflowCollaborators.GetEnumerator()) {
    $text = Get-Text $entry.Key
    if ($text -match '\bpartial\s+(sealed\s+)?class\b') {
        Add-Failure (
            "Generate workflow collaborators must be explicit types, not " +
            "partial-class fragments: $($entry.Key)")
    }

    if ($text -notmatch
        "\bclass\s+$([regex]::Escape($entry.Value))\b") {
        Add-Failure (
            "Generate workflow collaborator does not declare its expected " +
            "type $($entry.Value): $($entry.Key)")
    }
}

$progressBoundaries = @{
    "ReplayFoundry.Desktop\Features\Generate\Progress\GenerationProgressViewModel.cs" = @{
        Type = "GenerationProgressViewModel"
        MaximumLines = 525
    }
    "ReplayFoundry.Desktop\Features\Generate\Progress\GenerationProgressPresentation.cs" = @{
        Type = "GenerationProgressPresentationFactory"
        MaximumLines = 250
    }
}

foreach ($entry in $progressBoundaries.GetEnumerator()) {
    $lines = Get-Content -LiteralPath (Join-Path $repositoryRoot $entry.Key)
    $text = $lines -join "`n"
    if ($lines.Count -gt $entry.Value.MaximumLines) {
        Add-Failure (
            "Generate progress boundary exceeds its focused line ceiling of " +
            "$($entry.Value.MaximumLines): $($entry.Key) has $($lines.Count) lines.")
    }

    if ($text -match '\bpartial\s+(sealed\s+|static\s+)?class\b') {
        Add-Failure (
            "Generate progress boundaries must be explicit types, not " +
            "partial-class fragments: $($entry.Key)")
    }

    if ($text -notmatch
        "\b(class|record)\s+$([regex]::Escape($entry.Value.Type))\b") {
        Add-Failure (
            "Generate progress boundary does not declare its expected " +
            "type $($entry.Value.Type): $($entry.Key)")
    }

    if ($text -match
        'System\.Windows\.(Controls|Media)|\b(Window|UserControl|MessageBox)\b|' +
        'Application\.Current|IProcessRunner|ProcessStartInfo|' +
        'Qwen|Python|Whisper|ReplayFoundry\.DeveloperTools') {
        Add-Failure (
            "Generate progress boundary contains a prohibited UI, process, " +
            "or research dependency: $($entry.Key)")
    }
}

$progressViewModelText =
    Get-Text (
        "ReplayFoundry.Desktop\Features\Generate\Progress\" +
        "GenerationProgressViewModel.cs")
if ($progressViewModelText -notmatch
    'GenerationProgressPresentationFactory\.(BeginPreparation|BeginEvidenceAnalysis|BeginGeneration)') {
    Add-Failure `
        "GenerationProgressViewModel must delegate immutable stage presentation construction."
}

$compositionFiles =
    Get-ChildItem `
        -LiteralPath (
            Join-Path $repositoryRoot `
                "ReplayFoundry.Desktop\Features\Generate\CompositionReview") `
        -Recurse `
        -File `
        -Filter "*.cs"

foreach ($file in $compositionFiles) {
    $text = Get-Content -Raw -LiteralPath $file.FullName

    if ([regex]::IsMatch(
            $text,
            'catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\))?\s*\{\s*\}',
            [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
        Add-Failure `
            "Empty catch block is prohibited in Composition Review lifecycle code: $($file.FullName)"
    }
}

$compositionWindowText =
    Get-Text (
        "ReplayFoundry.Desktop\Features\Generate\CompositionReview\" +
        "CompositionReviewWindow.xaml.cs")
$requiredCompositionLifecycleFragments = @(
    "CompositionReviewInitializationOutcome",
    "LifecycleCancelled",
    "_dialogCompletionRequested",
    "_isClosed",
    "Loaded -=",
    "Closed -=",
    "_viewModel.CancelRequested -=",
    "_viewModel.FinishRequested -=",
    "_viewModel.Dispose()"
)

foreach ($fragment in $requiredCompositionLifecycleFragments) {
    if ($compositionWindowText -notmatch
        [regex]::Escape($fragment)) {
        Add-Failure `
            "Composition Review lifecycle ownership fragment is missing: $fragment"
    }
}

$compositionViewModelText =
    Get-Text (
        "ReplayFoundry.Desktop\Features\Generate\CompositionReview\" +
        "CompositionReviewViewModel.cs")

if ($compositionViewModelText -notmatch
    'catch\s*\(Exception\s+exception\)[\s\S]*ReportUnexpectedPreviewFailure\s*\(\s*exception\s*\)') {
    Add-Failure `
        "Composition preview observer failures must have an explicit observable owner."
}

$bindingFiles = @(
    "ReplayFoundry.Desktop\Features\Generate\GenerateView.xaml",
    "ReplayFoundry.Desktop\Features\Generate\GenerateStyles.xaml",
    "ReplayFoundry.Desktop\Features\Generate\GenerateResponsiveStyles.xaml",
    "ReplayFoundry.Desktop\Features\Generate\SourceSelection\SourceSelectionView.xaml",
    "ReplayFoundry.Desktop\Features\Generate\ModeSelection\GenerationModeSelector.xaml"
)
$bindingText =
    ($bindingFiles | ForEach-Object { Get-Text $_ }) -join "`n"

$removedLinkIngressPaths = @(
    "ReplayFoundry.Desktop\Features\Generate\SourceSelection\VideoLinkImport.cs",
    "ReplayFoundry.Desktop\Platform\Media\DirectHttpsVideoLinkImportService.cs"
)

foreach ($relativePath in $removedLinkIngressPaths) {
    if (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath)) {
        Add-Failure "Removed network video-ingress source returned: $relativePath"
    }
}

$removedLinkIngressNames = @(
    "IVideoLinkImportService",
    "DirectHttpsVideoLinkImportService",
    "ImportVideoLinkCommand",
    "CanImportVideoLink",
    "VideoLinkStatus",
    "Video Link"
)

$generateProductionText =
    ((Get-ChildItem `
        -LiteralPath (Join-Path $repositoryRoot "ReplayFoundry.Desktop\Features\Generate") `
        -Recurse `
        -File | Where-Object { $_.Extension -in ".cs", ".xaml" }) |
        ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"

foreach ($removedName in $removedLinkIngressNames) {
    if ($generateProductionText -match [regex]::Escape($removedName)) {
        Add-Failure "Removed network video-ingress surface returned: $removedName"
    }
}

$generateViewText = Get-Text $bindingFiles[0]
if ($generateViewText -notmatch
    '<Grid\s+Grid\.Row="1"[\s\S]*?<Border\s+Style="\{DynamicResource Control\.SectionCard\}"\s+Padding="12,9">[\s\S]*?Text="Recent Projects"') {
    Add-Failure `
        "Recent Projects must occupy the full Generate footer after link-ingress removal."
}

$expectedBindingSurface = @(
    "SelectedSources",
    "GenerationProgress",
    "SelectSingleFileCommand",
    "SelectMultipleFilesCommand",
    "ClearSelectionCommand",
    "ContinueToGenerationSetupCommand",
    "IsSourceSelectionVisible",
    "IsProgressVisible",
    "IsIndividualClipsSelected",
    "IsMontageSelected",
    "HasSelectedSources",
    "HasValidationMessage",
    "SelectionSummary",
    "HasGenerationSetup",
    "GenerationSetupButtonText",
    "GenerationSetupSummary",
    "ValidationMessage"
)

foreach ($bindingName in $expectedBindingSurface) {
    if ($bindingText -notmatch
        "\{Binding\s+(?:DataContext\.)?$([regex]::Escape($bindingName))\b") {
        Add-Failure `
            "Expected live Generate XAML binding is missing: $bindingName"
    }

    if ($viewModelText -notmatch
        "public\s+[^\r\n;]+\b$([regex]::Escape($bindingName))\s*(=>|\{)") {
        Add-Failure `
            "GenerateViewModel no longer exposes the live XAML binding: $bindingName"
    }
}

$desktopProjectText =
    Get-Text "ReplayFoundry.Desktop\ReplayFoundry.Desktop.csproj"

if ($desktopProjectText -match
    '<ProjectReference[^>]+ReplayFoundry\.DeveloperTools') {
    Add-Failure `
        "ReplayFoundry.Desktop must not reference ReplayFoundry.DeveloperTools."
}

$removedAggregatePaths = @(
    "ReplayFoundry.Desktop\Features\Generate\GenerateWorkflowManager.cs",
    "ReplayFoundry.Desktop\Features\Generate\GenerateCoordinator.cs"
)

foreach ($relativePath in $removedAggregatePaths) {
    if (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath)) {
        Add-Failure "Prohibited Generate aggregate exists: $relativePath"
    }
}

if ($failures.Count -gt 0) {
    Write-Error (
        "Generate workflow architecture guard failed:`n- " +
        ($failures -join "`n- "))
    exit 1
}

Write-Host (
    "Generate workflow architecture guard passed: " +
    "$($viewModelLines.Count)-line ViewModel, " +
    "$($stateOwnerPaths.Count) focused state owners, " +
    "$($expectedBindingSurface.Count) live bindings, and " +
    "$($compositionFiles.Count) Composition Review files inspected; " +
    "network video ingress remains absent.")
