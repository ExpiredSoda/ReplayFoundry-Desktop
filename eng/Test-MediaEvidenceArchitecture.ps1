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

$boundaries = @{
    "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceCommandBuilder.cs" = @(
        "FfmpegEvidenceCommandBuilder", 180)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceArgumentBuilder.cs" = @(
        "FfmpegEvidenceArgumentBuilder", 120)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceFilterLabels.cs" = @(
        "FfmpegEvidenceFilterLabels", 100)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegSceneFilterGraphBuilder.cs" = @(
        "FfmpegSceneFilterGraphBuilder", 150)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegVisualIntervalFilterGraphBuilder.cs" = @(
        "FfmpegVisualIntervalFilterGraphBuilder", 190)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegVisualTargetFilterGraphBuilder.cs" = @(
        "FfmpegVisualTargetFilterGraphBuilder", 190)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegAudioEvidenceCommandBuilder.cs" = @(
        "FfmpegAudioEvidenceCommandBuilder", 240)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceResultParser.cs" = @(
        "FfmpegEvidenceResultParser", 240)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceMetadataKeys.cs" = @(
        "FfmpegEvidenceMetadataKeys", 100)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceParseAccumulators.cs" = @(
        "FfmpegEvidenceParseAccumulators", 150)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceValueParser.cs" = @(
        "FfmpegEvidenceValueParser", 250)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceRecordAttribution.cs" = @(
        "FfmpegEvidenceRecordAttribution", 210)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegVisualEvidenceRecordParser.cs" = @(
        "FfmpegVisualEvidenceRecordParser", 430)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegVisualIntervalPairing.cs" = @(
        "FfmpegVisualIntervalPairing", 150)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegAudioSignalRecordParser.cs" = @(
        "FfmpegAudioSignalRecordParser", 330)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegAudioIntervalPairing.cs" = @(
        "FfmpegAudioIntervalPairing", 200)
    "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceCoverageBuilder.cs" = @(
        "FfmpegEvidenceCoverageBuilder", 180)
    "ReplayFoundry.Desktop\Media\Analysis\Summaries\MediaEvidenceSummaryBuilder.cs" = @(
        "MediaEvidenceSummaryBuilder", 240)
    "ReplayFoundry.Desktop\Media\Analysis\Summaries\MediaEvidenceSummaryMath.cs" = @(
        "MediaEvidenceSummaryMath", 130)
    "ReplayFoundry.Desktop\Media\Analysis\Summaries\SceneEvidenceSummaryBuilder.cs" = @(
        "SceneEvidenceSummaryBuilder", 180)
    "ReplayFoundry.Desktop\Media\Analysis\Summaries\SilenceIntervalSummaryBuilder.cs" = @(
        "SilenceIntervalSummaryBuilder", 150)
    "ReplayFoundry.Desktop\Media\Analysis\Summaries\AudioStreamSignalSummaryBuilder.cs" = @(
        "AudioStreamSignalSummaryBuilder", 180)
    "ReplayFoundry.Desktop\Media\Analysis\Summaries\AudioStreamSilenceSummaryBuilder.cs" = @(
        "AudioStreamSilenceSummaryBuilder", 150)
    "ReplayFoundry.Desktop\Media\Analysis\Summaries\VisualTargetEvidenceSummaryBuilder.cs" = @(
        "VisualTargetEvidenceSummaryBuilder", 120)
    "ReplayFoundry.Desktop\Media\Analysis\Summaries\VisualTargetSignalSummaryBuilder.cs" = @(
        "VisualTargetSignalSummaryBuilder", 180)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceConsoleWriter.cs" = @(
        "MediaEvidenceConsoleWriter", 180)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceConsoleFormatting.cs" = @(
        "MediaEvidenceConsoleFormatting", 100)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceConsoleLimits.cs" = @(
        "MediaEvidenceConsoleLimits", 40)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceCollectionConsoleWriter.cs" = @(
        "MediaEvidenceCollectionConsoleWriter", 130)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceFailureConsoleWriter.cs" = @(
        "MediaEvidenceFailureConsoleWriter", 70)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceSceneConsoleWriter.cs" = @(
        "MediaEvidenceSceneConsoleWriter", 130)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceVisualSignalConsoleWriter.cs" = @(
        "MediaEvidenceVisualSignalConsoleWriter", 110)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceRegionConsoleWriter.cs" = @(
        "MediaEvidenceRegionConsoleWriter", 160)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceAudioConsoleWriter.cs" = @(
        "MediaEvidenceAudioConsoleWriter", 160)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceAudioSignalConsoleWriter.cs" = @(
        "MediaEvidenceAudioSignalConsoleWriter", 150)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceManifestConsoleWriter.cs" = @(
        "MediaEvidenceManifestConsoleWriter", 190)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceJsonExporter.cs" = @(
        "MediaEvidenceJsonExporter", 110)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceJsonDocumentFactory.cs" = @(
        "MediaEvidenceJsonDocumentFactory", 270)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceJsonCoverageProjection.cs" = @(
        "MediaEvidenceJsonCoverageProjection", 110)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceJsonInspectionProjection.cs" = @(
        "MediaEvidenceJsonInspectionProjection", 150)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceJsonManifestProjection.cs" = @(
        "MediaEvidenceJsonManifestProjection", 140)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceJsonSignalProjection.cs" = @(
        "MediaEvidenceJsonSignalProjection", 130)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceJsonSummaryProjection.cs" = @(
        "MediaEvidenceJsonSummaryProjection", 140)
    "ReplayFoundry.DeveloperTools\Output\MediaEvidenceJsonTargetProjection.cs" = @(
        "MediaEvidenceJsonTargetProjection", 170)
}

