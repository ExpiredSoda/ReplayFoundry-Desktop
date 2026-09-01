using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;

public enum EditorialMetadataPreferenceFeatureCode
{
    TitleCharacterCount,
    TitleUppercaseLetterRatio,
    TitleDigitCharacterRatio,
    TitlePunctuationCharacterRatio,
    DescriptionCharacterCount,
    DescriptionLineCount,
    DescriptionDigitCharacterRatio,
    DescriptionPunctuationCharacterRatio,
    TagCount,
    TagCharacterCount,
    TagAverageCharacterCount,
    TagPunctuationCharacterRatio,
}

public sealed record EditorialMetadataPreferenceFeature
{
    public EditorialMetadataPreferenceFeature(
        EditorialMetadataPreferenceFeatureCode code,
        double normalizedValue)
    {
        if (!Enum.IsDefined(code) ||
            !double.IsFinite(normalizedValue) ||
            normalizedValue is < 0 or > 1)
        {
            throw new ArgumentException(
                "Editorial preference features must be defined finite normalized measurements.");
        }

        Code = code;
        NormalizedValue = normalizedValue;
    }

    public EditorialMetadataPreferenceFeatureCode Code { get; }
    public double NormalizedValue { get; }
}

public sealed class EditorialMetadataPreferenceFeatureVector
{
    public const string SchemaVersion =
        "editorial-metadata-structural-features-1.0";

    private readonly ReadOnlyCollection<EditorialMetadataPreferenceFeature>
        _features;

    public EditorialMetadataPreferenceFeatureVector(
        IEnumerable<EditorialMetadataPreferenceFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        EditorialMetadataPreferenceFeature[] snapshot = features
            .OrderBy(static feature => feature.Code)
            .ToArray();
        if (snapshot.Length == 0 ||
            snapshot.Any(static feature => feature is null) ||
            snapshot.Select(static feature => feature.Code)
                .Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "An editorial preference vector requires unique typed measurements.",
                nameof(features));
        }

        _features = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<EditorialMetadataPreferenceFeature> Features =>
        _features;

    public double? Find(EditorialMetadataPreferenceFeatureCode code) =>
        _features.FirstOrDefault(feature => feature.Code == code)
            ?.NormalizedValue;
}

public enum EditorialMetadataPreferenceEvidenceKind
{
    UnchangedPublish,
    HumanCorrection,
    ExplicitWordingRating,
    ConfirmedYouTubeCorrection,
}

public enum EditorialMetadataPreferenceOutcome
{
    Rejected = -1,
    Neutral = 0,
    Accepted = 1,
}

public enum EditorialMetadataWordingRating
{
    Dislike = -1,
    Neutral = 0,
    Like = 1,
}

public sealed record EditorialMetadataPreferenceObservation
{
    internal EditorialMetadataPreferenceObservation(
        EditorialMetadataPreferenceFeatureVector features,
        EditorialMetadataPreferenceOutcome outcome,
        double weight)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (!Enum.IsDefined(outcome) ||
            !double.IsFinite(weight) ||
            weight <= 0)
        {
            throw new ArgumentException(
                "Editorial preference observations require a defined outcome and positive finite weight.");
        }

        Features = features;
        Outcome = outcome;
        Weight = weight;
    }

    public EditorialMetadataPreferenceFeatureVector Features { get; }
    public EditorialMetadataPreferenceOutcome Outcome { get; }
    public double Weight { get; }
}

public sealed class EditorialMetadataPreferenceEvidence
{
    public const string ContractVersion =
        "editorial-metadata-preference-evidence-1.0";
    public const double WeakUnchangedPublishWeight = 0.25;
    public const double StrongEvidenceWeight = 1;

    private readonly ReadOnlyCollection<EditorialMetadataPreferenceObservation>
        _observations;

    private EditorialMetadataPreferenceEvidence(
        EditorialMetadataPreferenceEvidenceKind kind,
        IEnumerable<EditorialMetadataPreferenceObservation> observations,
        EditorialMetadataWordingRating? explicitRating = null)
    {
        if (!Enum.IsDefined(kind) ||
            explicitRating is { } rating && !Enum.IsDefined(rating))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ArgumentNullException.ThrowIfNull(observations);
        EditorialMetadataPreferenceObservation[] snapshot =
            observations.ToArray();
        if (snapshot.Length == 0 ||
            snapshot.Any(static observation => observation is null))
        {
            throw new ArgumentException(
                "Editorial preference evidence requires immutable observations.",
                nameof(observations));
        }

        Kind = kind;
        ExplicitRating = explicitRating;
        _observations = Array.AsReadOnly(snapshot);
    }

    public EditorialMetadataPreferenceEvidenceKind Kind { get; }
    public EditorialMetadataWordingRating? ExplicitRating { get; }
    public IReadOnlyList<EditorialMetadataPreferenceObservation> Observations =>
        _observations;

    public static EditorialMetadataPreferenceEvidence UnchangedPublish(
        EditorialMetadataPreferenceFeatureVector published) =>
        new(
            EditorialMetadataPreferenceEvidenceKind.UnchangedPublish,
            [
                new EditorialMetadataPreferenceObservation(
                    published ?? throw new ArgumentNullException(
                        nameof(published)),
                    EditorialMetadataPreferenceOutcome.Accepted,
                    WeakUnchangedPublishWeight),
            ]);

