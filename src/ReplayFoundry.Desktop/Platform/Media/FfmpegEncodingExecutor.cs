using System.Collections.Concurrent;
using System.IO;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Media;

/// <summary>Hardware encoding is admitted only after a successful local encode/decode qualification.</summary>
internal sealed class FfmpegEncodingExecutor
{
    private static readonly ConcurrentDictionary<string, string> Qualified = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim QualificationGate = new(1, 1);
    private readonly IProcessRunner _runner;
    internal FfmpegEncodingExecutor(IProcessRunner runner) => _runner = runner;

    internal async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? validateOutput = null)
    {
        int flag = request.Arguments.ToList().IndexOf("-hw_encoding");
        if (flag < 0 || File.Exists(request.Arguments[^1]))
            return await RunSoftwareAsync(request, validateOutput, cancellationToken);
        string encoder = await QualifyAsync(request.ExecutablePath, cancellationToken);
        if (encoder.Length == 0) return await RunSoftwareAsync(request, validateOutput, cancellationToken);
        string[] accelerated = WithEncoder(request.Arguments, encoder);
        try
        {
            request.StandardOutputLine?.Invoke("encoder_started=hardware");
            ProcessRunResult result = await _runner.RunAsync(Copy(request, accelerated), cancellationToken);
            if (result.Succeeded)
            {
                if (!File.Exists(request.Arguments[^1]) || new FileInfo(request.Arguments[^1]).Length == 0)
                    throw new InvalidOperationException("The hardware encoder did not create a complete output.");
                if (validateOutput is not null) await validateOutput(cancellationToken);
                return result;
            }
        }
        catch (Exception error) when (error is ProcessExecutionException or ProcessTimeoutException or InvalidOperationException or MediaProbeException)
        {
            // A failed real encode or invalid bitstream disqualifies this session's hardware path.
            // Cancellation is deliberately outside this catch and never starts another encoder.
        }
        cancellationToken.ThrowIfCancellationRequested();
        Qualified[Identity(request.ExecutablePath)] = string.Empty;
        string output = request.Arguments[^1];
        if (File.Exists(output)) File.Delete(output);
        request.StandardOutputLine?.Invoke("encoder_fallback=software");
        return await RunSoftwareAsync(request, validateOutput, cancellationToken);
    }

    private async Task<ProcessRunResult> RunSoftwareAsync(ProcessRunRequest request,
        Func<CancellationToken, Task>? validateOutput, CancellationToken cancellationToken)
    {
        request.StandardOutputLine?.Invoke("encoder_started=software");
        ProcessRunResult result = await _runner.RunAsync(request, cancellationToken);
        if (result.Succeeded && validateOutput is not null) await validateOutput(cancellationToken);
        return result;
    }

    private async Task<string> QualifyAsync(string ffmpeg, CancellationToken cancellationToken)
    {
        string identity = Identity(ffmpeg);
        if (Qualified.TryGetValue(identity, out string? existing)) return existing;
        await QualificationGate.WaitAsync(cancellationToken);
        try
        {
            if (Qualified.TryGetValue(identity, out existing)) return existing;
            string root = ReplayFoundryLocalDataPaths.ResolveTemporary(
                "encoder-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string output = Path.Combine(root, "qualification.mp4");
            try
            {
                foreach (string encoder in new[] { "h264_nvenc", "h264_qsv", "h264_amf", "h264_mf" })
                {
                    if (File.Exists(output)) File.Delete(output);
                    var arguments = new List<string> { "-hide_banner", "-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-t", "1" };
                    arguments.AddRange(FfmpegH264EncodingPolicy.CreateArguments(4_000_000)); arguments.Add(output);
                    ProcessRunResult encode = await _runner.RunAsync(new ProcessRunRequest(ffmpeg,
                        WithEncoder(arguments, encoder), TimeSpan.FromSeconds(20)), cancellationToken);
                    if (!encode.Succeeded || !File.Exists(output) || new FileInfo(output).Length == 0) continue;
                    ProcessRunResult decode = await _runner.RunAsync(new ProcessRunRequest(ffmpeg,
                        ["-hide_banner", "-nostdin", "-v", "error", "-xerror", "-i", output, "-f", "null", "-"],
                        TimeSpan.FromSeconds(20)), cancellationToken);
                    if (decode.Succeeded) return Qualified[identity] = encoder;
                }
                return Qualified[identity] = string.Empty;
            }
            catch (Exception error) when (error is IOException or ProcessExecutionException or ProcessTimeoutException)
            { return Qualified[identity] = string.Empty; }
            finally { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        finally { QualificationGate.Release(); }
    }
    internal static string[] WithEncoder(IReadOnlyList<string> original, string encoder)
    {
        if (encoder == "h264_mf")
        {
            string[] mediaFoundation = original.ToArray();
            mediaFoundation[Array.IndexOf(mediaFoundation, "-hw_encoding") + 1] = "1";
            return mediaFoundation;
        }
        if (encoder is not ("h264_nvenc" or "h264_qsv" or "h264_amf"))
            throw new ArgumentException("The hardware encoder is not a qualified policy.", nameof(encoder));
        var result = new List<string>();
        for (int index = 0; index < original.Count; index++)
        {
            string value = original[index];
            if (value is "-rate_control" or "-scenario" or "-hw_encoding") { index++; continue; }
            result.Add(value);
            if (value == "-c:v") { result.Add(encoder); index++; }
            else if (value == "-profile:v") { result.Add("main"); index++; }
            else if (value == "-pix_fmt" && encoder == "h264_qsv") { result.Add("nv12"); index++; }
        }
        string[] options = encoder switch
        {
            "h264_nvenc" => ["-preset", "p4", "-tune", "hq", "-rc", "vbr"],
            "h264_qsv" => ["-preset", "medium"],
            _ => ["-quality", "balanced", "-rc", "vbr_peak"],
        };
        result.InsertRange(result.Count - 1, options);
        return result.ToArray();
    }
    private static string Identity(string path)
    {
        var file = new FileInfo(path);
        return path + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
    }
    private static ProcessRunRequest Copy(ProcessRunRequest original, string[] arguments) => new(
        original.ExecutablePath, arguments, original.Timeout, original.WorkingDirectory,
        original.MaxStandardOutputCharacters, original.MaxStandardErrorCharacters,
        original.EnvironmentVariables, original.InheritParentEnvironment, original.StandardOutputLine);
}
