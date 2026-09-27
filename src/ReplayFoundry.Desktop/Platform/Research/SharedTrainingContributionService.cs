using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Research;

/// <summary>Separate opt-in outbox. Neither local-learning consent nor report consent enables sharing.</summary>
public sealed class SharedTrainingContributionService : ISharedTrainingContributions
{
    public const string NoticeVersion = "shared-writer-training-1";
    public static SharedTrainingContributionService Current { get; } = new();
    private readonly string _root;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly Func<string, string, bool, CancellationToken, Task> _send;
    private CancellationTokenSource _cancel = new();
    private State _state;
    private bool _deleting;
    private sealed record State(bool Enabled, string Notice, DateTimeOffset? EnabledAt, string? ProtectedToken);

    public SharedTrainingContributionService(string? root = null,
        Func<string, string, bool, CancellationToken, Task>? send = null)
    {
        _root = ReplayFoundryLocalDataPaths.Resolve(root, "SharedTraining");
        _send = send ?? HttpsTrainingContributionTransport.SendAsync;
        string path = Path.Combine(_root, "consent.json");
        try { _state = File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path))! : new(false, NoticeVersion, null, null); }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        { _state = new(false, NoticeVersion, null, null); }
        _state ??= new(false, NoticeVersion, null, null);
    }
    public bool IsEnabled { get { lock (_gate) return _state.Enabled && _state.Notice == NoticeVersion && _state.EnabledAt.HasValue && _state.ProtectedToken is not null; } }
    public string Status => IsEnabled ? "Sharing future writing examples · text and metadata only" : "Shared training is off";

    public void Enable()
    {
        lock (_gate)
        {
            if (IsEnabled) return;
            if (_deleting) throw new InvalidOperationException("Wait for contribution deletion to finish.");
            string protectedToken = _state.ProtectedToken ?? Convert.ToBase64String(ProtectedData.Protect(
                RandomNumberGenerator.GetBytes(32), null, DataProtectionScope.CurrentUser));
            Save(new(true, NoticeVersion, DateTimeOffset.UtcNow, protectedToken));
            _cancel.Dispose(); _cancel = new();
        }
    }
    public void Disable()
    {
        lock (_gate)
        {
            Save(_state with { Enabled = false });
            _cancel.Cancel();
            string queue = Path.Combine(_root, "pending");
            if (Directory.Exists(queue)) foreach (string file in Directory.EnumerateFiles(queue, "*.json")) File.Delete(file);
        }
    }
    private void Save(State state)
    {
        Directory.CreateDirectory(_root);
        string target = Path.Combine(_root, "consent.json"), temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, target, true);
        _state = state;
    }
    private string Token() => Convert.ToHexString(ProtectedData.Unprotect(Convert.FromBase64String(_state.ProtectedToken!), null,
        DataProtectionScope.CurrentUser)).ToLowerInvariant();

    public void Capture(JsonObject captured, ClipEditorialContext context, JsonObject? example = null)
    {
        lock (_gate)
        {
            if (!IsEnabled) return;
            // Historical local examples are not swept into the upload queue.
            if (captured["capturedAtUtc"] is not JsonValue timestamp ||
                !DateTimeOffset.TryParse(timestamp.GetValue<string>(), out var at) || at < _state.EnabledAt) return;
            string token = Token();
            var evidence = SharedTrainingEvidence.Project(captured);
            var payload = new JsonObject
            {
                ["schema"] = "shared-writer-example-1", ["event"] = example is null ? "Generated" : "EditedOrApproved",
                ["clientVersion"] = typeof(SharedTrainingContributionService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown",
                ["consentVersion"] = NoticeVersion, ["consentAtUtc"] = _state.EnabledAt!.Value.ToString("O"),
                ["game"] = new JsonObject { ["name"] = context.GameContext.GameName, ["source"] = context.GameContext.Source.ToString() },
                ["recordingGroup"] = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(captured["sourceGroup"]!.GetValue<string>()))).ToLowerInvariant(),
                ["startSeconds"] = context.SourceStart.TotalSeconds, ["endSeconds"] = context.SourceEnd.TotalSeconds,
                ["promptSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(captured["prompt"]![0]!["content"]!.GetValue<string>()))).ToLowerInvariant(),
                ["before"] = SharedTrainingEvidence.Copy(example?["rejected"]),
                ["after"] = SharedTrainingEvidence.Copy(example?["chosen"] ?? captured["generated"]),
                ["feedback"] = example?["feedback"]?.DeepClone(),
                ["edits"] = example?["edits"]?.DeepClone(), ["evidence"] = evidence,
                ["humanWordingFeedback"] = example is not null,
            };
            // Parent hashes can correlate local files; the shared pair itself is sufficient.
            if (payload["edits"] is JsonObject edits) edits["parentExampleId"] = null;
            string content = payload.ToJsonString();
            if (Encoding.UTF8.GetByteCount(content) > 65_000) return;
            payload["id"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            string queue = Path.Combine(_root, "pending"); Directory.CreateDirectory(queue);
            if (Directory.EnumerateFiles(queue, "*.json").Take(2000).Count() >= 2000) return;
            string path = Path.Combine(queue, payload["id"]!.GetValue<string>() + ".json");
            File.WriteAllText(path + ".tmp", payload.ToJsonString()); File.Move(path + ".tmp", path, true);
        }
        _ = Task.Run(SendPendingAsync);
    }

    public async Task SendPendingAsync()
    {
        if (!await _sending.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            string[] paths;
            lock (_gate)
            {
                if (!IsEnabled) return;
                string queue = Path.Combine(_root, "pending");
                paths = Directory.Exists(queue) ? Directory.GetFiles(queue, "*.json").Take(64).ToArray() : [];
            }
            foreach (string path in paths)
            {
                string token, json; CancellationToken cancel;
                lock (_gate)
                {
                    if (!IsEnabled) return;
                    token = Token(); cancel = _cancel.Token;
                    if (!File.Exists(path) || new FileInfo(path).Length > 65_536) continue;
                    json = File.ReadAllText(path);
                }
                await _send(token, json, false, cancel).ConfigureAwait(false);
                lock (_gate) { if (File.Exists(path)) File.Delete(path); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException or CryptographicException or JsonException)
        { /* Keep pending records for an explicit retry or the next contribution. */ }
        finally { _sending.Release(); }
    }

    public async Task DeleteSharedAsync()
    {
        lock (_gate) { _deleting = true; }
        try { Disable(); }
        catch { lock (_gate) _deleting = false; throw; }
        await _sending.WaitAsync().ConfigureAwait(false);
        try
        {
            string? token; lock (_gate) token = _state.ProtectedToken is null ? null : Token();
            if (token is null) return;
            await _send(token, "", true, CancellationToken.None).ConfigureAwait(false);
            lock (_gate) Save(new(false, NoticeVersion, null, null));
        }
        finally { lock (_gate) _deleting = false; _sending.Release(); }
    }
}
