using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Analysis;

public sealed class MediaEvidenceAnalysisManifest
{
    private readonly ReadOnlyCollection<CompositionRegionRole>
        _requestedIncludedRegionRoles;

    private readonly ReadOnlyCollection<VisualEvidenceTarget>
        _visualTargets;

    private readonly ReadOnlyCollection<SkippedCompositionRegion>
        _skippedCompositionRegions;

    private readonly ReadOnlyCollection<AnalysisPassTiming>
        _passTimings;

    private readonly ReadOnlyCollection<VisualSignalCoverage>
        _visualSignalCoverages;

    private readonly ReadOnlyCollection<AudioSignalCoverage>
        _audioSignalCoverages;

    public MediaEvidenceAnalysisManifest(
        string analyzerName,
        string analyzerVersion,
        string toolName,
        string toolVersion,
        string toolPath,
        DateTimeOffset analyzedAtUtc,
        AnalysisCoverage coverage,
        MediaEvidenceAnalysisOptions options,
        string? compositionSchemaVersion,
        string? compositionCoordinateSpaceVersion,
        CompositionPlanOrigin? compositionPlanOrigin,
        IEnumerable<CompositionRegionRole> requestedIncludedRegionRoles,
        IEnumerable<VisualEvidenceTarget> visualTargets,
        IEnumerable<SkippedCompositionRegion> skippedCompositionRegions,
        int effectiveDisplayWidth,
        int effectiveDisplayHeight,
        string signalSchemaVersion,
        IEnumerable<VisualSignalCoverage> visualSignalCoverages,
        IEnumerable<AudioSignalCoverage> audioSignalCoverages,
        int visualPassCount,
        int audioPassCount,
        IEnumerable<AnalysisPassTiming> passTimings,
        TimeSpan totalElapsed)
    {
        ValidateText(
            analyzerName,
            nameof(analyzerName));
        ValidateText(
            analyzerVersion,
            nameof(analyzerVersion));
        ValidateText(
            toolName,
            nameof(toolName));
        ValidateText(
            toolVersion,
            nameof(toolVersion));
        ValidateText(
            toolPath,
            nameof(toolPath));

        if (!Path.IsPathFullyQualified(toolPath))
        {
            throw new ArgumentException(
                "The analysis tool path must be fully qualified.",
                nameof(toolPath));
        }

        if (analyzedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The analysis timestamp must use UTC.",
                nameof(analyzedAtUtc));
        }

        if (!Enum.IsDefined(coverage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(coverage),
                coverage,
                "The analysis coverage is not defined.");
        }

        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(
            requestedIncludedRegionRoles);
        ArgumentNullException.ThrowIfNull(visualTargets);
        ArgumentNullException.ThrowIfNull(
            skippedCompositionRegions);
        ArgumentNullException.ThrowIfNull(passTimings);
        ArgumentNullException.ThrowIfNull(visualSignalCoverages);
        ArgumentNullException.ThrowIfNull(audioSignalCoverages);

        if (string.IsNullOrWhiteSpace(
                signalSchemaVersion))
        {
            throw new ArgumentException(
                "The analysis manifest requires a signal schema version.",
                nameof(signalSchemaVersion));
        }

        CompositionRegionRole[] roleSnapshot =
            requestedIncludedRegionRoles.ToArray();

        VisualEvidenceTarget[] targetSnapshot =
            visualTargets.ToArray();

        SkippedCompositionRegion[] skippedSnapshot =
            skippedCompositionRegions.ToArray();

        AnalysisPassTiming[] timingSnapshot =
            passTimings.ToArray();

        VisualSignalCoverage[] visualCoverageSnapshot =
            visualSignalCoverages
                .OrderBy(
                    static item =>
                        item.TargetStart)
                .ThenBy(
                    static item =>
                        item.TargetKey,
                    StringComparer.Ordinal)
                .ToArray();

        AudioSignalCoverage[] audioCoverageSnapshot =
            audioSignalCoverages
                .OrderBy(
                    static item =>
                        item.AudioStreamIndex)
                .ToArray();

