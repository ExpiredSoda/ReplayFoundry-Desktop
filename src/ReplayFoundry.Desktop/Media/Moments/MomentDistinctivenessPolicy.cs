namespace ReplayFoundry.Desktop.Media.Moments;

/// <summary>
/// Versioned, source-independent measurements used to distinguish a bounded
/// episode from uniform activity. These values describe retained signals only.
/// </summary>
public sealed class MomentDistinctivenessPolicy
{
    public const string CurrentVersion = "1.3";

    public MomentDistinctivenessPolicy(
        double correlationThreshold,
        double minimumIncrementalPresenterProminence,
        double uniformActivityOccupancyThreshold,
        double peakSeparationWeight,
        double integratedSeparationWeight,
        double onsetWeight,
        double concentrationWeight,
        double familyAgreementWeight,
        double recoveryOrBoundaryWeight,
        double baselineCoreSeparationWeight,
        double continuousUniformityPenaltyWeight,
        double singleFamilyDominancePenaltyWeight,
        double correlatedVisualSupportPenaltyWeight,
        string version = CurrentVersion)
    {
        ValidateRatio(correlationThreshold, nameof(correlationThreshold));
        ValidateRatio(
            minimumIncrementalPresenterProminence,
            nameof(minimumIncrementalPresenterProminence));
        ValidateRatio(
            uniformActivityOccupancyThreshold,
            nameof(uniformActivityOccupancyThreshold));
        ValidateNonNegative(
            peakSeparationWeight,
            nameof(peakSeparationWeight));
        ValidateNonNegative(
            integratedSeparationWeight,
            nameof(integratedSeparationWeight));
        ValidateNonNegative(onsetWeight, nameof(onsetWeight));
        ValidateNonNegative(concentrationWeight, nameof(concentrationWeight));
        ValidateNonNegative(familyAgreementWeight, nameof(familyAgreementWeight));
        ValidateNonNegative(
            recoveryOrBoundaryWeight,
            nameof(recoveryOrBoundaryWeight));
        ValidateNonNegative(
            baselineCoreSeparationWeight,
            nameof(baselineCoreSeparationWeight));
        ValidateNonNegative(
            continuousUniformityPenaltyWeight,
            nameof(continuousUniformityPenaltyWeight));
        ValidateNonNegative(
            singleFamilyDominancePenaltyWeight,
            nameof(singleFamilyDominancePenaltyWeight));
        ValidateNonNegative(
            correlatedVisualSupportPenaltyWeight,
            nameof(correlatedVisualSupportPenaltyWeight));

        double positiveTotal =
            peakSeparationWeight +
            integratedSeparationWeight +
            onsetWeight +
            concentrationWeight +
            familyAgreementWeight +
            recoveryOrBoundaryWeight +
            baselineCoreSeparationWeight;
        if (Math.Abs(positiveTotal - 1) > 0.000000001)
        {
            throw new ArgumentException(
                "Distinctiveness positive weights must sum to exactly one.");
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException(
                "Distinctiveness policy version cannot be blank.",
                nameof(version));
        }

        CorrelationThreshold = correlationThreshold;
        MinimumIncrementalPresenterProminence =
            minimumIncrementalPresenterProminence;
        UniformActivityOccupancyThreshold =
            uniformActivityOccupancyThreshold;
        PeakSeparationWeight = peakSeparationWeight;
        IntegratedSeparationWeight = integratedSeparationWeight;
        OnsetWeight = onsetWeight;
        ConcentrationWeight = concentrationWeight;
        FamilyAgreementWeight = familyAgreementWeight;
        RecoveryOrBoundaryWeight = recoveryOrBoundaryWeight;
        BaselineCoreSeparationWeight = baselineCoreSeparationWeight;
        ContinuousUniformityPenaltyWeight =
            continuousUniformityPenaltyWeight;
        SingleFamilyDominancePenaltyWeight =
            singleFamilyDominancePenaltyWeight;
        CorrelatedVisualSupportPenaltyWeight =
            correlatedVisualSupportPenaltyWeight;
        Version = version.Trim();
    }

    public double CorrelationThreshold { get; }
    public double MinimumIncrementalPresenterProminence { get; }
    public double UniformActivityOccupancyThreshold { get; }
    public double PeakSeparationWeight { get; }
    public double IntegratedSeparationWeight { get; }
    public double OnsetWeight { get; }
    public double ConcentrationWeight { get; }
    public double FamilyAgreementWeight { get; }
    public double RecoveryOrBoundaryWeight { get; }
    public double BaselineCoreSeparationWeight { get; }
    public double ContinuousUniformityPenaltyWeight { get; }
    public double SingleFamilyDominancePenaltyWeight { get; }
    public double CorrelatedVisualSupportPenaltyWeight { get; }
    public string Version { get; }

    public static MomentDistinctivenessPolicy CreateDefaults() =>
        new(
            correlationThreshold: 0.72,
            minimumIncrementalPresenterProminence: 0.12,
            uniformActivityOccupancyThreshold: 0.72,
            peakSeparationWeight: 0.22,
            integratedSeparationWeight: 0.18,
            onsetWeight: 0.14,
            concentrationWeight: 0.12,
            familyAgreementWeight: 0.12,
            recoveryOrBoundaryWeight: 0.10,
            baselineCoreSeparationWeight: 0.12,
            continuousUniformityPenaltyWeight: 0.25,
            singleFamilyDominancePenaltyWeight: 0.10,
            correlatedVisualSupportPenaltyWeight: 0.08);

    private static void ValidateRatio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
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
}
