using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegVisualEvidenceParseResult
{
    private readonly ReadOnlyCollection<VisualTargetEvidenceResult>
        _targets;

    private readonly ReadOnlyCollection<MediaEvidenceWarning>
        _rootWarnings;

    public FfmpegVisualEvidenceParseResult(
        IEnumerable<VisualTargetEvidenceResult> targets,
        IEnumerable<MediaEvidenceWarning> rootWarnings)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(rootWarnings);

        VisualTargetEvidenceResult[] targetSnapshot =
            targets.ToArray();

        MediaEvidenceWarning[] warningSnapshot =
            rootWarnings.ToArray();

        if (targetSnapshot.Any(static item => item is null) ||
            warningSnapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Parsed visual evidence collections cannot contain null values.");
        }

        _targets =
            Array.AsReadOnly(
                targetSnapshot);
        _rootWarnings =
            Array.AsReadOnly(
                warningSnapshot);
    }

    public IReadOnlyList<VisualTargetEvidenceResult> Targets =>
        _targets;

    public IReadOnlyList<MediaEvidenceWarning> RootWarnings =>
        _rootWarnings;
}

internal sealed class FfmpegAudioEvidenceParseResult
{
    private readonly ReadOnlyCollection<SilenceInterval>
        _silenceIntervals;

    private readonly ReadOnlyCollection<AudioSignalSample>
        _signalSamples;

    private readonly ReadOnlyCollection<MediaEvidenceWarning>
        _warnings;

    public FfmpegAudioEvidenceParseResult(
        IEnumerable<SilenceInterval> silenceIntervals,
        IEnumerable<AudioSignalSample> signalSamples,
        AudioSignalCoverage signalCoverage,
        IEnumerable<MediaEvidenceWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(silenceIntervals);
        ArgumentNullException.ThrowIfNull(signalSamples);
        ArgumentNullException.ThrowIfNull(signalCoverage);
        ArgumentNullException.ThrowIfNull(warnings);

        SilenceInterval[] silenceSnapshot =
            silenceIntervals.ToArray();

        AudioSignalSample[] signalSnapshot =
            signalSamples.ToArray();

        MediaEvidenceWarning[] warningSnapshot =
            warnings.ToArray();

        if (silenceSnapshot.Any(static item => item is null) ||
            signalSnapshot.Any(static item => item is null) ||
            warningSnapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Parsed audio evidence collections cannot contain null values.");
        }

        _silenceIntervals =
            Array.AsReadOnly(
                silenceSnapshot);
        _signalSamples =
            Array.AsReadOnly(
                signalSnapshot);
        SignalCoverage = signalCoverage;
        _warnings =
            Array.AsReadOnly(
                warningSnapshot);
    }

    public IReadOnlyList<SilenceInterval> SilenceIntervals =>
        _silenceIntervals;

    public IReadOnlyList<AudioSignalSample> SignalSamples =>
        _signalSamples;

    public AudioSignalCoverage SignalCoverage { get; }

    public IReadOnlyList<MediaEvidenceWarning> Warnings =>
        _warnings;
}
