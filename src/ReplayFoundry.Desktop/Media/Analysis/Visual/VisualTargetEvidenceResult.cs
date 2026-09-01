using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;

namespace ReplayFoundry.Desktop.Media.Analysis.Visual;

public sealed class VisualTargetEvidenceResult
{
    private readonly ReadOnlyCollection<SceneBoundary>
        _sceneBoundaries;

    private readonly ReadOnlyCollection<BlackInterval>
        _blackIntervals;

    private readonly ReadOnlyCollection<FreezeInterval>
        _freezeIntervals;

    private readonly ReadOnlyCollection<VisualSignalSample>
        _signalSamples;

    private readonly ReadOnlyCollection<MediaEvidenceWarning>
        _warnings;

    public VisualTargetEvidenceResult(
        VisualEvidenceTarget target,
        IEnumerable<SceneBoundary> sceneBoundaries,
        IEnumerable<BlackInterval> blackIntervals,
        IEnumerable<FreezeInterval> freezeIntervals,
        IEnumerable<VisualSignalSample> signalSamples,
        VisualSignalCoverage signalCoverage,
        IEnumerable<MediaEvidenceWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sceneBoundaries);
        ArgumentNullException.ThrowIfNull(blackIntervals);
        ArgumentNullException.ThrowIfNull(freezeIntervals);
        ArgumentNullException.ThrowIfNull(signalSamples);
        ArgumentNullException.ThrowIfNull(signalCoverage);

        SceneBoundary[] sceneSnapshot =
            sceneBoundaries
                .OrderBy(
                    static item =>
                        item.Timestamp)
                .ToArray();

        BlackInterval[] blackSnapshot =
            blackIntervals
                .OrderBy(
                    static item =>
                        item.Start)
                .ThenBy(
                    static item =>
                        item.End)
                .ToArray();

        FreezeInterval[] freezeSnapshot =
            freezeIntervals
                .OrderBy(
                    static item =>
                        item.Start)
                .ThenBy(
                    static item =>
                        item.End)
                .ToArray();

        VisualSignalSample[] signalSnapshot =
            signalSamples
                .OrderBy(
                    static item =>
                        item.Timestamp)
                .ToArray();

        MediaEvidenceWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        RejectNullItems(
            sceneSnapshot,
            nameof(sceneBoundaries));
        RejectNullItems(
            blackSnapshot,
            nameof(blackIntervals));
        RejectNullItems(
            freezeSnapshot,
            nameof(freezeIntervals));
        RejectNullItems(
            signalSnapshot,
            nameof(signalSamples));
        RejectNullItems(
            warningSnapshot,
            nameof(warnings));

        if (sceneSnapshot.Any(
                item =>
                    item.Timestamp < target.Start ||
                    (target.Kind ==
                         VisualEvidenceTargetKind
                             .CompositionRegion
                         ? item.Timestamp >=
                           target.End
                         : item.Timestamp >
                           target.End)))
        {
            throw new ArgumentException(
                "Scene evidence must remain inside its visual target range.",
                nameof(sceneBoundaries));
        }

        if (blackSnapshot.Any(
                item =>
                    item.Start < target.Start ||
                    item.End > target.End))
        {
            throw new ArgumentException(
                "Black evidence must remain inside its visual target range.",
                nameof(blackIntervals));
        }

        if (freezeSnapshot.Any(
                item =>
                    item.Start < target.Start ||
                    item.End > target.End))
        {
            throw new ArgumentException(
                "Freeze evidence must remain inside its visual target range.",
                nameof(freezeIntervals));
        }

        if (!string.Equals(
                signalCoverage.TargetKey,
                target.TargetKey,
                StringComparison.Ordinal) ||
            signalCoverage.TargetStart !=
                target.Start ||
            signalCoverage.TargetEnd !=
                target.End)
        {
            throw new ArgumentException(
                "Visual signal coverage must describe the owning target.",
                nameof(signalCoverage));
        }

        if (signalSnapshot.Any(
                item =>
                    !string.Equals(
                        item.TargetKey,
                        target.TargetKey,
                        StringComparison.Ordinal) ||
                    item.Timestamp < target.Start ||
                    item.Timestamp >= target.End))
        {
            throw new ArgumentException(
                "Visual signal samples must identify and remain inside their owning target.",
                nameof(signalSamples));
        }

        if (signalSnapshot
            .GroupBy(
                static item =>
                    item.Timestamp)
            .Any(
                static group =>
                    group.Count() > 1))
        {
            throw new ArgumentException(
                "Visual signal samples cannot duplicate a target timestamp.",
                nameof(signalSamples));
        }

        if (!signalSnapshot
            .Select(
                static item =>
                    item.Timestamp)
            .SequenceEqual(
                signalCoverage.ActualSampleTimestamps))
        {
            throw new ArgumentException(
                "Visual signal samples must exactly match their coverage timestamps.",
                nameof(signalCoverage));
        }

        if (warningSnapshot.Any(
                warning =>
                    warning.TargetKey is not null &&
                    !string.Equals(
                        warning.TargetKey,
                        target.TargetKey,
                        StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Target-specific warnings must identify their owning visual target.",
                nameof(warnings));
        }

        Target = target;
        _sceneBoundaries =
            Array.AsReadOnly(
                sceneSnapshot);
        _blackIntervals =
            Array.AsReadOnly(
                blackSnapshot);
        _freezeIntervals =
            Array.AsReadOnly(
                freezeSnapshot);
        _signalSamples =
            Array.AsReadOnly(
                signalSnapshot);
        SignalCoverage = signalCoverage;
        _warnings =
            Array.AsReadOnly(
                warningSnapshot);
    }

    public VisualEvidenceTarget Target { get; }

    public IReadOnlyList<SceneBoundary> SceneBoundaries =>
        _sceneBoundaries;

    public IReadOnlyList<BlackInterval> BlackIntervals =>
        _blackIntervals;

    public IReadOnlyList<FreezeInterval> FreezeIntervals =>
        _freezeIntervals;

    public IReadOnlyList<VisualSignalSample> SignalSamples =>
        _signalSamples;

    public VisualSignalCoverage SignalCoverage { get; }

    public IReadOnlyList<MediaEvidenceWarning> Warnings =>
        _warnings;

    private static void RejectNullItems<TValue>(
        IReadOnlyList<TValue> values,
        string parameterName)
        where TValue : class
    {
        if (values.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Visual target evidence collections cannot contain null values.",
                parameterName);
        }
    }
}
