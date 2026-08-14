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

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegEvidenceValueParser
{
    internal static bool IsTimestampInTarget(
        TimeSpan timestamp,
        VisualEvidenceTarget target)
    {
        return timestamp >= target.Start &&
               timestamp < target.End;
    }

    internal static double? TryParseFiniteDouble(
        IReadOnlyDictionary<string, string> tags,
        string key,
        double minimum,
        double? maximum,
        TargetAccumulator accumulator)
    {
        if (!tags.TryGetValue(
                key,
                out string? value))
        {
            return null;
        }

        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed) ||
            !double.IsFinite(parsed) ||
            parsed < minimum ||
            maximum is double actualMaximum &&
            parsed > actualMaximum)
        {
            accumulator.Warnings.Add(
                InvalidValueWarning(
                    key,
                    value,
                    accumulator.Target.TargetKey));
            return null;
        }

        return parsed;
    }

    internal static bool TryParseSeconds(
        string value,
        out TimeSpan timestamp)
    {
        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double seconds) &&
            double.IsFinite(seconds) &&
            seconds >= 0 &&
            seconds <=
            TimeSpan.MaxValue.TotalSeconds)
        {
            timestamp =
                TimeSpan.FromSeconds(
                    seconds);
            return true;
        }

        timestamp = default;
        return false;
    }

    internal static bool TryParsePositiveInteger(
        string value,
        out int result)
    {
        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed) &&
            double.IsFinite(parsed) &&
            parsed >= 1 &&
            parsed <= int.MaxValue &&
            parsed ==
            Math.Truncate(parsed))
        {
            result =
                checked((int)parsed);
            return true;
        }

        result = default;
        return false;
    }

    internal static ParsedDbfs ParseDbfs(
        FfmpegMetadataRecord record,
        string key)
    {
        if (!record.Tags.TryGetValue(
                key,
                out string? value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return new ParsedDbfs(
                DbfsValueKind.Missing,
                null);
        }

        if (value.Equals(
                "-inf",
                StringComparison.OrdinalIgnoreCase) ||
            value.Equals(
                "-infinity",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedDbfs(
                DbfsValueKind.NegativeInfinity,
                null);
        }

        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed) &&
            double.IsFinite(parsed) &&
            parsed <= 0)
        {
            return new ParsedDbfs(
                DbfsValueKind.Finite,
                parsed);
        }

        return new ParsedDbfs(
            DbfsValueKind.Invalid,
            null);
    }

    internal static MediaEvidenceWarning InvalidValueWarning(
        string key,
        string value,
        string? targetKey = null,
        int? streamIndex = null)
    {
        return new MediaEvidenceWarning(
            MediaEvidenceWarningCode
                .InvalidMetadataValue,
            $"FFmpeg metadata '{key}' contained the invalid value '{value}'.",
            streamIndex,
            targetKey);
    }

    internal static MediaEvidenceWarning UnknownRecordKindWarning(
        string recordKind,
        int? streamIndex = null)
    {
        return new MediaEvidenceWarning(
            MediaEvidenceWarningCode
                .UnknownRecordKind,
            $"FFmpeg emitted the unsupported evidence record kind '{recordKind}'.",
            streamIndex);
    }

    internal static MediaEvidenceWarning OutsideTargetWarning(
        VisualEvidenceTarget target,
        string evidence)
    {
        return TargetWarning(
            target,
            MediaEvidenceWarningCode
                .EvidenceOutsideTargetInterval,
            $"{evidence} falls outside target range " +
            $"{target.Start} through {target.End}.");
    }

    internal static MediaEvidenceWarning TargetWarning(
        VisualEvidenceTarget target,
        MediaEvidenceWarningCode code,
        string message)
    {
        return new MediaEvidenceWarning(
            code,
            message,
            targetKey:
                target.TargetKey);
    }

    internal static double? PreferHigher(
        double? left,
        double? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Math.Max(
            left.Value,
            right.Value);
    }
}
