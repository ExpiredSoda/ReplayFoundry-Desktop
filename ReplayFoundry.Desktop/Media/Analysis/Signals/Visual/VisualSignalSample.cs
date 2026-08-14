using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;

public sealed class VisualSignalSample
{
    public VisualSignalSample(
        string targetKey,
        TimeSpan timestamp,
        double normalizedMeanLuma,
        double normalizedLowLuma,
        double normalizedHighLuma,
        double normalizedMeanSaturation,
        double? normalizedActivity,
        int signalBitDepth)
    {
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            throw new ArgumentException(
                "A visual signal sample requires a target key.",
                nameof(targetKey));
        }

        if (timestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                timestamp,
                "A visual signal timestamp cannot be negative.");
        }

        ValidateNormalized(
            normalizedMeanLuma,
            nameof(normalizedMeanLuma));
        ValidateNormalized(
            normalizedLowLuma,
            nameof(normalizedLowLuma));
        ValidateNormalized(
            normalizedHighLuma,
            nameof(normalizedHighLuma));
        ValidateNormalized(
            normalizedMeanSaturation,
            nameof(normalizedMeanSaturation));

        if (normalizedActivity is double activity)
        {
            ValidateNormalized(
                activity,
                nameof(normalizedActivity));
        }

        // signalstats YLOW/YHIGH are distribution percentiles. A small
        // outlier tail can legitimately place YAVG just outside them, so
        // only the percentile ordering itself is guaranteed.
        if (normalizedLowLuma > normalizedHighLuma)
        {
            throw new ArgumentException(
                "Visual luma percentiles must satisfy low <= high.");
        }

        if (signalBitDepth is < 8 or > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(signalBitDepth),
                signalBitDepth,
                "Visual signal bit depth must be between 8 and 16.");
        }

        TargetKey = targetKey.Trim();
        Timestamp = timestamp;
        NormalizedMeanLuma = normalizedMeanLuma;
        NormalizedLowLuma = normalizedLowLuma;
        NormalizedHighLuma = normalizedHighLuma;
        NormalizedMeanSaturation =
            normalizedMeanSaturation;
        NormalizedActivity = normalizedActivity;
        SignalBitDepth = signalBitDepth;
    }

    public string TargetKey { get; }

    public TimeSpan Timestamp { get; }

    public double NormalizedMeanLuma { get; }

    public double NormalizedLowLuma { get; }

    public double NormalizedHighLuma { get; }

    public double NormalizedLumaSpan =>
        NormalizedHighLuma -
        NormalizedLowLuma;

    public double NormalizedMeanSaturation { get; }

    public double? NormalizedActivity { get; }

    public int SignalBitDepth { get; }

    private static void ValidateNormalized(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Normalized visual signal values must be finite and between zero and one.");
        }
    }
}
