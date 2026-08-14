using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonStudioCandidateDecisionStore :
    IStudioCandidateDecisionStore
{
    private const string SchemaVersion =
        "replayfoundry-studio-candidate-decisions-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, StudioCandidateDecision> _values;

    public JsonStudioCandidateDecisionStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "studio-candidate-decisions.json");
        _values = Load(_path);
    }

    public IReadOnlyList<StudioCandidateDecision> Current
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

    public StudioCandidateDecision? Find(string candidateId)
    {
        lock (_gate)
        {
            return _values.GetValueOrDefault(candidateId);
        }
    }

    public void Upsert(StudioCandidateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        lock (_gate)
        {
            var updated = new Dictionary<string, StudioCandidateDecision>(
                _values,
                StringComparer.Ordinal)
            {
                [decision.CandidateId] = decision,
            };
            Write(updated.Values);
            _values = updated;
        }
    }

    private static Dictionary<string, StudioCandidateDecision> Load(
        string path)
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path),
                JsonOptions);
            if (document?.SchemaVersion != SchemaVersion ||
                document.Entries is null)
            {
                throw new InvalidDataException(
                    "The Studio candidate-decision schema is unsupported.");
            }
            return document.Entries
                .Select(static entry => new StudioCandidateDecision(
                    entry.CandidateId,
                    entry.ProjectId,
                    entry.SourceIdentity,
                    TimeSpan.FromTicks(entry.SourceStartTicks),
                    TimeSpan.FromTicks(entry.SourceEndTicks),
                    Enum.Parse<GenerationOutputAssetDisposition>(entry.Disposition),
                    entry.Rating is null
                        ? null
                        : Enum.Parse<StudioClipPreferenceRating>(entry.Rating),
                    entry.RecordedAtUtc))
                .ToDictionary(
                    static value => value.CandidateId,
                    StringComparer.Ordinal);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                "The Studio candidate-decision file is invalid.",
                exception);
        }
    }

    private void Write(IEnumerable<StudioCandidateDecision> values)
    {
        var document = new Document
        {
            SchemaVersion = SchemaVersion,
            Entries = values
                .OrderByDescending(static value => value.RecordedAtUtc)
                .Select(static value => new Entry
                {
                    CandidateId = value.CandidateId,
                    ProjectId = value.ProjectId,
                    SourceIdentity = value.SourceIdentity,
                    SourceStartTicks = value.SourceStart.Ticks,
                    SourceEndTicks = value.SourceEnd.Ticks,
                    Disposition = value.Disposition.ToString(),
                    Rating = value.Rating?.ToString(),
                    RecordedAtUtc = value.RecordedAtUtc,
                }).ToArray(),
        };
        AtomicJsonFile.Write(_path, document, JsonOptions);
    }

    private sealed class Document
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public Entry[]? Entries { get; set; }
    }

    private sealed class Entry
    {
        public string CandidateId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string SourceIdentity { get; set; } = string.Empty;
        public long SourceStartTicks { get; set; }
        public long SourceEndTicks { get; set; }
        public string Disposition { get; set; } = string.Empty;
        public string? Rating { get; set; }
        public DateTimeOffset RecordedAtUtc { get; set; }
    }
}
