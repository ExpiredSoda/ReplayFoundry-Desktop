namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MomentVisualSupportCorrelation
{
    public static double Measure(
        IEnumerable<ActivityBurst> gameplay,
        IEnumerable<ActivityBurst> presenter,
        TimeSpan agreementWindow)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        ArgumentNullException.ThrowIfNull(presenter);
        if (agreementWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(agreementWindow));
        }

        double seconds =
            Math.Max(
                0.000001,
                agreementWindow.TotalSeconds);
        return gameplay
            .SelectMany(
                left =>
                    presenter.Select(
                        right =>
                        {
                            double temporal =
                                Math.Clamp(
                                    1 -
                                    (
                                        left.PeakTimestamp -
                                        right.PeakTimestamp
                                    ).Duration().TotalSeconds /
                                    seconds,
                                    0,
                                    1);
                            double shape =
                                1 -
                                (
                                    Math.Abs(
                                        left.PeakProminence -
                                        right.PeakProminence) +
                                    Math.Abs(
                                        left.OnsetStrength -
                                        right.OnsetStrength) +
                                    Math.Abs(
                                        left.Concentration -
                                        right.Concentration) +
                                    Math.Abs(
                                        left.Occupancy -
                                        right.Occupancy)
                                ) / 4;
                            return Math.Clamp(
                                temporal *
                                Math.Clamp(shape, 0, 1),
                                0,
                                1);
                        }))
            .DefaultIfEmpty(0)
            .Max();
    }
}