foreach ($entry in $boundaries.GetEnumerator()) {
    $fullPath = Join-Path $repositoryRoot $entry.Key
    if (-not (Test-Path -LiteralPath $fullPath)) {
        Add-Failure "FFmpeg evidence command boundary is missing: $($entry.Key)"
        continue
    }

    $lines = Get-Content -LiteralPath $fullPath
    $text = $lines -join "`n"
    $expectedType = $entry.Value[0]
    $maximumLines = $entry.Value[1]

    if ($lines.Count -gt $maximumLines) {
        Add-Failure (
            "$($entry.Key) has $($lines.Count) lines; maximum is " +
            "$maximumLines.")
    }

    if ($text -match '\bpartial\s+(class|record|struct)\b') {
        Add-Failure (
            "FFmpeg evidence command boundaries must not use partial types: " +
            "$($entry.Key)")
    }

    if ($text -notmatch
        "\b(class|record|struct)\s+$([regex]::Escape($expectedType))\b") {
        Add-Failure "$($entry.Key) does not declare $expectedType."
    }
}

$removedFragments = @(
    "ReplayFoundry.Desktop\Platform\Media\FfmpegAudioFilterGraphBuilder.cs"
)

foreach ($relativePath in $removedFragments) {
    if (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath)) {
        Add-Failure "Replaced FFmpeg evidence fragment remains: $relativePath"
    }
}

$facade =
    Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot `
            "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceCommandBuilder.cs")
$requiredDelegations = @(
    "FfmpegEvidenceArgumentBuilder.ValidateTargets",
    "FfmpegSceneFilterGraphBuilder.Build",
    "FfmpegVisualIntervalFilterGraphBuilder.Build",
    "FfmpegAudioEvidenceCommandBuilder.BuildArguments"
)

foreach ($delegation in $requiredDelegations) {
    if ($facade -notmatch [regex]::Escape($delegation)) {
        Add-Failure "FFmpeg evidence facade delegation is missing: $delegation"
    }
}

$parserFacade =
    Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot `
            "ReplayFoundry.Desktop\Platform\Media\FfmpegEvidenceResultParser.cs")