    public static EditorialMetadataPreferenceEvidence HumanCorrection(
        EditorialMetadataPreferenceFeatureVector before,
        EditorialMetadataPreferenceFeatureVector after) =>
        Correction(
            EditorialMetadataPreferenceEvidenceKind.HumanCorrection,
            before,
            after);

    public static EditorialMetadataPreferenceEvidence ExplicitWordingRating(
        EditorialMetadataPreferenceFeatureVector wording,
        EditorialMetadataWordingRating rating)
    {
        ArgumentNullException.ThrowIfNull(wording);
        if (!Enum.IsDefined(rating))
        {
            throw new ArgumentOutOfRangeException(nameof(rating));
        }

        return new(
            EditorialMetadataPreferenceEvidenceKind.ExplicitWordingRating,
            [
                new EditorialMetadataPreferenceObservation(
                    wording,
                    rating switch
                    {
                        EditorialMetadataWordingRating.Like =>
                            EditorialMetadataPreferenceOutcome.Accepted,
                        EditorialMetadataWordingRating.Neutral =>
                            EditorialMetadataPreferenceOutcome.Neutral,
                        EditorialMetadataWordingRating.Dislike =>
                            EditorialMetadataPreferenceOutcome.Rejected,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(rating)),
                    },
                    StrongEvidenceWeight),
            ],
            rating);
    }

    public static EditorialMetadataPreferenceEvidence
        ConfirmedYouTubeCorrection(
            EditorialMetadataPreferenceFeatureVector before,
            EditorialMetadataPreferenceFeatureVector after) =>
        Correction(
            EditorialMetadataPreferenceEvidenceKind
                .ConfirmedYouTubeCorrection,
            before,
            after);

    private static EditorialMetadataPreferenceEvidence Correction(
        EditorialMetadataPreferenceEvidenceKind kind,
        EditorialMetadataPreferenceFeatureVector before,
        EditorialMetadataPreferenceFeatureVector after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return new(
            kind,
            [
                new EditorialMetadataPreferenceObservation(
                    before,
                    EditorialMetadataPreferenceOutcome.Rejected,
                    StrongEvidenceWeight),
                new EditorialMetadataPreferenceObservation(
                    after,
                    EditorialMetadataPreferenceOutcome.Accepted,
                    StrongEvidenceWeight),
            ]);
    }
}

public sealed record EditorialMetadataPreferenceFeatureStatistics
{
    public EditorialMetadataPreferenceFeatureStatistics(
        EditorialMetadataPreferenceFeatureCode code,
        double acceptedWeight,
        double acceptedSum,
        double rejectedWeight,
        double rejectedSum)
    {
        if (!Enum.IsDefined(code) ||
            !IsBoundedAggregate(acceptedWeight, acceptedSum) ||
            !IsBoundedAggregate(rejectedWeight, rejectedSum))
        {
            throw new ArgumentException(
                "Editorial preference statistics must be finite bounded aggregates.");
        }

        Code = code;
        AcceptedWeight = acceptedWeight;
        AcceptedSum = acceptedSum;
        RejectedWeight = rejectedWeight;
        RejectedSum = rejectedSum;
    }

    public EditorialMetadataPreferenceFeatureCode Code { get; }
    public double AcceptedWeight { get; }
    public double AcceptedSum { get; }
    public double RejectedWeight { get; }
    public double RejectedSum { get; }
    public double? AcceptedMean =>
        AcceptedWeight == 0 ? null : AcceptedSum / AcceptedWeight;
    public double? RejectedMean =>
        RejectedWeight == 0 ? null : RejectedSum / RejectedWeight;

    private static bool IsBoundedAggregate(double weight, double sum) =>
        double.IsFinite(weight) &&
        double.IsFinite(sum) &&
        weight >= 0 &&
        sum >= 0 &&
        sum <= weight;
}

public sealed record EditorialMetadataPreferenceEvidenceStatistics
{
    public EditorialMetadataPreferenceEvidenceStatistics(
        EditorialMetadataPreferenceEvidenceKind kind,
        int count)
    {
        if (!Enum.IsDefined(kind) || count <= 0)
        {
            throw new ArgumentException(
                "Editorial preference evidence counts must be defined and positive.");
        }

        Kind = kind;
        Count = count;
    }

    public EditorialMetadataPreferenceEvidenceKind Kind { get; }
    public int Count { get; }
}

public sealed class EditorialMetadataPreferenceProfile
{
    public const string PolicyVersion =
        "editorial-metadata-preference-profile-1.0";

    private const double Tolerance = 1e-9;

    private readonly ReadOnlyCollection<
        EditorialMetadataPreferenceEvidenceStatistics> _evidence;
    private readonly ReadOnlyCollection<
        EditorialMetadataPreferenceFeatureStatistics> _features;
    private readonly int _evidenceCount;

