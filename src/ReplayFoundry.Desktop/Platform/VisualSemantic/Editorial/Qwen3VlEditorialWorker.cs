using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

/// <summary>One local model process with serialized requests and bounded idle residency.</summary>
internal sealed class Qwen3VlEditorialWorker : IProcessRunner, IDisposable
{
    private const string Schema = "replayfoundry-editorial-worker-1";
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly HashSet<string> RequestEnvironment =
        ["REPLAYFOUNDRY_GROUNDING_HANDOFF", "REPLAYFOUNDRY_GROUNDING_IMPORT_SHA256", "REPLAYFOUNDRY_WRITER_CAPTURE", "REPLAYFOUNDRY_WRITER_ROOT"];
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly object _sync = new();
    private readonly StringBuilder _nativeDiagnostics = new();
    private readonly Timer _idle;
    private Process? _process;
    private WindowsProcessJob? _job;
    private Task? _stderrDrain;
    private string? _identity;
    private bool _active;
    private bool _disposed;
    private bool _resident;

    internal Qwen3VlEditorialWorker() => _idle = new(_ => ReleaseIdle(), null,
        Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    internal bool HasResidentModel { get { lock (_sync) return _resident && _process is { HasExited: false }; } }

    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
    {
        await _requests.WaitAsync(cancellationToken).ConfigureAwait(false);
        var elapsed = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            Process process;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _active = true;
                _idle.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                process = GetOrStart(request);
                _nativeDiagnostics.Clear();
            }
            string id = Guid.NewGuid().ToString("N");
            string packet = JsonSerializer.Serialize(new
            {
                schema = Schema, id,
                arguments = request.Arguments.Skip(2).ToArray(),
                environment = request.EnvironmentVariables.Where(pair => RequestEnvironment.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
            });
            if (Encoding.UTF8.GetByteCount(packet) > 131_070)
                throw new ProcessExecutionException("The editorial worker request exceeds its size limit.");
            await process.StandardInput.WriteLineAsync(packet.AsMemory(), linked.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(linked.Token).ConfigureAwait(false);
            string response = await ReadResponseAsync(process.StandardOutput, linked.Token).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(response);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4 ||
                root.GetProperty("schema").GetString() != Schema || root.GetProperty("id").GetString() != id)
                throw new ProcessExecutionException("The editorial worker returned a mismatched response.");
            int code = root.GetProperty("exitCode").GetInt32();
            string diagnostics = root.GetProperty("diagnostics").GetString() ?? "";
            if (diagnostics.Length > request.MaxStandardErrorCharacters)
                throw new ProcessExecutionException("Editorial diagnostics exceeded the configured output limit.");
            lock (_sync)
            {
                diagnostics += _nativeDiagnostics.ToString();
                _resident = code == 0;
                if (code != 0) Stop();
            }
            return new(code, "", diagnostics, elapsed.Elapsed);
        }
        catch (OperationCanceledException)
        {
            lock (_sync) Stop();
            cancellationToken.ThrowIfCancellationRequested();
            throw new ProcessExecutionException("The local editorial worker exceeded its request time limit.");
        }
        catch
        {
            lock (_sync) Stop();
            throw;
        }
        finally
        {
            lock (_sync)
            {
                _active = false;
                if (!_disposed && _process is not null) _idle.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
            }
            _requests.Release();
        }
    }

    private Process GetOrStart(ProcessRunRequest request)
    {
        if (request.Arguments.Count < 3 || request.Arguments[0] != "-B" ||
            request.Arguments[2] != "run-grounded-editorial-metadata-batch")
            throw new ProcessExecutionException("The editorial worker accepts only grounded metadata requests.");
        string identity = JsonSerializer.Serialize(new
        {
            request.ExecutablePath, host = request.Arguments[1],
            environment = request.EnvironmentVariables.Where(pair => !RequestEnvironment.Contains(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
        });
        if (_process is { HasExited: false } && _identity == identity) return _process;
        Stop();
        if (QwenGpuAdmission.GetBlockingReason() is string reason)
            throw new ProcessExecutionException(reason);
        var start = new ProcessStartInfo(request.ExecutablePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // Batch folders are deleted after parsing; the worker must not keep
            // one as its current directory or retain a deleted working path.
            WorkingDirectory = Path.GetDirectoryName(request.Arguments[1])!,
        };
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add(request.Arguments[1]);
        start.ArgumentList.Add("editorial-worker");
        start.Environment.Clear();
        foreach (var pair in request.EnvironmentVariables.Where(pair => !RequestEnvironment.Contains(pair.Key)))
            start.Environment[pair.Key] = pair.Value;
        _job = WindowsProcessJob.CreateKillOnClose();
        _process = new() { StartInfo = start };
        try
        {
            if (!_process.Start()) throw new ProcessExecutionException("The local editorial worker did not start.");
            _job.Assign(_process);
            _identity = identity;
            _stderrDrain = DrainNativeDiagnosticsAsync(_process.StandardError);
            return _process;
        }
        catch { Stop(); throw; }
    }

    private static async Task<string> ReadResponseAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        char[] buffer = new char[4096];
        while (true)
        {
            int read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new ProcessExecutionException("The editorial worker stopped before completing its response.");
            int newline = Array.IndexOf(buffer, '\n', 0, read);
            result.Append(buffer, 0, newline < 0 ? read : newline);
            if (result.Length > 3_200_000) throw new ProcessExecutionException("The editorial worker response is too large.");
            if (newline >= 0)
            {
                if (newline != read - 1) throw new ProcessExecutionException("Unexpected data followed the worker response.");
                return result.ToString();
            }
        }
    }

    private async Task DrainNativeDiagnosticsAsync(StreamReader reader)
    {
        char[] buffer = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
                lock (_sync) _nativeDiagnostics.Append(buffer, 0, Math.Min(read, Math.Max(0, 65536 - _nativeDiagnostics.Length)));
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
    }

    internal void ReleaseIdle() { lock (_sync) if (!_active) Stop(); }

    private void Stop()
    {
        _resident = false;
        _identity = null;
        _job?.TryTerminate();
        _job?.Dispose();
        _job = null;
        _process?.Dispose();
        _process = null;
        // Observe cleanup faults even if cancellation disposed the stream while
        // its read was pending. No reader survives a job termination.
        if (_stderrDrain is not null)
            _ = _stderrDrain.ContinueWith(static task => _ = task.Exception,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        _stderrDrain = null;
    }

    public void Dispose()
    {
        lock (_sync) { _disposed = true; _idle.Dispose(); Stop(); }
    }
}
