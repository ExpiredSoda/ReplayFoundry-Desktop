using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonClipPreferenceFeedbackStore :
    IClipPreferenceFeedbackStore
{
    private const string SchemaVersion = "clip-preference-store-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private MutableStore _store;

    public JsonClipPreferenceFeedbackStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "The preference store path must be fully qualified.",
                nameof(path));
        }

        _path = Path.GetFullPath(path);
        _store = Load(_path);
        Current = BuildProfile(_store);
    }

    public static JsonClipPreferenceFeedbackStore CreateDefault()
    {
        return new(ReplayFoundryLocalDataPaths.Resolve(
            overridePath: null,
            "clip-preferences.json"));
    }

    public ClipPreferenceProfile Current { get; private set; }

    public ClipPreferenceProfile Update(
        ClipPreferenceFeatureVector features,
        ClipPreferenceRating? previous,
        ClipPreferenceRating current)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (previous is not null && !Enum.IsDefined(previous.Value) ||
            !Enum.IsDefined(current))
        {
            throw new ArgumentOutOfRangeException(nameof(current));
        }
        lock (_gate)
        {
            if (previous == current)
            {
                return Current;
            }

            MutableStore updated = _store.Clone();
            if (previous is ClipPreferenceRating oldRating)
            {
                Adjust(updated, features, oldRating, -1);
            }
            Adjust(updated, features, current, 1);
            Validate(updated);
            WriteAtomic(updated);
            _store = updated;
            Current = BuildProfile(updated);
            return Current;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
            _store = new MutableStore();
            Current = ClipPreferenceProfile.Empty;
        }
    }

    private static void Adjust(
        MutableStore store,
        ClipPreferenceFeatureVector vector,
        ClipPreferenceRating rating,
        int direction)
    {
        switch (rating)
        {
            case ClipPreferenceRating.Like:
                store.LikeCount += direction;
                break;
            case ClipPreferenceRating.Neutral:
                store.NeutralCount += direction;
                return;
            case ClipPreferenceRating.Dislike:
                store.DislikeCount += direction;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(rating));
        }

        foreach (ClipPreferenceFeature feature in vector.Features)
        {
            if (!store.Features.TryGetValue(
                    feature.Code,
                    out MutableFeature? aggregate))
            {
                aggregate = new MutableFeature();
                store.Features.Add(feature.Code, aggregate);
            }
            if (rating == ClipPreferenceRating.Like)
            {
                aggregate.LikeCount += direction;
                aggregate.LikeSum += direction * feature.NormalizedValue;
            }
            else
            {
                aggregate.DislikeCount += direction;
                aggregate.DislikeSum += direction * feature.NormalizedValue;
            }
        }

        foreach (ClipPreferenceFeatureCode empty in store.Features
                     .Where(static pair =>
                         pair.Value.LikeCount == 0 &&
                         pair.Value.DislikeCount == 0)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            store.Features.Remove(empty);
        }
    }

    private static MutableStore Load(string path)
    {
        if (!File.Exists(path))
        {
            return new MutableStore();
        }
        try
        {
            StoreDocument? document = JsonSerializer.Deserialize<StoreDocument>(
                File.ReadAllText(path),
                JsonOptions);
            if (document is null ||
                document.SchemaVersion != SchemaVersion ||
                document.FeatureSchemaVersion !=
                    ClipPreferenceFeatureVector.SchemaVersion ||
                document.PolicyVersion != ClipPreferenceProfile.PolicyVersion)
            {
                throw new InvalidDataException(
                    "The local clip-preference schema is unsupported.");
            }
            var result = new MutableStore
            {
                LikeCount = document.LikeCount,
                NeutralCount = document.NeutralCount,
                DislikeCount = document.DislikeCount,
            };
            foreach (FeatureDocument feature in document.Features ?? [])
            {
                if (!Enum.TryParse(
                        feature.Code,
                        ignoreCase: false,
                        out ClipPreferenceFeatureCode code) ||
                    result.Features.ContainsKey(code))
                {
                    throw new InvalidDataException(
                        "The local clip-preference feature list is invalid.");
                }
                result.Features.Add(
                    code,
                    new MutableFeature
                    {
                        LikeCount = feature.LikeCount,
                        LikeSum = feature.LikeSum,
                        DislikeCount = feature.DislikeCount,
                        DislikeSum = feature.DislikeSum,
                    });
            }
            Validate(result);
            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The local clip-preference file is not valid JSON.",
                exception);
        }
    }

    private void WriteAtomic(MutableStore value)
    {
        var document = new StoreDocument
        {
            SchemaVersion = SchemaVersion,
            FeatureSchemaVersion =
                ClipPreferenceFeatureVector.SchemaVersion,
            PolicyVersion = ClipPreferenceProfile.PolicyVersion,
            LikeCount = value.LikeCount,
            NeutralCount = value.NeutralCount,
            DislikeCount = value.DislikeCount,
            Features = value.Features
                .OrderBy(static pair => pair.Key)
                .Select(pair => new FeatureDocument
                {
                    Code = pair.Key.ToString(),
                    LikeCount = pair.Value.LikeCount,
                    LikeSum = pair.Value.LikeSum,
                    DislikeCount = pair.Value.DislikeCount,
                    DislikeSum = pair.Value.DislikeSum,
                })
                .ToArray(),
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

    private static void Validate(MutableStore value)
    {
        if (value.LikeCount < 0 ||
            value.NeutralCount < 0 ||
            value.DislikeCount < 0 ||
            value.Features.Any(pair =>
                !Enum.IsDefined(pair.Key) ||
                pair.Value.LikeCount < 0 ||
                pair.Value.DislikeCount < 0 ||
                pair.Value.LikeCount > value.LikeCount ||
                pair.Value.DislikeCount > value.DislikeCount ||
                !double.IsFinite(pair.Value.LikeSum) ||
                !double.IsFinite(pair.Value.DislikeSum) ||
                pair.Value.LikeSum is < -1e-9 ||
                pair.Value.DislikeSum is < -1e-9 ||
                pair.Value.LikeSum > pair.Value.LikeCount + 1e-9 ||
                pair.Value.DislikeSum > pair.Value.DislikeCount + 1e-9))
        {
            throw new InvalidDataException(
                "The local clip-preference aggregates are inconsistent.");
        }
    }

    private static ClipPreferenceProfile BuildProfile(MutableStore value) =>
        new(
            value.LikeCount,
            value.NeutralCount,
            value.DislikeCount,
            value.Features.Select(pair =>
                new ClipPreferenceFeatureStatistics(
                    pair.Key,
                    pair.Value.LikeCount,
                    Math.Clamp(pair.Value.LikeSum, 0, pair.Value.LikeCount),
                    pair.Value.DislikeCount,
                    Math.Clamp(
                        pair.Value.DislikeSum,
                        0,
                        pair.Value.DislikeCount))));

    private sealed class MutableStore
    {
        public int LikeCount { get; set; }
        public int NeutralCount { get; set; }
        public int DislikeCount { get; set; }
        public Dictionary<ClipPreferenceFeatureCode, MutableFeature> Features
        { get; } = [];

        public MutableStore Clone()
        {
            var clone = new MutableStore
            {
                LikeCount = LikeCount,
                NeutralCount = NeutralCount,
                DislikeCount = DislikeCount,
            };
            foreach ((ClipPreferenceFeatureCode code, MutableFeature value) in
                     Features)
            {
                clone.Features.Add(code, value.Clone());
            }
            return clone;
        }
    }

    private sealed class MutableFeature
    {
        public int LikeCount { get; set; }
        public double LikeSum { get; set; }
        public int DislikeCount { get; set; }
        public double DislikeSum { get; set; }

        public MutableFeature Clone() => new()
        {
            LikeCount = LikeCount,
            LikeSum = LikeSum,
            DislikeCount = DislikeCount,
            DislikeSum = DislikeSum,
        };
    }

    private sealed class StoreDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string FeatureSchemaVersion { get; set; } = string.Empty;
        public string PolicyVersion { get; set; } = string.Empty;
        public int LikeCount { get; set; }
        public int NeutralCount { get; set; }
        public int DislikeCount { get; set; }
        public FeatureDocument[]? Features { get; set; }
    }

    private sealed class FeatureDocument
    {
        public string Code { get; set; } = string.Empty;
        public int LikeCount { get; set; }
        public double LikeSum { get; set; }
        public int DislikeCount { get; set; }
        public double DislikeSum { get; set; }
    }
}