    public EditorialMetadataPreferenceProfile(
        double acceptedWeight,
        double neutralWeight,
        double rejectedWeight,
        IEnumerable<EditorialMetadataPreferenceEvidenceStatistics>?
            evidence = null,
        IEnumerable<EditorialMetadataPreferenceFeatureStatistics>?
            features = null)
    {
        if (!IsWeight(acceptedWeight) ||
            !IsWeight(neutralWeight) ||
            !IsWeight(rejectedWeight))
        {
            throw new ArgumentException(
                "Editorial preference profile weights must be finite and non-negative.");
        }
        EditorialMetadataPreferenceEvidenceStatistics[] evidenceSnapshot =
            evidence?.OrderBy(static value => value.Kind).ToArray() ?? [];
        EditorialMetadataPreferenceFeatureStatistics[] featureSnapshot =
            features?.OrderBy(static value => value.Code).ToArray() ?? [];
        long evidenceCount = evidenceSnapshot.Sum(
            static value => (long)(value?.Count ?? 0));
        if (evidenceSnapshot.Any(static value => value is null) ||
            evidenceSnapshot.Select(static value => value.Kind)
                .Distinct().Count() != evidenceSnapshot.Length ||
            evidenceCount > int.MaxValue ||
            featureSnapshot.Any(static value => value is null) ||
            featureSnapshot.Select(static value => value.Code)
                .Distinct().Count() != featureSnapshot.Length ||
            !EvidenceWeightsAreConsistent(
                acceptedWeight,
                neutralWeight,
                rejectedWeight,
                evidenceSnapshot) ||
            featureSnapshot.Any(value =>
                value.AcceptedWeight > acceptedWeight ||
                value.RejectedWeight > rejectedWeight))
        {
            throw new ArgumentException(
                "Editorial preference profile aggregates are inconsistent.");
        }

        AcceptedWeight = acceptedWeight;
        NeutralWeight = neutralWeight;
        RejectedWeight = rejectedWeight;
        _evidence = Array.AsReadOnly(evidenceSnapshot);
        _features = Array.AsReadOnly(featureSnapshot);
        _evidenceCount = (int)evidenceCount;
    }

    public static EditorialMetadataPreferenceProfile Empty { get; } =
        new(0, 0, 0);

    public double AcceptedWeight { get; }
    public double NeutralWeight { get; }
    public double RejectedWeight { get; }
    public int EvidenceCount => _evidenceCount;
    public IReadOnlyList<EditorialMetadataPreferenceEvidenceStatistics>
        Evidence => _evidence;
    public IReadOnlyList<EditorialMetadataPreferenceFeatureStatistics>
        Features => _features;
    public bool IsEmpty => EvidenceCount == 0;

    public int Count(EditorialMetadataPreferenceEvidenceKind kind) =>
        _evidence.FirstOrDefault(value => value.Kind == kind)?.Count ?? 0;

    public EditorialMetadataPreferenceFeatureStatistics? Find(
        EditorialMetadataPreferenceFeatureCode code) =>
        _features.FirstOrDefault(value => value.Code == code);

    private static bool IsWeight(double value) =>
        double.IsFinite(value) && value >= 0;

    private static bool EvidenceWeightsAreConsistent(
        double acceptedWeight,
        double neutralWeight,
        double rejectedWeight,
        IReadOnlyList<EditorialMetadataPreferenceEvidenceStatistics>
            evidence)
    {
        int Count(EditorialMetadataPreferenceEvidenceKind kind) =>
            evidence.FirstOrDefault(value => value.Kind == kind)?.Count ?? 0;

        int unchanged = Count(
            EditorialMetadataPreferenceEvidenceKind.UnchangedPublish);
        double correctionCount =
            (double)Count(
                EditorialMetadataPreferenceEvidenceKind.HumanCorrection) +
            Count(
                EditorialMetadataPreferenceEvidenceKind
                    .ConfirmedYouTubeCorrection);
        int explicitRatings = Count(
            EditorialMetadataPreferenceEvidenceKind.ExplicitWordingRating);
        double explicitAccepted = acceptedWeight -
            correctionCount -
            unchanged *
                EditorialMetadataPreferenceEvidence
                    .WeakUnchangedPublishWeight;
        double explicitRejected = rejectedWeight - correctionCount;

        return explicitAccepted >= -Tolerance &&
            explicitRejected >= -Tolerance &&
            neutralWeight >= -Tolerance &&
            Math.Abs(
                explicitAccepted +
                explicitRejected +
                neutralWeight -
                explicitRatings *
                    EditorialMetadataPreferenceEvidence
                        .StrongEvidenceWeight) <= Tolerance;
    }
}

public interface IEditorialMetadataPreferenceProfileProvider
{
    EditorialMetadataPreferenceProfile Current { get; }
}

public interface IEditorialMetadataPreferenceStore :
    IEditorialMetadataPreferenceProfileProvider
{
    EditorialMetadataPreferenceProfile Update(
        EditorialMetadataPreferenceEvidence? previous,
        EditorialMetadataPreferenceEvidence current);

    void Reset();
}

public interface IEditorialMetadataPreferenceLearningConsent
{
    bool IsEnabled { get; }
}
