using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;

namespace ReplayFoundry.Desktop.Platform.Storage;

public sealed class JsonStudioHiddenMomentDecisionStore :
    IStudioHiddenMomentDecisionStore
{
    private const string SchemaVersion =
        "replayfoundry-hidden-moment-decisions-1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, StudioHiddenMomentDecision> _values;

    public JsonStudioHiddenMomentDecisionStore(string? path = null)
    {
        _path = ReplayFoundryLocalDataPaths.Resolve(
            path,
            "hidden-moment-decisions.json");
        _values = Load(_path);
    }

    public IReadOnlyList<StudioHiddenMomentDecision> Current
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

    public StudioHiddenMomentDecision? Find(
        string projectId,
        string candidateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        lock (_gate)
        {
            return _values.GetValueOrDefault(Key(projectId, candidateId));
        }
    }

    public void Upsert(StudioHiddenMomentDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        lock (_gate)
        {
            var updated = new Dictionary<string, StudioHiddenMomentDecision>(
                _values,
                StringComparer.Ordinal)
            {
                [Key(decision.ProjectId, decision.CandidateId)] = decision,
            };
            Write(updated.Values);
            _values = updated;
        }
    }

    public void ClearSkippedForProject(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        lock (_gate)
        {
            var updated = _values
                .Where(pair =>
                    !pair.Value.ProjectId.Equals(
                        projectId,
                        StringComparison.Ordinal) ||
                    pair.Value.Decision !=
                        StudioHiddenMomentReviewDecision.SkippedForProject)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value,
                    StringComparer.Ordinal);
            Write(updated.Values);
            _values = updated;
        }
    }

    private static string Key(string projectId, string candidateId) =>
        projectId.Trim() + "\u001f" + candidateId.Trim();

    private static Dictionary<string, StudioHiddenMomentDecision> Load(
        string path)
    {
        if (!File.Exists(path))
        {
            return new(StringComparer.Ordinal);
        }
        try
        {
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path),
                JsonOptions);
            if (document?.SchemaVersion != SchemaVersion ||
                document.Entries is null)
            {
                throw new InvalidDataException(
                    "The Hidden Moments decision schema is unsupported.");
            }
            return document.Entries
                .Select(static entry => new StudioHiddenMomentDecision(
                    entry.ProjectId,
                    entry.CandidateId,
                    entry.SourceIdentity,
                    TimeSpan.FromTicks(entry.SourceStartTicks),
                    TimeSpan.FromTicks(entry.SourceEndTicks),
                    Enum.Parse<StudioHiddenMomentReviewDecision>(entry.Decision),
                    entry.RecordedAtUtc))
                .ToDictionary(
                    value => Key(value.ProjectId, value.CandidateId),
                    StringComparer.Ordinal);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                "The Hidden Moments decision file is invalid.",
                exception);
        }
    }

    private void Write(IEnumerable<StudioHiddenMomentDecision> values)
    {
        var document = new Document
        {
            SchemaVersion = SchemaVersion,
            Entries = values
                .OrderByDescending(static value => value.RecordedAtUtc)
                .Select(static value => new Entry
                {
                    ProjectId = value.ProjectId,
                    CandidateId = value.CandidateId,
                    SourceIdentity = value.SourceIdentity,
                    SourceStartTicks = value.SourceStart.Ticks,
                    SourceEndTicks = value.SourceEnd.Ticks,
                    Decision = value.Decision.ToString(),
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
        public string ProjectId { get; set; } = string.Empty;
        public string CandidateId { get; set; } = string.Empty;
        public string SourceIdentity { get; set; } = string.Empty;
        public long SourceStartTicks { get; set; }
        public long SourceEndTicks { get; set; }
        public string Decision { get; set; } = string.Empty;
        public DateTimeOffset RecordedAtUtc { get; set; }
    }
}
