namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentEpisodeFeatureVector
{
    public const string CurrentVersion = "1.3";

    public MomentEpisodeFeatureVector(
        string version,
        double peakSeparation,
        double integratedSeparation,
        double onsetStrength,
        double recoverySupport,
        double activationConcentration,
        double activationOccupancy,
        double activationEntropy,
        double baselineCoreSeparation,
        double coreRecoverySeparation,
        double independentFamilyAgreement,
        double sceneClusterSupport,
        double continuousActivityRatio,
        double continuousUniformityPenalty,
        double singleFamilyDominancePenalty,
        double correlatedVisualSupport,
        double presenterIncrementalSupport,
        double distinctiveness)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException(
                "An episode feature vector requires a version.",
                nameof(version));
        }

        PeakSeparation = Ratio(peakSeparation, nameof(peakSeparation));
        IntegratedSeparation =
            Ratio(integratedSeparation, nameof(integratedSeparation));
        OnsetStrength = Ratio(onsetStrength, nameof(onsetStrength));
        RecoverySupport = Ratio(recoverySupport, nameof(recoverySupport));
        ActivationConcentration =
            Ratio(activationConcentration, nameof(activationConcentration));
        ActivationOccupancy =
            Ratio(activationOccupancy, nameof(activationOccupancy));
        ActivationEntropy =
            Ratio(activationEntropy, nameof(activationEntropy));
        BaselineCoreSeparation =
            Ratio(
                baselineCoreSeparation,
                nameof(baselineCoreSeparation));
        CoreRecoverySeparation =
            Ratio(coreRecoverySeparation, nameof(coreRecoverySeparation));
        IndependentFamilyAgreement =
            Ratio(
                independentFamilyAgreement,
                nameof(independentFamilyAgreement));
        SceneClusterSupport =
            Ratio(sceneClusterSupport, nameof(sceneClusterSupport));
        ContinuousActivityRatio =
            Ratio(continuousActivityRatio, nameof(continuousActivityRatio));
        ContinuousUniformityPenalty =
            Ratio(
                continuousUniformityPenalty,
                nameof(continuousUniformityPenalty));
        SingleFamilyDominancePenalty =
            Ratio(
                singleFamilyDominancePenalty,
                nameof(singleFamilyDominancePenalty));
        CorrelatedVisualSupport =
            Ratio(
                correlatedVisualSupport,
                nameof(correlatedVisualSupport));
        PresenterIncrementalSupport =
            Ratio(
                presenterIncrementalSupport,
                nameof(presenterIncrementalSupport));
        Distinctiveness =
            Ratio(distinctiveness, nameof(distinctiveness));
        Version = version.Trim();
    }

    public string Version { get; }
    public double PeakSeparation { get; }
    public double IntegratedSeparation { get; }
    public double OnsetStrength { get; }
    public double RecoverySupport { get; }
    public double ActivationConcentration { get; }
    public double ActivationOccupancy { get; }
    public double ActivationEntropy { get; }
    public double BaselineCoreSeparation { get; }
    public double CoreRecoverySeparation { get; }
    public double IndependentFamilyAgreement { get; }
    public double SceneClusterSupport { get; }
    public double ContinuousActivityRatio { get; }
    public double ContinuousUniformityPenalty { get; }
    public double SingleFamilyDominancePenalty { get; }
    public double CorrelatedVisualSupport { get; }
    public double PresenterIncrementalSupport { get; }
    public double Distinctiveness { get; }

    private static double Ratio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }
}
