namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MontageSegmentFeatureVector
{
    public MontageSegmentFeatureVector(
        double representativeCoverage,
        double peakContainment,
        double conciseActivationDensity,
        double multiFamilyDensity,
        double rankingValue)
    {
        RepresentativeCoverage =
            Ratio(
                representativeCoverage,
                nameof(representativeCoverage));
        PeakContainment =
            Ratio(peakContainment, nameof(peakContainment));
        ConciseActivationDensity =
            Ratio(
                conciseActivationDensity,
                nameof(conciseActivationDensity));
        MultiFamilyDensity =
            Ratio(multiFamilyDensity, nameof(multiFamilyDensity));
        RankingValue = Ratio(rankingValue, nameof(rankingValue));
    }

    public double RepresentativeCoverage { get; }
    public double PeakContainment { get; }
    public double ConciseActivationDensity { get; }
    public double MultiFamilyDensity { get; }
    public double RankingValue { get; }

    private static double Ratio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }
}
