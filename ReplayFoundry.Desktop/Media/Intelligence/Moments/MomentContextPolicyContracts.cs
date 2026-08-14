using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Enrichment;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Media.Intelligence.Moments;

public sealed class MomentContextOptions
{
    public const string CurrentPolicyVersion = "0.3";

    public MomentContextOptions(
        TimeSpan relationshipTolerance,
        string policyVersion = CurrentPolicyVersion)
    {
        if (relationshipTolerance < TimeSpan.Zero ||
            relationshipTolerance > TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(relationshipTolerance),
                "Relationship tolerance must be between zero and five seconds.");
        }

        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException(
                "Moment context options require a policy version.",
                nameof(policyVersion));
        }

        RelationshipTolerance = relationshipTolerance;
        PolicyVersion = policyVersion.Trim();
    }

    public TimeSpan RelationshipTolerance { get; }

    public string PolicyVersion { get; }

    public static MomentContextOptions CreateDefaults() =>
        new(TimeSpan.FromMilliseconds(500));
}

public sealed record MomentDeterministicScoreComponentSnapshot
{
    private readonly ReadOnlyCollection<string> _evidenceReferenceIds;

    public MomentDeterministicScoreComponentSnapshot(
        string code,
        double rawValue,
        double normalizedValue,
        double signedWeight,
        double signedContribution,
        string explanation,
        IEnumerable<string>? evidenceReferenceIds = null)
    {
        if (string.IsNullOrWhiteSpace(code) ||
            !double.IsFinite(rawValue) ||
            !double.IsFinite(normalizedValue) ||
            normalizedValue is < 0 or > 1 ||
            !double.IsFinite(signedWeight) ||
            !double.IsFinite(signedContribution) ||
            Math.Abs(
                signedContribution -
                normalizedValue * signedWeight) > 0.000000001 ||
            string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException(
                "A deterministic score-component snapshot must remain finite and exactly reconciled.");
        }

        string[] references =
            evidenceReferenceIds?.Select(Required).ToArray() ??
            [];

        if (references.Distinct(StringComparer.Ordinal).Count() !=
            references.Length)
        {
            throw new ArgumentException(
                "Score-component evidence-reference identities must be unique.",
                nameof(evidenceReferenceIds));
        }

        Code = code.Trim();
        RawValue = rawValue;
        NormalizedValue = normalizedValue;
        SignedWeight = signedWeight;
        SignedContribution = signedContribution;
        Explanation = explanation.Trim();
        _evidenceReferenceIds = Array.AsReadOnly(references);
    }

    public string Code { get; }

    public double RawValue { get; }

    public double NormalizedValue { get; }

    public double SignedWeight { get; }

    public double SignedContribution { get; }

    public string Explanation { get; }

    public IReadOnlyList<string> EvidenceReferenceIds =>
        _evidenceReferenceIds;

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "Evidence-reference identities cannot be blank.")
            : value.Trim();
}
