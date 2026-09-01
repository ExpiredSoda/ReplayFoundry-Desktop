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

internal static class FfmpegEvidenceResultParser
{
    public static FfmpegVisualEvidenceParseResult
        ParseVisualEvidence(
            string? sceneOutput,
            string? visualIntervalOutput,
            IReadOnlyList<VisualEvidenceTarget> targets,
            TimeSpan? visualSignalSampleInterval = null,
            int signalBitDepth =
                MediaSignalEvidencePolicy.VisualAnalysisBitDepth)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count == 0 ||
            targets.Any(static target => target is null))
        {
            throw new ArgumentException(
                "Visual evidence parsing requires known targets.",
                nameof(targets));
        }

        TimeSpan actualVisualSignalSampleInterval =
            visualSignalSampleInterval ??
            MediaEvidenceAnalysisOptions
                .DefaultVisualSignalSampleInterval;

        MediaEvidenceAnalysisOptions
            .ValidateSignalCadence(
                actualVisualSignalSampleInterval,
                nameof(visualSignalSampleInterval));

        if (signalBitDepth is < 8 or > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(signalBitDepth),
                signalBitDepth,
                "Signal normalization bit depth must be between 8 and 16.");
        }

        var accumulators =
            targets.ToDictionary(
                static target =>
                    target.TargetKey,
                static target =>
                    new FfmpegEvidenceParseAccumulators.TargetAccumulator(
                        target),
                StringComparer.Ordinal);

        var rootWarnings =
            new List<MediaEvidenceWarning>();

        FfmpegVisualEvidenceRecordParser.ParseSceneProcessRecords(
            FfmpegMetadataParser.Parse(
                sceneOutput),
            accumulators,
            rootWarnings,
            signalBitDepth);

        FfmpegVisualEvidenceRecordParser.ParseVisualIntervalProcessRecords(
            FfmpegMetadataParser.Parse(
                visualIntervalOutput),
            accumulators,
            rootWarnings);

        VisualTargetEvidenceResult[] results =
            targets
                .Select(
                    target =>
                        accumulators[
                            target.TargetKey]
                            .Build(
                                actualVisualSignalSampleInterval))
                .ToArray();

        return new FfmpegVisualEvidenceParseResult(
            results,
            rootWarnings);
    }

    public static FfmpegAudioEvidenceParseResult
        ParseAudioEvidence(
            string? output,
            AudioStreamInfo audioStream,
            TimeSpan sourceDuration,
            TimeSpan requestedWindowDuration)
    {
        ArgumentNullException.ThrowIfNull(audioStream);

        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "Audio parsing requires a positive source duration.");
        }

        FfmpegAudioWindowSpecification window =
            FfmpegEvidenceCommandBuilder
                .CreateAudioWindowSpecification(
                    audioStream,
                    requestedWindowDuration);

        var warnings =
            new List<MediaEvidenceWarning>();

        var silenceEvents =
            new List<FfmpegEvidenceParseAccumulators.IntervalEvent>();

        var candidateSamples =
            new List<AudioSignalSample>();

        foreach (FfmpegMetadataRecord record in
                 FfmpegMetadataParser.Parse(output))
        {
            if (!FfmpegEvidenceRecordAttribution.TryGetRecordKind(
                    record,
                    warnings,
                    out string? recordKind))
            {
                continue;
            }

            if (!FfmpegEvidenceRecordAttribution.TryResolveAudioStream(
                    record,
                    audioStream.Index,
                    warnings))
            {
                continue;
            }

            switch (recordKind)
            {
                case FfmpegEvidenceCommandBuilder
                    .SilenceRecordKind:
                    FfmpegAudioIntervalPairing.AddAudioIntervalEvent(
                        record,
                        "lavfi.silence_start",
                        isStart: true,
                        silenceEvents,
                        warnings,
                        audioStream.Index);
                    FfmpegAudioIntervalPairing.AddAudioIntervalEvent(
                        record,
                        "lavfi.silence_end",
                        isStart: false,
                        silenceEvents,
                        warnings,
                        audioStream.Index);
                    break;

                case FfmpegEvidenceCommandBuilder
                    .AudioSignalRecordKind:
                    FfmpegAudioSignalRecordParser.ParseAudioSignalRecord(
                        record,
                        audioStream.Index,
                        window.SampleRate,
                        sourceDuration,
                        candidateSamples,
                        warnings);
                    break;

                default:
                    warnings.Add(
                        FfmpegEvidenceValueParser.UnknownRecordKindWarning(
                            recordKind!,
                            streamIndex:
                                audioStream.Index));
                    break;
            }
        }

        IReadOnlyList<SilenceInterval> silenceIntervals =
            FfmpegAudioIntervalPairing.PairAudioIntervals(
                silenceEvents,
                audioStream.Index,
                sourceDuration,
                warnings);

        IReadOnlyList<AudioSignalSample> signalSamples =
            FfmpegAudioSignalRecordParser.NormalizeAudioSignalWindows(
                candidateSamples,
                audioStream.Index,
                warnings);

        AudioSignalCoverage coverage =
            FfmpegAudioSignalRecordParser.CreateAudioCoverage(
                audioStream.Index,
                sourceDuration,
                requestedWindowDuration,
                window,
                signalSamples);

        return new FfmpegAudioEvidenceParseResult(
            silenceIntervals,
            signalSamples,
            coverage,
            warnings);
    }
}
