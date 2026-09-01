using System;
using ReplayFoundry.Desktop.Media.Analysis.Visual;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public sealed class VisualTargetSignalSummary
{
    public VisualTargetSignalSummary(
        VisualEvidenceTarget target,
        string policyVersion,
        double darkLumaThreshold,
        double brightLumaThreshold,
        int sampleCount,
        double? meanNormalizedActivity,
        double? medianNormalizedActivity,
        double? maximumNormalizedActivity,
        double? meanNormalizedLuma,
        double? normalizedLumaStandardDeviation,
        double? meanNormalizedContrastSpan,
        double? meanNormalizedSaturation,
        TimeSpan? darkestSampleTimestamp,
        double? darkestNormalizedMeanLuma,
        TimeSpan? brightestSampleTimestamp,
        double? brightestNormalizedMeanLuma,
        double? darkSamplePercentage,
        double? brightSamplePercentage,
        TimeSpan maximumInterSampleGap)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException(
                "A visual signal summary requires a policy version.",
                nameof(policyVersion));
        }

        ValidateNormalized(
            darkLumaThreshold,
            nameof(darkLumaThreshold));
        ValidateNormalized(
            brightLumaThreshold,
            nameof(brightLumaThreshold));

        if (brightLumaThreshold <=
            darkLumaThreshold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(brightLumaThreshold),
                brightLumaThreshold,
                "Bright-luma threshold must exceed the dark-luma threshold.");
        }

        if (sampleCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount),
                sampleCount,
                "Visual signal sample count cannot be negative.");
        }

        ValidateNullableNormalized(
            meanNormalizedActivity,
            nameof(meanNormalizedActivity));
        ValidateNullableNormalized(
            medianNormalizedActivity,
            nameof(medianNormalizedActivity));
        ValidateNullableNormalized(
            maximumNormalizedActivity,
            nameof(maximumNormalizedActivity));
        ValidateNullableNormalized(
            meanNormalizedLuma,
            nameof(meanNormalizedLuma));
        ValidateNullableNormalized(
            normalizedLumaStandardDeviation,
            nameof(normalizedLumaStandardDeviation));
        ValidateNullableNormalized(
            meanNormalizedContrastSpan,
            nameof(meanNormalizedContrastSpan));
        ValidateNullableNormalized(
            meanNormalizedSaturation,
            nameof(meanNormalizedSaturation));
        ValidateNullableNormalized(
            darkestNormalizedMeanLuma,
            nameof(darkestNormalizedMeanLuma));
        ValidateNullableNormalized(
            brightestNormalizedMeanLuma,
            nameof(brightestNormalizedMeanLuma));
        ValidateNullablePercentage(
            darkSamplePercentage,
            nameof(darkSamplePercentage));
        ValidateNullablePercentage(
            brightSamplePercentage,
            nameof(brightSamplePercentage));

        bool hasExtremes =
            darkestSampleTimestamp is not null ||
            darkestNormalizedMeanLuma is not null ||
            brightestSampleTimestamp is not null ||
            brightestNormalizedMeanLuma is not null;

        if (sampleCount == 0 &&
            hasExtremes)
        {
            throw new ArgumentException(
                "An empty visual signal summary cannot report sampled extrema.");
        }

        ValidateTimestamp(
            darkestSampleTimestamp,
            target,
            nameof(darkestSampleTimestamp));
        ValidateTimestamp(
            brightestSampleTimestamp,
            target,
            nameof(brightestSampleTimestamp));

        if ((darkestSampleTimestamp is null) !=
                (darkestNormalizedMeanLuma is null) ||
            (brightestSampleTimestamp is null) !=
                (brightestNormalizedMeanLuma is null))
        {
            throw new ArgumentException(
                "Sampled luma extrema require both a timestamp and a value.");
        }

        if (maximumInterSampleGap < TimeSpan.Zero ||
            maximumInterSampleGap > target.Duration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumInterSampleGap),
                maximumInterSampleGap,
                "Maximum visual sample gap must remain inside the target duration.");
        }

        Target = target;
        PolicyVersion = policyVersion.Trim();
        DarkLumaThreshold = darkLumaThreshold;
        BrightLumaThreshold = brightLumaThreshold;
        SampleCount = sampleCount;
        MeanNormalizedActivity =
            meanNormalizedActivity;
        MedianNormalizedActivity =
            medianNormalizedActivity;
        MaximumNormalizedActivity =
            maximumNormalizedActivity;
        MeanNormalizedLuma = meanNormalizedLuma;
        NormalizedLumaStandardDeviation =
            normalizedLumaStandardDeviation;
        MeanNormalizedContrastSpan =
            meanNormalizedContrastSpan;
        MeanNormalizedSaturation =
            meanNormalizedSaturation;
        DarkestSampleTimestamp =
            darkestSampleTimestamp;
        DarkestNormalizedMeanLuma =
            darkestNormalizedMeanLuma;
        BrightestSampleTimestamp =
            brightestSampleTimestamp;
        BrightestNormalizedMeanLuma =
            brightestNormalizedMeanLuma;
        DarkSamplePercentage =
            darkSamplePercentage;
        BrightSamplePercentage =
            brightSamplePercentage;
        MaximumInterSampleGap =
            maximumInterSampleGap;
    }

    public VisualEvidenceTarget Target { get; }

    public string PolicyVersion { get; }

    public double DarkLumaThreshold { get; }

    public double BrightLumaThreshold { get; }

    public int SampleCount { get; }

    public double? MeanNormalizedActivity { get; }

    public double? MedianNormalizedActivity { get; }

    public double? MaximumNormalizedActivity { get; }

    public double? MeanNormalizedLuma { get; }

    public double? NormalizedLumaStandardDeviation { get; }

    public double? MeanNormalizedContrastSpan { get; }

    public double? MeanNormalizedSaturation { get; }

    public TimeSpan? DarkestSampleTimestamp { get; }

    public double? DarkestNormalizedMeanLuma { get; }

    public TimeSpan? BrightestSampleTimestamp { get; }

    public double? BrightestNormalizedMeanLuma { get; }

    public double? DarkSamplePercentage { get; }

    public double? BrightSamplePercentage { get; }

    public TimeSpan MaximumInterSampleGap { get; }

    private static void ValidateTimestamp(
        TimeSpan? value,
        VisualEvidenceTarget target,
        string parameterName)
    {
        if (value is TimeSpan actual &&
            (actual < target.Start ||
             actual >= target.End))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A visual signal summary timestamp must remain within the target interval.");
        }
    }

    private static void ValidateNullableNormalized(
        double? value,
        string parameterName)
    {
        if (value is double actual)
        {
            ValidateNormalized(
                actual,
                parameterName);
        }
    }

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
                "Normalized signal summary values must be finite and between zero and one.");
        }
    }

    private static void ValidateNullablePercentage(
        double? value,
        string parameterName)
    {
        if (value is double actual &&
            (!double.IsFinite(actual) ||
             actual is < 0 or > 100))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Signal summary percentages must be finite and between zero and 100.");
        }
    }
}
