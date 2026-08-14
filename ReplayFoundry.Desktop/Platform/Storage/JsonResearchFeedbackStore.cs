using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonResearchFeedbackStore : IResearchFeedbackStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, ResearchFeedbackRecord> _values;

    public JsonResearchFeedbackStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "research-feedback.json");
        _values = Load(_path);
    }

    public IReadOnlyList<ResearchFeedbackRecord> Current
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(_values.Values
                    .OrderByDescending(static value => value.RecordedAtUtc)
                    .ToArray());
            }
        }
    }

    public void Upsert(ResearchFeedbackRecord value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            var updated = new Dictionary<string, ResearchFeedbackRecord>(
                _values, StringComparer.Ordinal)
            {
                [value.Key] = value,
            };
            Write(updated.Values);
            _values = updated;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Write([]);
            _values = new(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, ResearchFeedbackRecord> Load(string path)
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path), JsonOptions);
            if (document?.SchemaVersion != ResearchFeedbackRecord.SchemaVersion ||
                document.Entries is null)
            {
                throw new InvalidDataException(
                    "The research-feedback schema is unsupported.");
            }
            return document.Entries.Select(static entry =>
                    new ResearchFeedbackRecord(
                        entry.CandidateIdentity,
                        entry.SourceIdentity,
                        Enum.Parse<ResearchFeedbackChannel>(entry.Channel),
                        Enum.Parse<ResearchFeedbackValue>(entry.Value),
                        entry.Features.Select(static feature =>
                            new ClipPreferenceFeature(
                                Enum.Parse<ClipPreferenceFeatureCode>(feature.Code),
                                feature.Value)),
                        entry.RecordedAtUtc))
                .ToDictionary(static value => value.Key, StringComparer.Ordinal);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                "The research-feedback file is invalid.", exception);
        }
    }

    private void Write(IEnumerable<ResearchFeedbackRecord> values) =>
        AtomicJsonFile.Write(_path, new Document
        {
            SchemaVersion = ResearchFeedbackRecord.SchemaVersion,
            Entries = values
                .OrderBy(static value => value.Key, StringComparer.Ordinal)
                .Select(static value => new Entry
                {
                    CandidateIdentity = value.CandidateIdentity,
                    SourceIdentity = value.SourceIdentity,
                    Channel = value.Channel.ToString(),
                    Value = value.Value.ToString(),
                    Features = value.Features.Select(static feature =>
                        new Feature
                        {
                            Code = feature.Code.ToString(),
                            Value = feature.NormalizedValue,
                        }).ToArray(),
                    RecordedAtUtc = value.RecordedAtUtc,
                }).ToArray(),
        }, JsonOptions);

    private sealed class Document
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public Entry[]? Entries { get; set; }
    }

    private sealed class Entry
    {
        public string CandidateIdentity { get; set; } = string.Empty;
        public string SourceIdentity { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public Feature[] Features { get; set; } = [];
        public DateTimeOffset RecordedAtUtc { get; set; }
    }

    private sealed class Feature
    {
        public string Code { get; set; } = string.Empty;
        public double Value { get; set; }
    }
}
