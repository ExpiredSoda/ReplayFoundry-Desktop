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
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceMetadataKeys;
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceParseAccumulators;
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceRecordAttribution;
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceValueParser;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegVisualEvidenceRecordParser
{
    internal static void ParseSceneProcessRecords(
        IReadOnlyList<FfmpegMetadataRecord> records,
        IReadOnlyDictionary<string, TargetAccumulator> accumulators,
        ICollection<MediaEvidenceWarning> rootWarnings,
        int signalBitDepth)
    {
        foreach (FfmpegMetadataRecord record in records)
        {
            if (!TryGetRecordKind(
                    record,
                    rootWarnings,
                    out string? recordKind))
            {
                continue;
            }

            switch (recordKind)
            {
                case FfmpegEvidenceCommandBuilder
                    .SceneRecordKind:
                    ParseSceneRecord(
                        record,
                        accumulators,
                        rootWarnings);
                    break;

                case FfmpegEvidenceCommandBuilder
                    .VisualSignalRecordKind:
                    ParseVisualSignalRecord(
                        record,
                        accumulators,
                        rootWarnings,
                        signalBitDepth);
                    break;

                default:
                    rootWarnings.Add(
                        UnknownRecordKindWarning(
                            recordKind!));
                    break;
            }
        }
    }

    internal static void ParseVisualIntervalProcessRecords(
        IReadOnlyList<FfmpegMetadataRecord> records,
        IReadOnlyDictionary<string, TargetAccumulator> accumulators,
        ICollection<MediaEvidenceWarning> rootWarnings)
    {
        foreach (FfmpegMetadataRecord record in records)
        {
            if (!TryGetRecordKind(
                    record,
                    rootWarnings,
                    out string? recordKind))
            {
                continue;
            }

            if (recordKind is not
                    FfmpegEvidenceCommandBuilder
                        .BlackRecordKind and not
                    FfmpegEvidenceCommandBuilder
                        .FreezeRecordKind)
            {
                rootWarnings.Add(
                    UnknownRecordKindWarning(
                        recordKind!));
                continue;
            }

            if (!TryResolveTarget(
                    record,
                    accumulators,
                    rootWarnings,
                    out TargetAccumulator? accumulator))
            {
                continue;
            }

            if (recordKind ==
                FfmpegEvidenceCommandBuilder
                    .BlackRecordKind)
            {
                AddTargetIntervalEvent(
                    record,
                    BlackStartKey,
                    isStart: true,
                    accumulator!.BlackEvents,
                    accumulator);
                AddTargetIntervalEvent(
                    record,
                    BlackEndKey,
                    isStart: false,
                    accumulator.BlackEvents,
                    accumulator);
            }
            else
            {
                AddTargetIntervalEvent(
                    record,
                    FreezeStartKey,
                    isStart: true,
                    accumulator!.FreezeEvents,
                    accumulator);
                AddTargetIntervalEvent(
                    record,
                    FreezeEndKey,
                    isStart: false,
                    accumulator.FreezeEvents,
                    accumulator);
            }
        }
    }

    internal static void ParseSceneRecord(
        FfmpegMetadataRecord record,
        IReadOnlyDictionary<string, TargetAccumulator> accumulators,
        ICollection<MediaEvidenceWarning> rootWarnings)
    {
        if (!TryResolveTarget(
                record,
                accumulators,
                rootWarnings,
                out TargetAccumulator? accumulator))
        {
            return;
        }

        if (!record.Tags.TryGetValue(
                SceneTimeKey,
                out string? timeText) ||
            !TryParseSeconds(
                timeText,
                out TimeSpan timestamp))
        {
            accumulator!.Warnings.Add(
                InvalidValueWarning(
                    SceneTimeKey,
                    timeText ??
                    "<missing>",
                    accumulator.Target.TargetKey));
            return;
        }

        if (!IsTimestampInTarget(
                timestamp,
                accumulator!.Target))
        {
            accumulator.Warnings.Add(
                OutsideTargetWarning(
                    accumulator.Target,
                    $"Scene boundary at {timestamp}"));
            return;
        }

        double? score =
            TryParseFiniteDouble(
                record.Tags,
                SceneScoreKey,
                minimum: 0,
                maximum: 100,
                accumulator);

        double? mafd =
            TryParseFiniteDouble(
                record.Tags,
                SceneMafdKey,
                minimum: 0,
                maximum: null,
                accumulator);

        var boundary =
            new SceneBoundary(
                timestamp,
                score,
                mafd);

        if (accumulator.Scenes.TryGetValue(
                timestamp,
                out SceneBoundary? existing))
        {
            accumulator.Warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .DuplicateTargetMetadata,
                    $"Duplicate scene metadata at {timestamp} was consolidated within target " +
                    $"'{accumulator.Target.TargetKey}'.",
                    targetKey:
                        accumulator.Target.TargetKey));

            accumulator.Scenes[timestamp] =
                new SceneBoundary(
                    timestamp,
                    PreferHigher(
                        existing.ScorePercent,
                        boundary.ScorePercent),
                    PreferHigher(
                        existing.MeanAbsoluteFrameDifference,
                        boundary.MeanAbsoluteFrameDifference));
            return;
        }

        accumulator.Scenes.Add(
            timestamp,
            boundary);
    }

    internal static void ParseVisualSignalRecord(
        FfmpegMetadataRecord record,
        IReadOnlyDictionary<string, TargetAccumulator> accumulators,
        ICollection<MediaEvidenceWarning> rootWarnings,
        int signalBitDepth)
    {
        if (!TryResolveTarget(
                record,
                accumulators,
                rootWarnings,
                out TargetAccumulator? accumulator))
        {
            return;
        }

        if (record.Timestamp is not TimeSpan timestamp)
        {
            accumulator!.Warnings.Add(
                InvalidValueWarning(
                    "frame.pts_time",
                    "<missing>",
                    accumulator.Target.TargetKey));
            return;
        }

        if (!IsTimestampInTarget(
                timestamp,
                accumulator!.Target))
        {
            accumulator.Warnings.Add(
                OutsideTargetWarning(
                    accumulator.Target,
                    $"Visual signal sample at {timestamp}"));
            return;
        }

        double lumaScale =
            Math.Pow(
                2,
                signalBitDepth) -
            1;

        double saturationScale =
            Math.Sqrt(2) *
            Math.Pow(
                2,
                signalBitDepth -
                1);

        if (!TryParseNormalizedSignal(
                record,
                VisualMeanLumaKey,
                lumaScale,
                accumulator,
                out double meanLuma) ||
            !TryParseNormalizedSignal(
                record,
                VisualLowLumaKey,
                lumaScale,
                accumulator,
                out double lowLuma) ||
            !TryParseNormalizedSignal(
                record,
                VisualHighLumaKey,
                lumaScale,
                accumulator,
                out double highLuma) ||
            !TryParseNormalizedSignal(
                record,
                VisualSaturationKey,
                saturationScale,
                accumulator,
                out double meanSaturation))
        {
            return;
        }

        double? activity = null;

        if (record.Tags.ContainsKey(
                VisualActivityKey))
        {
            if (!TryParseNormalizedSignal(
                    record,
                    VisualActivityKey,
                    lumaScale,
                    accumulator,
                    out double parsedActivity))
            {
                return;
            }

            activity = parsedActivity;
        }

        VisualSignalSample sample;

        try
        {
            sample =
                new VisualSignalSample(
                    accumulator.Target.TargetKey,
                    timestamp,
                    meanLuma,
                    lowLuma,
                    highLuma,
                    meanSaturation,
                    activity,
                    signalBitDepth);
        }
        catch (ArgumentException exception)
        {
            accumulator.Warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .InvalidMetadataValue,
                    $"Visual signal metadata at {timestamp} is invalid: {exception.Message}",
                    targetKey:
                        accumulator.Target.TargetKey));
            return;
        }

        if (!accumulator.Signals.TryAdd(
                timestamp,
                sample))
        {
            accumulator.Warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .DuplicateVisualSignalSample,
                    $"Duplicate visual signal metadata at {timestamp} was ignored.",
                    targetKey:
                        accumulator.Target.TargetKey));
        }
    }

    internal static bool TryParseNormalizedSignal(
        FfmpegMetadataRecord record,
        string key,
        double scale,
        TargetAccumulator accumulator,
        out double normalized)
    {
        normalized = default;

        if (!record.Tags.TryGetValue(
                key,
                out string? value) ||
            !double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double raw) ||
            !double.IsFinite(raw) ||
            raw < 0 ||
            raw > scale)
        {
            accumulator.Warnings.Add(
                InvalidValueWarning(
                    key,
                    value ??
                    "<missing>",
                    accumulator.Target.TargetKey));
            return false;
        }

        normalized =
            raw /
            scale;

        return true;
    }
}
