using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Intelligence.Preferences;

public enum ClipPreferenceRating
{
    Dislike = -1,
    Neutral = 0,
    Like = 1,
}

public enum ClipPreferenceFeatureCode
{
    Duration,
    DeterministicScore,
    EpisodeDistinctiveness,
    EpisodeOnset,
    EpisodeRecovery,
    ContinuousActivity,
    SpeechCoverage,
    CreatorSpeech,
    GameDialogue,
    VisualSemanticSupport,
    VisualSemanticRejection,
}

public sealed record ClipPreferenceFeature
{
    public ClipPreferenceFeature(
        ClipPreferenceFeatureCode code,
        double normalizedValue)
    {
        if (!Enum.IsDefined(code) ||
            !double.IsFinite(normalizedValue) ||
            normalizedValue is < 0 or > 1)
        {
            throw new ArgumentException(
                "Preference features must be defined finite normalized measurements.");
        }

        Code = code;
        NormalizedValue = normalizedValue;
    }

    public ClipPreferenceFeatureCode Code { get; }
    public double NormalizedValue { get; }
}

public sealed class ClipPreferenceFeatureVector
{
    public const string SchemaVersion = "clip-preference-features-1.0";
    private readonly ReadOnlyCollection<ClipPreferenceFeature> _features;

    public ClipPreferenceFeatureVector(
        IEnumerable<ClipPreferenceFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        ClipPreferenceFeature[] snapshot = features
            .OrderBy(static feature => feature.Code)
            .ToArray();
        if (snapshot.Length == 0 ||
            snapshot.Any(static feature => feature is null) ||
            snapshot.Select(static feature => feature.Code)
                .Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "A preference vector requires unique typed measurements.",
                nameof(features));
        }

        _features = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<ClipPreferenceFeature> Features => _features;

    public double? Find(ClipPreferenceFeatureCode code) =>
        _features.FirstOrDefault(feature => feature.Code == code)
            ?.NormalizedValue;
}

public sealed record ClipPreferenceFeatureStatistics
{
    public ClipPreferenceFeatureStatistics(
        ClipPreferenceFeatureCode code,
        int likeCount,
        double likeSum,
        int dislikeCount,
        double dislikeSum)
    {
        if (!Enum.IsDefined(code) ||
            likeCount < 0 ||
            dislikeCount < 0 ||
            !double.IsFinite(likeSum) ||
            !double.IsFinite(dislikeSum) ||
            likeSum is < 0 ||
            dislikeSum is < 0 ||
            likeSum > likeCount ||
            dislikeSum > dislikeCount)
        {
            throw new ArgumentException(
                "Preference statistics must be finite bounded aggregates.");
        }

        Code = code;
        LikeCount = likeCount;
        LikeSum = likeSum;
        DislikeCount = dislikeCount;
        DislikeSum = dislikeSum;
    }

    public ClipPreferenceFeatureCode Code { get; }
    public int LikeCount { get; }
    public double LikeSum { get; }
    public int DislikeCount { get; }
    public double DislikeSum { get; }
    public double? LikeMean => LikeCount == 0 ? null : LikeSum / LikeCount;
    public double? DislikeMean => DislikeCount == 0 ? null : DislikeSum / DislikeCount;
}

public sealed record ClipPreferenceEvaluation(
    bool IsActive,
    double SignedContribution,
    int ComparedFeatureCount,
    string Explanation);

public sealed class ClipPreferenceProfile
{
    public const string PolicyVersion = "clip-preference-profile-1.0";
    public const int MinimumLikeCount = 3;
    public const int MinimumDislikeCount = 3;
    public const int MinimumRatedCount = 8;
    public const double MaximumAbsoluteContribution = 4;

    private readonly ReadOnlyCollection<ClipPreferenceFeatureStatistics>
        _statistics;

    public ClipPreferenceProfile(
        int likeCount,
        int neutralCount,
        int dislikeCount,
        IEnumerable<ClipPreferenceFeatureStatistics>? statistics = null)
    {
        if (likeCount < 0 || neutralCount < 0 || dislikeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(likeCount));
        }
        ClipPreferenceFeatureStatistics[] snapshot = statistics?
            .OrderBy(static value => value.Code)
            .ToArray() ?? [];
        if (snapshot.Any(static value => value is null) ||
            snapshot.Select(static value => value.Code).Distinct().Count() !=
                snapshot.Length ||
            snapshot.Any(value =>
                value.LikeCount > likeCount ||
                value.DislikeCount > dislikeCount))
        {
            throw new ArgumentException(
                "Preference statistics must cover the same aggregate rating counts.",
                nameof(statistics));
        }

        LikeCount = likeCount;
        NeutralCount = neutralCount;
        DislikeCount = dislikeCount;
        _statistics = Array.AsReadOnly(snapshot);
    }

    public static ClipPreferenceProfile Empty { get; } = new(0, 0, 0);

    public int LikeCount { get; }
    public int NeutralCount { get; }
    public int DislikeCount { get; }
    public int RatedCount => LikeCount + DislikeCount;
    public int TotalFeedbackCount => RatedCount + NeutralCount;
    public bool IsReady =>
        RatedCount >= MinimumRatedCount &&
        LikeCount >= MinimumLikeCount &&
        DislikeCount >= MinimumDislikeCount;
    public IReadOnlyList<ClipPreferenceFeatureStatistics> Statistics =>
        _statistics;

    public ClipPreferenceEvaluation Evaluate(
        ClipPreferenceFeatureVector vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (!IsReady)
        {
            return new(
                false,
                0,
                0,
                $"Preference history needs at least {MinimumRatedCount} rated clips, including {MinimumLikeCount} Likes and {MinimumDislikeCount} Dislikes.");
        }

        var signals = new List<double>();
        foreach (ClipPreferenceFeatureStatistics statistic in _statistics)
        {
            double? value = vector.Find(statistic.Code);
            if (value is null ||
                statistic.LikeMean is not double likeMean ||
                statistic.DislikeMean is not double dislikeMean)
            {
                continue;
            }

            signals.Add(
                Math.Abs(value.Value - dislikeMean) -
                Math.Abs(value.Value - likeMean));
        }
        if (signals.Count == 0)
        {
            return new(
                false,
                0,
                0,
                "The retained preference history has no comparable neutral features for this clip.");
        }

        double balanceConfidence = Math.Clamp(
            Math.Min(LikeCount, DislikeCount) / 10d,
            0,
            1);
        double contribution = Math.Clamp(
            signals.Average() *
            balanceConfidence *
            MaximumAbsoluteContribution,
            -MaximumAbsoluteContribution,
            MaximumAbsoluteContribution);
        return new(
            true,
            contribution,
            signals.Count,
            $"Local game-agnostic preference history compared {signals.Count} normalized clip features at {balanceConfidence:P0} sample confidence.");
    }
}

public interface IClipPreferenceProfileProvider
{
    ClipPreferenceProfile Current { get; }
}

public interface IClipPreferenceFeedbackStore :
    IClipPreferenceProfileProvider
{
    ClipPreferenceProfile Update(
        ClipPreferenceFeatureVector features,
        ClipPreferenceRating? previous,
        ClipPreferenceRating current);

    void Reset();
}
