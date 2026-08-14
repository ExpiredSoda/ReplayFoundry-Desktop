using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

internal static class AudioStreamSignalSummaryBuilder
{
    internal static AudioStreamSignalSummary Build(
        int audioStreamIndex,
        IEnumerable<AudioSignalSample> samples,
        AudioSignalCoverage? coverage,
        MediaEvidenceSummaryOptions? options = null)
    {
        if (audioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioStreamIndex),
                audioStreamIndex,
                "Audio stream index cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(samples);

        options ??=
            MediaEvidenceSummaryOptions.CreateDefault();

        AudioSignalSample[] snapshot =
            samples
                .OrderBy(
                    static sample =>
                        sample.Start)
                .ToArray();

        if (snapshot.Any(
                sample =>
                    sample is null ||
                    sample.AudioStreamIndex !=
                    audioStreamIndex))
        {
            throw new ArgumentException(
                "Audio signal samples must belong to the requested stream.",
                nameof(samples));
        }

        if (coverage is null)
        {
            if (snapshot.Length != 0)
            {
                throw new ArgumentException(
                    "Audio signal samples require matching coverage.",
                    nameof(coverage));
            }

            return new AudioStreamSignalSummary(
                audioStreamIndex,
                options.SignalSummaryPolicyVersion,
                sampleCount: 0,
                totalCoveredDuration: TimeSpan.Zero,
                meanRmsLevelDbfs: null,
                medianRmsLevelDbfs: null,
                rmsLevelP10Dbfs: null,
                rmsLevelP90Dbfs: null,
                maximumPeakLevelDbfs: null,
                digitalSilenceWindowCount: 0,
                digitalSilenceWindowPercentage: null,
                maximumInterWindowGap: TimeSpan.Zero);
        }

        if (coverage.AudioStreamIndex !=
                audioStreamIndex ||
            coverage.ActualWindowCount !=
                snapshot.Length)
        {
            throw new ArgumentException(
                "Audio signal coverage must match the requested stream and samples.",
                nameof(coverage));
        }
        double[] rmsValues =
            snapshot
                .Where(
                    static sample =>
                        !sample.IsDigitalSilence &&
                        sample.RmsLevelDbfs is not null)
                .Select(
                    static sample =>
                        sample.RmsLevelDbfs!.Value)
                .OrderBy(
                    static value =>
                        value)
                .ToArray();

        double[] peakValues =
            snapshot
                .Where(
                    static sample =>
                        !sample.IsDigitalSilence &&
                        sample.PeakLevelDbfs is not null)
                .Select(
                    static sample =>
                        sample.PeakLevelDbfs!.Value)
                .ToArray();

        int digitalSilenceCount =
            snapshot.Count(
                static sample =>
                    sample.IsDigitalSilence);

        return new AudioStreamSignalSummary(
            audioStreamIndex,
            options.SignalSummaryPolicyVersion,
            snapshot.Length,
            coverage.TotalCoveredDuration,
            rmsValues.Length == 0
                ? null
                : rmsValues.Average(),
            rmsValues.Length == 0
                ? null
                : MediaEvidenceSummaryMath.CalculateMedian(rmsValues),
            rmsValues.Length == 0
                ? null
                : MediaEvidenceSummaryMath.CalculatePercentile(
                    rmsValues,
                    0.10),
            rmsValues.Length == 0
                ? null
                : MediaEvidenceSummaryMath.CalculatePercentile(
                    rmsValues,
                    0.90),
            peakValues.Length == 0
                ? null
                : peakValues.Max(),
            digitalSilenceCount,
            snapshot.Length == 0
                ? null
                : digitalSilenceCount /
                  (double)snapshot.Length *
                  100,
            coverage.MaximumObservedGap);
    }
}