        if (targetSnapshot.Any(static item => item is null) ||
            skippedSnapshot.Any(static item => item is null) ||
            timingSnapshot.Any(static item => item is null) ||
            visualCoverageSnapshot.Any(static item => item is null) ||
            audioCoverageSnapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Analysis manifest collections cannot contain null values.");
        }

        if (roleSnapshot.Any(
                static role =>
                    !Enum.IsDefined(role)) ||
            roleSnapshot.Distinct().Count() !=
            roleSnapshot.Length)
        {
            throw new ArgumentException(
                "Requested region roles must be unique defined values.",
                nameof(requestedIncludedRegionRoles));
        }

        bool hasComposition =
            compositionSchemaVersion is not null ||
            compositionCoordinateSpaceVersion is not null ||
            compositionPlanOrigin is not null;

        if (hasComposition &&
            (string.IsNullOrWhiteSpace(
                 compositionSchemaVersion) ||
             string.IsNullOrWhiteSpace(
                 compositionCoordinateSpaceVersion) ||
             compositionPlanOrigin is null))
        {
            throw new ArgumentException(
                "Composition provenance must be complete when supplied.");
        }

        if (!hasComposition &&
            (roleSnapshot.Length != 0 ||
             skippedSnapshot.Length != 0 ||
             targetSnapshot.Any(
                 static target =>
                     target.Kind ==
                     VisualEvidenceTargetKind
                         .CompositionRegion)))
        {
            throw new ArgumentException(
                "Full-frame-only analysis cannot claim composition provenance.");
        }

