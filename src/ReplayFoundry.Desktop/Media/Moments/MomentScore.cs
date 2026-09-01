using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentScoreComponent
{
    private readonly ReadOnlyCollection<MomentEvidenceReference>
        _evidenceReferences;

    public MomentScoreComponent(
        MomentScoreComponentCode code,
        double rawMeasuredValue,
        double normalizedValue,
        double configuredSignedWeight,
        double signedContribution,
        string explanation,
        IEnumerable<MomentEvidenceReference>? evidenceReferences = null)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        if (!double.IsFinite(rawMeasuredValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawMeasuredValue));
        }

        if (!double.IsFinite(normalizedValue) ||
            normalizedValue is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalizedValue));
        }

        if (!double.IsFinite(configuredSignedWeight) ||
            configuredSignedWeight is < -100 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuredSignedWeight));
        }

        double expectedContribution =
            normalizedValue *
            configuredSignedWeight;

        if (!double.IsFinite(signedContribution) ||
            Math.Abs(
                signedContribution -
                expectedContribution) > 0.000000001)
        {
            throw new ArgumentException(
                "A score component contribution must exactly equal normalized value multiplied by its signed weight.",
                nameof(signedContribution));
        }

        if (string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException(
                "A score component requires a plain-language explanation.",
                nameof(explanation));
        }

        MomentEvidenceReference[] snapshot =
            evidenceReferences?.ToArray() ??
            [];

        if (snapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Score-component evidence references cannot contain null entries.",
                nameof(evidenceReferences));
        }

        Code = code;
        RawMeasuredValue = rawMeasuredValue;
        NormalizedValue = normalizedValue;
        ConfiguredSignedWeight = configuredSignedWeight;
        SignedContribution = signedContribution;
        Explanation = explanation.Trim();
        _evidenceReferences =
            Array.AsReadOnly(snapshot);
    }

    public MomentScoreComponentCode Code { get; }

    public double RawMeasuredValue { get; }

    public double NormalizedValue { get; }

    public double ConfiguredSignedWeight { get; }

    public double SignedContribution { get; }

    public string Explanation { get; }

    public IReadOnlyList<MomentEvidenceReference>
        EvidenceReferences =>
        _evidenceReferences;
}

public sealed class MomentScore
{
    private readonly ReadOnlyCollection<MomentScoreComponent>
        _components;

    public MomentScore(
        IEnumerable<MomentScoreComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        MomentScoreComponent[] snapshot =
            components.ToArray();

        if (snapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "A moment score cannot contain null components.",
                nameof(components));
        }

        if (snapshot
            .GroupBy(static item => item.Code)
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A moment score cannot contain duplicate component codes.",
                nameof(components));
        }

        double rawTotal =
            snapshot.Sum(
                static item =>
                    item.SignedContribution);

        if (!double.IsFinite(rawTotal))
        {
            throw new ArgumentException(
                "A moment score must reconcile to a finite total.",
                nameof(components));
        }

        RawComponentTotal = rawTotal;
        HeuristicScore =
            Math.Clamp(
                rawTotal,
                0,
                100);

        _components =
            Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<MomentScoreComponent> Components =>
        _components;

    public double RawComponentTotal { get; }

    public double HeuristicScore { get; }
}