$requiredParserDelegations = @(
    "FfmpegVisualEvidenceRecordParser.ParseSceneProcessRecords",
    "FfmpegVisualEvidenceRecordParser.ParseVisualIntervalProcessRecords",
    "FfmpegEvidenceRecordAttribution.TryGetRecordKind",
    "FfmpegAudioIntervalPairing.PairAudioIntervals",
    "FfmpegAudioSignalRecordParser.NormalizeAudioSignalWindows",
    "FfmpegAudioSignalRecordParser.CreateAudioCoverage"
)

foreach ($delegation in $requiredParserDelegations) {
    if ($parserFacade -notmatch [regex]::Escape($delegation)) {
        Add-Failure "FFmpeg evidence parser facade delegation is missing: $delegation"
    }
}

$summaryFacade =
    Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot `
            "ReplayFoundry.Desktop\Media\Analysis\Summaries\MediaEvidenceSummaryBuilder.cs")
$requiredSummaryDelegations = @(
    "AudioStreamSignalSummaryBuilder.Build",
    "AudioStreamSilenceSummaryBuilder.Build",
    "VisualTargetEvidenceSummaryBuilder.Build",
    "VisualTargetSignalSummaryBuilder.Build",
    "SceneEvidenceSummaryBuilder.Build"
)

foreach ($delegation in $requiredSummaryDelegations) {
    if ($summaryFacade -notmatch [regex]::Escape($delegation)) {
        Add-Failure "Media evidence summary facade delegation is missing: $delegation"
    }
}

$consoleFacade =
    Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot `
            "ReplayFoundry.DeveloperTools\Output\MediaEvidenceConsoleWriter.cs")
$requiredConsoleDelegations = @(
    "MediaEvidenceSceneConsoleWriter.WriteSummary",
    "MediaEvidenceVisualSignalConsoleWriter.Write",
    "MediaEvidenceRegionConsoleWriter.Write",
    "MediaEvidenceAudioConsoleWriter.Write",
    "MediaEvidenceAudioSignalConsoleWriter.Write",
    "MediaEvidenceManifestConsoleWriter.Write",
    "MediaEvidenceFailureConsoleWriter.Write"
)

foreach ($delegation in $requiredConsoleDelegations) {
    if ($consoleFacade -notmatch [regex]::Escape($delegation)) {
        Add-Failure "Media evidence console facade delegation is missing: $delegation"
    }
}

$jsonFacade =
    Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot `
            "ReplayFoundry.DeveloperTools\Output\MediaEvidenceJsonExporter.cs")
if ($jsonFacade -notmatch
    'MediaEvidenceJsonDocumentFactory\.Serialize') {
    Add-Failure `
        "Media evidence JSON facade must delegate complete document construction."
}

$jsonDocumentFactory =
    Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot `
            "ReplayFoundry.DeveloperTools\Output\MediaEvidenceJsonDocumentFactory.cs")
$requiredJsonProjections = @(
    "CreateInspectionDocument",
    "CreateVisualTargetDocument",
    "CreateAudioCoverageDocument",
    "CreateManifestDocument",
    "CreateSceneSummaryDocument",
    "CreateVisualSignalSummaryDocument",
    "CreateTargetSummaryDocument",
    "CreateAudioSignalSummaryDocument"
)

foreach ($projection in $requiredJsonProjections) {
    if ($jsonDocumentFactory -notmatch "\b$([regex]::Escape($projection))\b") {
        Add-Failure "Media evidence JSON projection is missing: $projection"
    }
}

if ($failures.Count -gt 0) {
    Write-Error (
        "Media evidence architecture guard failed:`n- " +
        ($failures -join "`n- "))
    exit 1
}

Write-Host (
    "Media evidence architecture guard passed: explicit command, parser, " +
    "summary, console, and JSON boundaries with no partial fragments.")
