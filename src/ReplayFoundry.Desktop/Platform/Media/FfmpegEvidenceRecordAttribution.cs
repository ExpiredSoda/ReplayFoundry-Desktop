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
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceParseAccumulators;
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceValueParser;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegEvidenceRecordAttribution
{
    internal static bool TryGetRecordKind(
        FfmpegMetadataRecord record,
        ICollection<MediaEvidenceWarning> warnings,
        out string? recordKind)
    {
        if (!record.Tags.TryGetValue(
                FfmpegEvidenceCommandBuilder
                    .RecordKindMetadataKey,
                out recordKind) ||
            string.IsNullOrWhiteSpace(recordKind))
        {
            warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .MissingRecordKind,
                    "FFmpeg emitted evidence metadata without an explicit Replay Foundry record kind."));
            recordKind = null;
            return false;
        }

        recordKind =
            recordKind.Trim();

        return true;
    }

    internal static bool TryResolveTarget(
        FfmpegMetadataRecord record,
        IReadOnlyDictionary<string, TargetAccumulator> accumulators,
        ICollection<MediaEvidenceWarning> rootWarnings,
        out TargetAccumulator? accumulator)
    {
        if (!record.Tags.TryGetValue(
                FfmpegEvidenceCommandBuilder
                    .VisualTargetMetadataKey,
                out string? targetKey) ||
            string.IsNullOrWhiteSpace(targetKey))
        {
            rootWarnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .MissingVisualTargetKey,
                    "FFmpeg emitted visual evidence metadata without an internal target key."));
            accumulator = null;
            return false;
        }

        if (!accumulators.TryGetValue(
                targetKey,
                out accumulator))
        {
            rootWarnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .UnknownVisualTargetKey,
                    $"FFmpeg emitted evidence metadata for unknown target '{targetKey}'."));
            return false;
        }

        return true;
    }

    internal static bool TryResolveAudioStream(
        FfmpegMetadataRecord record,
        int expectedStreamIndex,
        ICollection<MediaEvidenceWarning> warnings)
    {
        if (!record.Tags.TryGetValue(
                FfmpegEvidenceCommandBuilder
                    .AudioStreamMetadataKey,
                out string? streamText) ||
            !int.TryParse(
                streamText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int streamIndex) ||
            streamIndex < 0)
        {
            warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .MissingAudioStreamIndex,
                    "FFmpeg emitted audio evidence metadata without a valid absolute stream index.",
                    expectedStreamIndex));
            return false;
        }

        if (streamIndex !=
            expectedStreamIndex)
        {
            warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .UnknownAudioStreamIndex,
                    $"FFmpeg emitted audio evidence for unexpected stream {streamIndex}.",
                    expectedStreamIndex));
            return false;
        }

        return true;
    }

    internal static void AddTargetIntervalEvent(
        FfmpegMetadataRecord record,
        string key,
        bool isStart,
        ICollection<IntervalEvent> events,
        TargetAccumulator accumulator)
    {
        if (!record.Tags.TryGetValue(
                key,
                out string? value))
        {
            return;
        }

        if (!TryParseSeconds(
                value,
                out TimeSpan timestamp))
        {
            accumulator.Warnings.Add(
                InvalidValueWarning(
                    key,
                    value,
                    accumulator.Target.TargetKey));
            return;
        }

        bool withinTarget =
            isStart
                ? timestamp >= accumulator.Target.Start &&
                  timestamp < accumulator.Target.End
                : timestamp > accumulator.Target.Start &&
                  timestamp <= accumulator.Target.End;

        if (!withinTarget)
        {
            accumulator.Warnings.Add(
                OutsideTargetWarning(
                    accumulator.Target,
                    $"{key} metadata at {timestamp}"));
            return;
        }

        if (events.Any(
                item =>
                    item.Timestamp == timestamp &&
                    item.IsStart == isStart))
        {
            accumulator.Warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .DuplicateTargetMetadata,
                    $"Duplicate '{key}' metadata at {timestamp} was ignored within target " +
                    $"'{accumulator.Target.TargetKey}'.",
                    targetKey:
                        accumulator.Target.TargetKey));
            return;
        }

        events.Add(
            new IntervalEvent(
                timestamp,
                isStart));
    }
}
