using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed record Qwen3VlGroundingPacketReceipt(
    string RequestSha256, int SourceAttempt, string CandidateId, string FactWitness);

/// <summary>Bounded operation-local bytes, promoted only after full provider validation.</summary>
internal sealed class Qwen3VlGroundingPacketHandoff : IDisposable
{
    internal const string EnvironmentFlag = "REPLAYFOUNDRY_GROUNDING_HANDOFF";
    internal const string ImportHashFlag = "REPLAYFOUNDRY_GROUNDING_IMPORT_SHA256";
    internal const string Schema = "grounded-editorial-packet-handoff-1.0";
    internal const string ImportFile = "grounding-import.json";
    internal const string ExportFile = "grounding-export.json";
    internal const int MaximumEntries = 30;
    internal const int MaximumEntryBytes = 262_144;
    internal const int MaximumFileBytes = 8_388_608;
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    internal Dictionary<string, Qwen3VlGroundingPacketReceipt> Receipts { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    internal Qwen3VlGroundingPacketHandoff Copy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var copy = new Qwen3VlGroundingPacketHandoff();
        foreach (var entry in _entries) copy._entries.Add(entry.Key, entry.Value);
        foreach (var receipt in Receipts) copy.Receipts.Add(receipt.Key, receipt.Value);
        return copy;
    }

    internal void ReplaceWith(Qwen3VlGroundingPacketHandoff validated)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _entries.Clear();
        Receipts.Clear();
        foreach (var entry in validated._entries) _entries.Add(entry.Key, entry.Value);
        foreach (string raw in validated._entries.Values)
        {
            using JsonDocument entry = JsonDocument.Parse(raw);
            string hash = Text(entry.RootElement, "factSha256");
            if (validated.Receipts.TryGetValue(hash, out var receipt)) Receipts[hash] = receipt;
        }
    }

    internal IReadOnlyDictionary<string, string> Prepare(
        string workspace, IReadOnlyDictionary<string, string> environment)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase)
        {
            [EnvironmentFlag] = "1",
        };
        result.Remove(ImportHashFlag);
        if (_entries.Count == 0) return result;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", Schema);
            writer.WriteStartArray("entries");
            foreach (string entry in _entries.Values) writer.WriteRawValue(entry);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        byte[] bytes = buffer.ToArray();
        if (bytes.Length > MaximumFileBytes) return result;
        string path = Path.Combine(workspace, ImportFile);
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            file.Write(bytes);
        result[ImportHashFlag] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return result;
    }

    internal async Task RetainExportAsync(string workspace, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string path = Path.Combine(workspace, ExportFile);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > MaximumFileBytes ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0) return;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumFileBytes) return;
            byte[] bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            Exact(root, "schemaVersion", "entries");
            if (Text(root, "schemaVersion") != Schema) return;
            JsonElement entries = root.GetProperty("entries");
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > MaximumEntries) return;
            var retained = new Dictionary<string, string>(_entries, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string raw = entry.GetRawText();
                if (Encoding.UTF8.GetByteCount(raw) > MaximumEntryBytes) return;
                Exact(entry, "schemaVersion", "requestIdentitySha256", "canonicalRequestIdentity",
                    "factSha256", "canonicalFacts", "sourceAttempt", "groundingPassCount",
                    "groundingElapsedSeconds", "runtimeIdentitySha256", "editorialBriefSha256");
                string requestHash = Sha(entry, "requestIdentitySha256");
                if (!seen.Add(requestHash)) return;
                string factHash = Sha(entry, "factSha256");
                string schema = Text(entry, "schemaVersion");
                string canonicalIdentity = Text(entry, "canonicalRequestIdentity");
                string canonicalFacts = Text(entry, "canonicalFacts");
                string actualFactHash = Hash("{\"facts\":" + canonicalFacts +
                    ",\"requestIdentitySha256\":\"" + requestHash + "\",\"schemaVersion\":\"" + schema + "\"}");
                _ = Sha(entry, "runtimeIdentitySha256");
                _ = Sha(entry, "editorialBriefSha256");
                int sourceAttempt = entry.GetProperty("sourceAttempt").GetInt32();
                int passes = entry.GetProperty("groundingPassCount").GetInt32();
                double seconds = entry.GetProperty("groundingElapsedSeconds").GetDouble();
                if (schema != Qwen3VlGroundedMetadataGenerator.GroundingPacketSchemaVersion ||
                    sourceAttempt is < 0 or > 100 || passes is < 1 or > 32 ||
                    !double.IsFinite(seconds) || seconds is < 0 or > 86_400 ||
                    Hash(canonicalIdentity) != requestHash || actualFactHash != factHash) return;
                using JsonDocument identity = JsonDocument.Parse(canonicalIdentity);
                using JsonDocument facts = JsonDocument.Parse(canonicalFacts);
                if (facts.RootElement.ValueKind != JsonValueKind.Object) return;
                // Only a receipt produced by the complete strict output parser authorizes import.
                if (!Receipts.TryGetValue(factHash, out Qwen3VlGroundingPacketReceipt? receipt) ||
                    receipt.RequestSha256 != requestHash || receipt.SourceAttempt != sourceAttempt ||
                    Text(identity.RootElement, "candidateId") != receipt.CandidateId) continue;
                if (retained.ContainsKey(requestHash) || retained.Count < MaximumEntries)
                    retained[requestHash] = raw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (retained.Values.Sum(static value => Encoding.UTF8.GetByteCount(value)) >
                MaximumFileBytes - 4096) return;
            _entries.Clear();
            foreach (var entry in retained) _entries.Add(entry.Key, entry.Value);
            // Do not accumulate obsolete receipts across generations of the same input.
            var retainedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in _entries.Values)
            {
                using JsonDocument entry = JsonDocument.Parse(raw);
                retainedHashes.Add(Text(entry.RootElement, "factSha256"));
            }
            foreach (string hash in Receipts.Keys.Where(key => !retainedHashes.Contains(key)).ToArray())
                Receipts.Remove(hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        {
            // Reuse is optional. Missing or damaged handoff data cannot invalidate valid copy.
        }
    }

    internal static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()
        ?? throw new JsonException("Packet text is absent.");

    private static string Sha(JsonElement value, string name)
    {
        string result = Text(value, name);
        if (result.Length != 64 || result.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new JsonException("Packet identity is invalid.");
        return result;
    }

    private static void Exact(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(fields.Order(StringComparer.Ordinal)))
            throw new JsonException("Packet fields are invalid.");
    }

    public void Dispose()
    {
        _disposed = true;
        _entries.Clear();
        Receipts.Clear();
    }
}