        if (effectiveDisplayWidth <= 0 ||
            effectiveDisplayHeight <= 0 ||
            (effectiveDisplayWidth & 1) != 0 ||
            (effectiveDisplayHeight & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveDisplayWidth),
                "Manifest effective-display dimensions must be positive and even.");
        }

        if (visualPassCount != 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(visualPassCount),
                visualPassCount,
                "Deterministic evidence analysis requires exactly two visual passes.");
        }

        if (audioPassCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioPassCount),
                audioPassCount,
                "Audio pass count cannot be negative.");
        }

        if (timingSnapshot.Length !=
            visualPassCount +
            audioPassCount)
        {
            throw new ArgumentException(
                "Pass timing count must match the declared visual and audio pass counts.",
                nameof(passTimings));
        }

        if (targetSnapshot.Count(
                static target =>
                    target.Kind ==
                    VisualEvidenceTargetKind.FullFrame) != 1 ||
            targetSnapshot
                .GroupBy(
                    static target =>
                        target.TargetKey,
                    StringComparer.Ordinal)
                .Any(
                    static group =>
                        group.Count() > 1))
        {
            throw new ArgumentException(
                "Manifest target mapping requires one full-frame target and unique keys.",
                nameof(visualTargets));
        }

        string[] targetKeys =
            targetSnapshot
                .Select(
                    static target =>
                        target.TargetKey)
                .OrderBy(
                    static key =>
                        key,
                    StringComparer.Ordinal)
                .ToArray();

        string[] coverageKeys =
            visualCoverageSnapshot
                .Select(
                    static coverage =>
                        coverage.TargetKey)
                .OrderBy(
                    static key =>
                        key,
                    StringComparer.Ordinal)
                .ToArray();

        if (!targetKeys.SequenceEqual(
                coverageKeys,
                StringComparer.Ordinal) ||
            visualCoverageSnapshot.Any(
                coverage =>
                    coverage.RequestedSampleInterval !=
                    options.VisualSignalSampleInterval))
        {
            throw new ArgumentException(
                "Manifest visual signal coverage must match every target and the requested cadence.",
                nameof(visualSignalCoverages));
        }

        if (audioCoverageSnapshot.Length !=
                audioPassCount ||
            audioCoverageSnapshot
                .GroupBy(
                    static coverage =>
                        coverage.AudioStreamIndex)
                .Any(
                    static group =>
                        group.Count() > 1) ||
            audioCoverageSnapshot.Any(
                coverage =>
                    coverage.RequestedWindowDuration !=
                    options.AudioSignalWindowDuration))
        {
            throw new ArgumentException(
                "Manifest audio signal coverage must match the audio pass count and requested cadence.",
                nameof(audioSignalCoverages));
        }

        if (totalElapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalElapsed),
                totalElapsed,
                "Total analysis duration cannot be negative.");
        }

        AnalyzerName = analyzerName.Trim();
        AnalyzerVersion = analyzerVersion.Trim();
        ToolName = toolName.Trim();
        ToolVersion = toolVersion.Trim();
        ToolPath = toolPath;
        AnalyzedAtUtc = analyzedAtUtc;
        Coverage = coverage;
        Options = options;
        CompositionSchemaVersion =
            compositionSchemaVersion?.Trim();
        CompositionCoordinateSpaceVersion =
            compositionCoordinateSpaceVersion?.Trim();
        CompositionPlanOrigin =
            compositionPlanOrigin;
        _requestedIncludedRegionRoles =
            Array.AsReadOnly(
                roleSnapshot);
        _visualTargets =
            Array.AsReadOnly(
                targetSnapshot);
        _skippedCompositionRegions =
            Array.AsReadOnly(
                skippedSnapshot);
        EffectiveDisplayWidth =
            effectiveDisplayWidth;
        EffectiveDisplayHeight =
            effectiveDisplayHeight;
        SignalSchemaVersion =
            signalSchemaVersion.Trim();
        _visualSignalCoverages =
            Array.AsReadOnly(
                visualCoverageSnapshot);
        _audioSignalCoverages =
            Array.AsReadOnly(
                audioCoverageSnapshot);
        VisualSignalSampleCount =
            visualCoverageSnapshot.Sum(
                static coverage =>
                    coverage.ActualSampleCount);
        AudioSignalSampleCount =
            audioCoverageSnapshot.Sum(
                static coverage =>
                    coverage.ActualWindowCount);
        VisualPassCount = visualPassCount;
        AudioPassCount = audioPassCount;
        _passTimings =
            Array.AsReadOnly(
                timingSnapshot);
        TotalElapsed = totalElapsed;
    }

    public string AnalyzerName { get; }

    public string AnalyzerVersion { get; }

    public string ToolName { get; }

    public string ToolVersion { get; }

    public string ToolPath { get; }

    public DateTimeOffset AnalyzedAtUtc { get; }

    public AnalysisCoverage Coverage { get; }

    public MediaEvidenceAnalysisOptions Options { get; }

    public bool CompositionPlanSupplied =>
        CompositionSchemaVersion is not null;

    public string? CompositionSchemaVersion { get; }

    public string? CompositionCoordinateSpaceVersion { get; }

    public CompositionPlanOrigin? CompositionPlanOrigin { get; }

    public IReadOnlyList<CompositionRegionRole>
        RequestedIncludedRegionRoles =>
        _requestedIncludedRegionRoles;

    public IReadOnlyList<VisualEvidenceTarget> VisualTargets =>
        _visualTargets;

    public int IncludedRegionTargetCount =>
        _visualTargets.Count -
        1;

    public IReadOnlyList<SkippedCompositionRegion>
        SkippedCompositionRegions =>
        _skippedCompositionRegions;

    public int EffectiveDisplayWidth { get; }

    public int EffectiveDisplayHeight { get; }

    public string SignalSchemaVersion { get; }

    public IReadOnlyList<VisualSignalCoverage>
        VisualSignalCoverages =>
        _visualSignalCoverages;

    public IReadOnlyList<AudioSignalCoverage>
        AudioSignalCoverages =>
        _audioSignalCoverages;

    public int VisualSignalSampleCount { get; }

    public int AudioSignalSampleCount { get; }

    public int VisualPassCount { get; }

    public int AudioPassCount { get; }

    public IReadOnlyList<AnalysisPassTiming> PassTimings =>
        _passTimings;

    public TimeSpan TotalElapsed { get; }

    private static void ValidateText(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Analysis manifest text values cannot be blank.",
                parameterName);
        }
    }
}
