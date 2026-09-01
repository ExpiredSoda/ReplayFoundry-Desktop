namespace ReplayFoundry.Desktop.Media.Moments;

public readonly record struct LocalSignalSample
{
    public LocalSignalSample(
        TimeSpan timestamp,
        double value)
    {
        if (timestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Timestamp = timestamp;
        Value = value;
    }

    public TimeSpan Timestamp { get; }
    public double Value { get; }
}

public sealed class LocalSignalContext
{
    public LocalSignalContext(
        TimeSpan timestamp,
        double rawValue,
        double localBaseline,
        double localSpread,
        double rawExcess,
        double normalizedProminence,
        double onsetStrength,
        double sustainedOccupancy,
        double integratedExcess,
        double localConcentration,
        double returnToBaseline)
    {
        if (timestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        ValidateFinite(rawValue, nameof(rawValue));
        ValidateFinite(localBaseline, nameof(localBaseline));
        ValidatePositive(localSpread, nameof(localSpread));
        ValidateNonNegative(rawExcess, nameof(rawExcess));
        ValidateRatio(normalizedProminence, nameof(normalizedProminence));
        ValidateRatio(onsetStrength, nameof(onsetStrength));
        ValidateRatio(sustainedOccupancy, nameof(sustainedOccupancy));
        ValidateNonNegative(integratedExcess, nameof(integratedExcess));
        ValidateRatio(localConcentration, nameof(localConcentration));
        ValidateRatio(returnToBaseline, nameof(returnToBaseline));

        Timestamp = timestamp;
        RawValue = rawValue;
        LocalBaseline = localBaseline;
        LocalSpread = localSpread;
        RawExcess = rawExcess;
        NormalizedProminence = normalizedProminence;
        OnsetStrength = onsetStrength;
        SustainedOccupancy = sustainedOccupancy;
        IntegratedExcess = integratedExcess;
        LocalConcentration = localConcentration;
        ReturnToBaseline = returnToBaseline;
    }

    public TimeSpan Timestamp { get; }
    public double RawValue { get; }
    public double LocalBaseline { get; }
    public double LocalSpread { get; }
    public double RawExcess { get; }
    public double NormalizedProminence { get; }
    public double OnsetStrength { get; }
    public double SustainedOccupancy { get; }
    public double IntegratedExcess { get; }
    public double LocalConcentration { get; }
    public double ReturnToBaseline { get; }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidatePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateRatio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

public static class LocalProminenceCalculator
{
    public static IReadOnlyList<LocalSignalContext> Calculate(
        IEnumerable<LocalSignalSample> samples,
        MomentCalibrationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(policy);

        LocalSignalSample[] ordered =
            samples
                .OrderBy(static sample => sample.Timestamp)
                .ToArray();

        if (ordered
            .Select(static sample => sample.Timestamp)
            .Distinct()
            .Count() != ordered.Length)
        {
            throw new ArgumentException(
                "Local signal samples cannot duplicate a timestamp.",
                nameof(samples));
        }

        var contexts =
            new LocalSignalContext[ordered.Length];

        for (int index = 0; index < ordered.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LocalSignalSample current = ordered[index];

            double[] baselineValues =
                ordered
                    .Where(
                        sample =>
                        {
                            TimeSpan distance =
                                (sample.Timestamp - current.Timestamp).Duration();
                            return distance <= policy.LocalBaselineHalfWindow &&
                                   distance >= policy.LocalBaselineGuardHalfWindow;
                        })
                    .Select(static sample => sample.Value)
                    .OrderBy(static value => value)
                    .ToArray();

            if (baselineValues.Length == 0)
            {
                baselineValues = [current.Value];
            }

            double baseline = Median(baselineValues);
            double spread =
                Math.Max(
                    policy.ProminenceSpreadFloor,
                    RobustSpread(baselineValues));
            double excess =
                Math.Max(0, current.Value - baseline);
            double prominence =
                Normalize(
                    excess,
                    spread,
                    policy.ProminenceSaturationMultiple);

            double[] preceding =
                ordered
                    .Where(
                        sample =>
                            sample.Timestamp < current.Timestamp &&
                            sample.Timestamp >= current.Timestamp - policy.OnsetLookback)
                    .Select(static sample => sample.Value)
                    .OrderBy(static value => value)
                    .ToArray();
            double precedingBaseline =
                preceding.Length == 0
                    ? baseline
                    : Median(preceding);
            double onset =
                Normalize(
                    Math.Max(0, current.Value - precedingBaseline),
                    spread,
                    policy.ProminenceSaturationMultiple);

            LocalSignalSample[] neighborhood =
                ordered
                    .Where(
                        sample =>
                            (sample.Timestamp - current.Timestamp).Duration() <=
                            policy.LocalBaselineGuardHalfWindow)
                    .ToArray();
            double[] excesses =
                neighborhood
                    .Select(sample => Math.Max(0, sample.Value - baseline))
                    .ToArray();
            int occupied =
                neighborhood.Count(
                    sample =>
                        sample.Value >= baseline + spread);
            double integrated =
                excesses.Sum();
            double totalContextExcess =
                ordered
                    .Where(
                        sample =>
                            (sample.Timestamp - current.Timestamp).Duration() <=
                            policy.LocalBaselineHalfWindow)
                    .Sum(sample => Math.Max(0, sample.Value - baseline));
            double concentration =
                totalContextExcess <= 0
                    ? 0
                    : Math.Clamp(integrated / totalContextExcess, 0, 1);

            double[] following =
                ordered
                    .Where(
                        sample =>
                            sample.Timestamp > current.Timestamp &&
                            sample.Timestamp <= current.Timestamp + policy.OnsetLookback)
                    .Select(static sample => sample.Value)
                    .ToArray();
            double returnToBaseline =
                following.Length == 0
                    ? 0
                    : Math.Clamp(
                        following.Count(value => value <= baseline + spread) /
                        (double)following.Length,
                        0,
                        1);

            contexts[index] =
                new LocalSignalContext(
                    current.Timestamp,
                    current.Value,
                    baseline,
                    spread,
                    excess,
                    prominence,
                    onset,
                    neighborhood.Length == 0
                        ? 0
                        : occupied / (double)neighborhood.Length,
                    integrated,
                    concentration,
                    returnToBaseline);
        }

        return Array.AsReadOnly(contexts);
    }

    internal static double Median(IReadOnlyList<double> ordered)
    {
        if (ordered.Count == 0)
        {
            throw new ArgumentException("A median requires at least one value.", nameof(ordered));
        }

        int middle = ordered.Count / 2;
        return (ordered.Count & 1) == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2d;
    }

    private static double RobustSpread(IReadOnlyList<double> ordered)
    {
        if (ordered.Count < 2)
        {
            return 0;
        }

        double q1 = Percentile(ordered, 0.25);
        double q3 = Percentile(ordered, 0.75);
        return Math.Max(0, (q3 - q1) / 1.349d);
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        double position = percentile * (ordered.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return ordered[lower];
        }

        double fraction = position - lower;
        return ordered[lower] + ((ordered[upper] - ordered[lower]) * fraction);
    }

    private static double Normalize(
        double excess,
        double spread,
        double saturationMultiple) =>
        Math.Clamp(
            excess / (spread * saturationMultiple),
            0,
            1);
}
