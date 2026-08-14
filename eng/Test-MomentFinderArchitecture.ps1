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
    "ReplayFoundry.Desktop\Media\Moments\MomentScoreCalculator.cs" = @(
        "MomentScoreCalculator", 120)
    "ReplayFoundry.Desktop\Media\Moments\MomentScoreMeasurementCalculator.cs" = @(
        "MomentScoreMeasurementCalculator", 380)
    "ReplayFoundry.Desktop\Media\Moments\MomentScoreSupport.cs" = @(
        "MomentScoreSupport", 400)
    "ReplayFoundry.Desktop\Media\Moments\MomentSignalScoreComponentBuilder.cs" = @(
        "MomentSignalScoreComponentBuilder", 220)
    "ReplayFoundry.Desktop\Media\Moments\MomentEpisodeScoreComponentBuilder.cs" = @(
        "MomentEpisodeScoreComponentBuilder", 180)
    "ReplayFoundry.Desktop\Media\Moments\DeterministicMediaMomentFinder.cs" = @(
        "DeterministicMediaMomentFinder", 240)
    "ReplayFoundry.Desktop\Media\Moments\DeterministicMomentManifestBuilder.cs" = @(
        "DeterministicMomentManifestBuilder", 110)
    "ReplayFoundry.Desktop\Media\Moments\DeterministicMomentSelection.cs" = @(
        "DeterministicMomentSelection", 280)
    "ReplayFoundry.Desktop\Media\Moments\DeterministicMomentWarnings.cs" = @(
        "DeterministicMomentWarnings", 180)
}

foreach ($entry in $boundaries.GetEnumerator()) {
    $fullPath = Join-Path $repositoryRoot $entry.Key
    if (-not (Test-Path -LiteralPath $fullPath)) {
        Add-Failure "Moment scoring boundary is missing: $($entry.Key)"
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
            "Moment scoring boundaries must not use partial types: " +
            "$($entry.Key)")
    }

    if ($text -notmatch
        "\b(class|record|struct)\s+$([regex]::Escape($expectedType))\b") {
        Add-Failure (
            "$($entry.Key) does not declare $expectedType.")
    }
}

$removedFragments = @(
    "ReplayFoundry.Desktop\Media\Moments\MomentScoreMeasurements.cs",
    "ReplayFoundry.Desktop\Media\Moments\MomentScoreCalculationHelpers.cs"
)

foreach ($relativePath in $removedFragments) {
    if (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath)) {
        Add-Failure "Replaced Moment scoring fragment remains: $relativePath"
    }
}

$facade =
    Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot `
            "ReplayFoundry.Desktop\Media\Moments\MomentScoreCalculator.cs")
$requiredDelegations = @(
    "MomentScoreMeasurementCalculator.MeasureSignals",
    "MomentScoreMeasurementCalculator.MeasureIntegrity",
    "MomentScoreMeasurementCalculator.MeasureEpisode",
    "MomentSignalScoreComponentBuilder.AddComponents",
    "MomentEpisodeScoreComponentBuilder.AddComponents"
)

foreach ($delegation in $requiredDelegations) {
    if ($facade -notmatch [regex]::Escape($delegation)) {
        Add-Failure "Moment scoring facade delegation is missing: $delegation"
    }
}

$finderFacade =
    Get-Content -Raw -LiteralPath (
        Join-Path $repositoryRoot `
            "ReplayFoundry.Desktop\Media\Moments\DeterministicMediaMomentFinder.cs")
$requiredFinderDelegations = @(
    "DeterministicMomentWarnings.BuildInputWarnings",
    "DeterministicMomentWarnings.GetInitialDisposition",
    "DeterministicMomentSelection.Select",
    "DeterministicMomentManifestBuilder.Build"
)

foreach ($delegation in $requiredFinderDelegations) {
    if ($finderFacade -notmatch [regex]::Escape($delegation)) {
        Add-Failure "Deterministic Moment Finder facade delegation is missing: $delegation"
    }
}

if ($failures.Count -gt 0) {
    Write-Error (
        "Moment Finder architecture guard failed:`n- " +
        ($failures -join "`n- "))
    exit 1
}

Write-Host (
    "Moment Finder architecture guard passed: explicit finder and scoring " +
    "boundaries, no partial fragments, and bounded files inspected.")
