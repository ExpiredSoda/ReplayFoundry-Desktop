using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonEditorialMetadataPreferenceStore :
    IEditorialMetadataPreferenceStore
{
    public const string SchemaVersion =
        "editorial-metadata-preference-store-1.0";

    private const double Tolerance = 1e-9;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private MutableStore _store;
    private EditorialMetadataPreferenceProfile _current;

    public JsonEditorialMetadataPreferenceStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "editorial-metadata-preferences.json");
        _store = Load(_path);
        _current = BuildProfile(_store);
    }

    public EditorialMetadataPreferenceProfile Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public EditorialMetadataPreferenceProfile Update(
        EditorialMetadataPreferenceEvidence? previous,
        EditorialMetadataPreferenceEvidence current)
    {
        ArgumentNullException.ThrowIfNull(current);
        lock (_gate)
        {
            if (ReferenceEquals(previous, current))
            {
                return _current;
            }

            MutableStore updated = _store.Clone();
            if (previous is not null)
            {
                Adjust(updated, previous, -1);
            }
            Adjust(updated, current, 1);
            Canonicalize(updated);
            Validate(updated);
            WriteAtomic(updated);
            _store = updated;
            _current = BuildProfile(updated);
            return _current;
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
            _current = EditorialMetadataPreferenceProfile.Empty;
        }
    }

    private static void Adjust(
        MutableStore store,
        EditorialMetadataPreferenceEvidence evidence,
        int direction)
    {
        store.EvidenceCounts.TryGetValue(
            evidence.Kind,
            out int evidenceCount);
        store.EvidenceCounts[evidence.Kind] = evidenceCount + direction;

        foreach (EditorialMetadataPreferenceObservation observation in
                 evidence.Observations)
        {
            double weightedDirection = direction * observation.Weight;
            switch (observation.Outcome)
            {
                case EditorialMetadataPreferenceOutcome.Accepted:
                    store.AcceptedWeight += weightedDirection;
                    break;
                case EditorialMetadataPreferenceOutcome.Neutral:
                    store.NeutralWeight += weightedDirection;
                    continue;
                case EditorialMetadataPreferenceOutcome.Rejected:
                    store.RejectedWeight += weightedDirection;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(evidence),
                        observation.Outcome,
                        "Editorial metadata preference outcomes must be supported.");
            }

            foreach (EditorialMetadataPreferenceFeature feature in
                     observation.Features.Features)
            {
                if (!store.Features.TryGetValue(
                        feature.Code,
                        out MutableFeature? aggregate))
                {
                    aggregate = new MutableFeature();
                    store.Features.Add(feature.Code, aggregate);
                }

                if (observation.Outcome ==
                    EditorialMetadataPreferenceOutcome.Accepted)
                {
                    aggregate.AcceptedWeight += weightedDirection;
                    aggregate.AcceptedSum +=
                        weightedDirection * feature.NormalizedValue;
                }
                else
                {
                    aggregate.RejectedWeight += weightedDirection;
                    aggregate.RejectedSum +=
                        weightedDirection * feature.NormalizedValue;
                }
            }
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
                    EditorialMetadataPreferenceFeatureVector.SchemaVersion ||
                document.EvidenceContractVersion !=
                    EditorialMetadataPreferenceEvidence.ContractVersion ||
                document.ProfilePolicyVersion !=
                    EditorialMetadataPreferenceProfile.PolicyVersion)
            {
                throw new InvalidDataException(
                    "The local editorial metadata preference schema is unsupported.");
            }

            var result = new MutableStore
            {
                AcceptedWeight = document.AcceptedWeight,
                NeutralWeight = document.NeutralWeight,
                RejectedWeight = document.RejectedWeight,
            };
            foreach (EvidenceDocument evidence in document.Evidence ?? [])
            {
                if (!Enum.TryParse(
                        evidence.Kind,
                        ignoreCase: false,
                        out EditorialMetadataPreferenceEvidenceKind kind) ||
                    result.EvidenceCounts.ContainsKey(kind))
                {
                    throw new InvalidDataException(
                        "The local editorial metadata preference evidence list is invalid.");
                }
                result.EvidenceCounts.Add(kind, evidence.Count);
            }
            foreach (FeatureDocument feature in document.Features ?? [])
            {
                if (!Enum.TryParse(
                        feature.Code,
                        ignoreCase: false,
                        out EditorialMetadataPreferenceFeatureCode code) ||
                    result.Features.ContainsKey(code))
                {
                    throw new InvalidDataException(
                        "The local editorial metadata preference feature list is invalid.");
                }
                result.Features.Add(
                    code,
                    new MutableFeature
                    {
                        AcceptedWeight = feature.AcceptedWeight,
                        AcceptedSum = feature.AcceptedSum,
                        RejectedWeight = feature.RejectedWeight,
                        RejectedSum = feature.RejectedSum,
                    });
            }
            Canonicalize(result);
            Validate(result);
            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The local editorial metadata preference file is not valid JSON.",
                exception);
        }
    }

    private void WriteAtomic(MutableStore value)
    {
        var document = new StoreDocument
        {
            SchemaVersion = SchemaVersion,
            FeatureSchemaVersion =
                EditorialMetadataPreferenceFeatureVector.SchemaVersion,
            EvidenceContractVersion =
                EditorialMetadataPreferenceEvidence.ContractVersion,
            ProfilePolicyVersion =
                EditorialMetadataPreferenceProfile.PolicyVersion,
            AcceptedWeight = value.AcceptedWeight,
            NeutralWeight = value.NeutralWeight,
            RejectedWeight = value.RejectedWeight,
            Evidence = value.EvidenceCounts
                .OrderBy(static pair => pair.Key)
                .Select(static pair => new EvidenceDocument
                {
                    Kind = pair.Key.ToString(),
                    Count = pair.Value,
                })
                .ToArray(),
            Features = value.Features
                .OrderBy(static pair => pair.Key)
                .Select(static pair => new FeatureDocument
                {
                    Code = pair.Key.ToString(),
                    AcceptedWeight = pair.Value.AcceptedWeight,
                    AcceptedSum = pair.Value.AcceptedSum,
                    RejectedWeight = pair.Value.RejectedWeight,
                    RejectedSum = pair.Value.RejectedSum,
                })
                .ToArray(),
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

    private static void Canonicalize(MutableStore value)
    {
        value.AcceptedWeight = NearZero(value.AcceptedWeight);
        value.NeutralWeight = NearZero(value.NeutralWeight);
        value.RejectedWeight = NearZero(value.RejectedWeight);

        foreach (EditorialMetadataPreferenceEvidenceKind empty in
                 value.EvidenceCounts
                     .Where(static pair => pair.Value == 0)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            value.EvidenceCounts.Remove(empty);
        }
        foreach (MutableFeature feature in value.Features.Values)
        {
            feature.AcceptedWeight = NearZero(feature.AcceptedWeight);
            feature.AcceptedSum = NearZero(feature.AcceptedSum);
            feature.RejectedWeight = NearZero(feature.RejectedWeight);
            feature.RejectedSum = NearZero(feature.RejectedSum);
        }
        foreach (EditorialMetadataPreferenceFeatureCode empty in
                 value.Features
                     .Where(static pair =>
                         pair.Value.AcceptedWeight == 0 &&
                         pair.Value.AcceptedSum == 0 &&
                         pair.Value.RejectedWeight == 0 &&
                         pair.Value.RejectedSum == 0)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            value.Features.Remove(empty);
        }
    }

    private static void Validate(MutableStore value)
    {
        if (!IsWeight(value.AcceptedWeight) ||
            !IsWeight(value.NeutralWeight) ||
            !IsWeight(value.RejectedWeight) ||
            value.EvidenceCounts.Any(static pair =>
                !Enum.IsDefined(pair.Key) || pair.Value <= 0) ||
            value.EvidenceCounts.Values.Sum(
                static count => (long)count) > int.MaxValue ||
            !EvidenceWeightsAreConsistent(value) ||
            value.Features.Any(pair =>
                !Enum.IsDefined(pair.Key) ||
                !IsAggregate(
                    pair.Value.AcceptedWeight,
                    pair.Value.AcceptedSum,
                    value.AcceptedWeight) ||
                !IsAggregate(
                    pair.Value.RejectedWeight,
                    pair.Value.RejectedSum,
                    value.RejectedWeight)))
        {
            throw new InvalidDataException(
                "The local editorial metadata preference aggregates are inconsistent.");
        }
    }

    private static bool EvidenceWeightsAreConsistent(MutableStore value)
    {
        int unchanged = value.EvidenceCounts.GetValueOrDefault(
            EditorialMetadataPreferenceEvidenceKind.UnchangedPublish);
        int humanCorrections = value.EvidenceCounts.GetValueOrDefault(
            EditorialMetadataPreferenceEvidenceKind.HumanCorrection);
        int explicitRatings = value.EvidenceCounts.GetValueOrDefault(
            EditorialMetadataPreferenceEvidenceKind.ExplicitWordingRating);
        int youtubeCorrections = value.EvidenceCounts.GetValueOrDefault(
            EditorialMetadataPreferenceEvidenceKind
                .ConfirmedYouTubeCorrection);
        double correctionCount =
            (double)humanCorrections + youtubeCorrections;
        double explicitAccepted = value.AcceptedWeight -
            correctionCount -
            unchanged *
                EditorialMetadataPreferenceEvidence
                    .WeakUnchangedPublishWeight;
        double explicitRejected =
            value.RejectedWeight - correctionCount;
        double explicitNeutral = value.NeutralWeight;

        return explicitAccepted >= -Tolerance &&
            explicitRejected >= -Tolerance &&
            explicitNeutral >= -Tolerance &&
            Math.Abs(
                explicitAccepted +
                explicitRejected +
                explicitNeutral -
                explicitRatings *
                    EditorialMetadataPreferenceEvidence
                        .StrongEvidenceWeight) <= Tolerance;
    }

    private static bool IsWeight(double value) =>
        double.IsFinite(value) && value >= 0;

    private static bool IsAggregate(
        double weight,
        double sum,
        double totalWeight) =>
        double.IsFinite(weight) &&
        double.IsFinite(sum) &&
        weight >= 0 &&
        sum >= 0 &&
        weight <= totalWeight + Tolerance &&
        sum <= weight + Tolerance;

    private static double NearZero(double value) =>
        Math.Abs(value) <= Tolerance ? 0 : value;

    private static EditorialMetadataPreferenceProfile BuildProfile(
        MutableStore value) =>
        new(
            value.AcceptedWeight,
            value.NeutralWeight,
            value.RejectedWeight,
            value.EvidenceCounts.Select(static pair =>
                new EditorialMetadataPreferenceEvidenceStatistics(
                    pair.Key,
                    pair.Value)),
            value.Features.Select(static pair =>
                new EditorialMetadataPreferenceFeatureStatistics(
                    pair.Key,
                    pair.Value.AcceptedWeight,
                    Math.Clamp(
                        pair.Value.AcceptedSum,
                        0,
                        pair.Value.AcceptedWeight),
                    pair.Value.RejectedWeight,
                    Math.Clamp(
                        pair.Value.RejectedSum,
                        0,
                        pair.Value.RejectedWeight))));

    private sealed class MutableStore
    {
        public double AcceptedWeight { get; set; }
        public double NeutralWeight { get; set; }
        public double RejectedWeight { get; set; }
        public Dictionary<EditorialMetadataPreferenceEvidenceKind, int>
            EvidenceCounts
        { get; } = [];
        public Dictionary<EditorialMetadataPreferenceFeatureCode,
            MutableFeature> Features
        { get; } = [];

        public MutableStore Clone()
        {
            var clone = new MutableStore
            {
                AcceptedWeight = AcceptedWeight,
                NeutralWeight = NeutralWeight,
                RejectedWeight = RejectedWeight,
            };
            foreach ((EditorialMetadataPreferenceEvidenceKind kind, int count)
                     in EvidenceCounts)
            {
                clone.EvidenceCounts.Add(kind, count);
            }
            foreach ((EditorialMetadataPreferenceFeatureCode code,
                     MutableFeature feature) in Features)
            {
                clone.Features.Add(code, feature.Clone());
            }
            return clone;
        }
    }

    private sealed class MutableFeature
    {
        public double AcceptedWeight { get; set; }
        public double AcceptedSum { get; set; }
        public double RejectedWeight { get; set; }
        public double RejectedSum { get; set; }

        public MutableFeature Clone() => new()
        {
            AcceptedWeight = AcceptedWeight,
            AcceptedSum = AcceptedSum,
            RejectedWeight = RejectedWeight,
            RejectedSum = RejectedSum,
        };
    }

    private sealed class StoreDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string FeatureSchemaVersion { get; set; } = string.Empty;
        public string EvidenceContractVersion { get; set; } = string.Empty;
        public string ProfilePolicyVersion { get; set; } = string.Empty;
        public double AcceptedWeight { get; set; }
        public double NeutralWeight { get; set; }
        public double RejectedWeight { get; set; }
        public EvidenceDocument[]? Evidence { get; set; }
        public FeatureDocument[]? Features { get; set; }
    }

    private sealed class EvidenceDocument
    {
        public string Kind { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class FeatureDocument
    {
        public string Code { get; set; } = string.Empty;
        public double AcceptedWeight { get; set; }
        public double AcceptedSum { get; set; }
        public double RejectedWeight { get; set; }
        public double RejectedSum { get; set; }
    }
}
