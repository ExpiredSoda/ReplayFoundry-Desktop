using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;

namespace ReplayFoundry.Desktop.Features.Research;

public enum ResearchFeedbackChannel
{
    StudioSelection,
    Satisfaction,
    HiddenMomentReview,
}

public enum ResearchFeedbackValue
{
    Included,
    Excluded,
    Like,
    Neutral,
    Dislike,
    Accepted,
    Skipped,
}

public sealed class ResearchFeedbackRecord
{
    private readonly ReadOnlyCollection<ClipPreferenceFeature> _features;

    public ResearchFeedbackRecord(
        string candidateIdentity,
        string sourceIdentity,
        ResearchFeedbackChannel channel,
        ResearchFeedbackValue value,
        IEnumerable<ClipPreferenceFeature> features,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdentity);
        ArgumentNullException.ThrowIfNull(features);
        ClipPreferenceFeature[] snapshot = features
            .OrderBy(static feature => feature.Code)
            .ToArray();
        if (!Enum.IsDefined(channel) ||
            !Enum.IsDefined(value) ||
            recordedAtUtc.Offset != TimeSpan.Zero ||
            candidateIdentity.Length != 64 ||
            sourceIdentity.Length != 64 ||
            snapshot.Length == 0 ||
            snapshot.Any(static feature => feature is null) ||
            snapshot.Select(static feature => feature.Code).Distinct().Count() !=
                snapshot.Length)
        {
            throw new ArgumentException(
                "Research feedback requires pseudonymous identities, typed features, and a UTC decision.");
        }

        CandidateIdentity = candidateIdentity.ToUpperInvariant();
        SourceIdentity = sourceIdentity.ToUpperInvariant();
        Channel = channel;
        Value = value;
        _features = Array.AsReadOnly(snapshot);
        RecordedAtUtc = recordedAtUtc;
    }

    public const string SchemaVersion = "research-feedback-1.0";
    public string CandidateIdentity { get; }
    public string SourceIdentity { get; }
    public ResearchFeedbackChannel Channel { get; }
    public ResearchFeedbackValue Value { get; }
    public IReadOnlyList<ClipPreferenceFeature> Features => _features;
    public DateTimeOffset RecordedAtUtc { get; }
    public string Key => $"{CandidateIdentity}|{Channel}";
}

public interface IResearchFeedbackStore
{
    IReadOnlyList<ResearchFeedbackRecord> Current { get; }
    void Upsert(ResearchFeedbackRecord value);
    void Clear();
}

public sealed class InMemoryResearchFeedbackStore : IResearchFeedbackStore
{
    private readonly Dictionary<string, ResearchFeedbackRecord> _values =
        new(StringComparer.Ordinal);

    public IReadOnlyList<ResearchFeedbackRecord> Current =>
        Array.AsReadOnly(_values.Values
            .OrderByDescending(static value => value.RecordedAtUtc)
            .ToArray());

    public void Upsert(ResearchFeedbackRecord value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _values[value.Key] = value;
    }

    public void Clear() => _values.Clear();
}

public interface IResearchFeedbackRecorder
{
    void Record(
        string candidateId,
        string sourceFullPath,
        TimeSpan sourceDuration,
        ClipPreferenceFeatureVector features,
        ResearchFeedbackChannel channel,
        ResearchFeedbackValue value);
}

public sealed class ResearchFeedbackRecorder : IResearchFeedbackRecorder
{
    private readonly ResearchParticipationState _participation;
    private readonly IResearchFeedbackStore _store;

    public ResearchFeedbackRecorder(
        ResearchParticipationState participation,
        IResearchFeedbackStore store)
    {
        _participation = participation ??
            throw new ArgumentNullException(nameof(participation));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public void Record(
        string candidateId,
        string sourceFullPath,
        TimeSpan sourceDuration,
        ClipPreferenceFeatureVector features,
        ResearchFeedbackChannel channel,
        ResearchFeedbackValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFullPath);
        ArgumentNullException.ThrowIfNull(features);
        if (!_participation.IsEnabled)
        {
            return;
        }

        _store.Upsert(new ResearchFeedbackRecord(
            Hash(candidateId),
            Hash(Path.GetFullPath(sourceFullPath).ToUpperInvariant() + "|" +
                 sourceDuration.Ticks),
            channel,
            value,
            features.Features,
            DateTimeOffset.UtcNow));
    }

    private static string Hash(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)));
}
