using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlProcessOutputReader
{
    public static async Task<string> ReadAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new Qwen3VlInferenceException(
                "The Qwen3-VL host completed without creating structured output.");
        }

        var info = new FileInfo(path);

        if (info.Length <= 0 ||
            info.Length > maximumBytes)
        {
            throw new Qwen3VlInferenceException(
                $"The Qwen3-VL structured output must contain 1 to {maximumBytes:N0} bytes.");
        }

        return await File.ReadAllTextAsync(
            path,
            cancellationToken);
    }

    public static string Diagnostics(
        ProcessRunResult result) =>
        $"Exit code: {result.ExitCode}{Environment.NewLine}" +
        $"stdout: {result.StandardOutput.Trim()}{Environment.NewLine}" +
        $"stderr: {result.StandardError.Trim()}";

    public static string? FailureSummary(ProcessRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (string line in result.StandardError
                     .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                     .Reverse()
                     .Concat(result.StandardOutput
                         .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                         .Reverse()))
        {
            string candidate = line.Trim();
            if (!candidate.StartsWith('{') || !candidate.EndsWith('}'))
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(candidate);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("errorCode", out JsonElement code) ||
                    code.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("message", out JsonElement message) ||
                    message.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(message.GetString()))
                {
                    continue;
                }

                string normalized = message.GetString()!.Trim();
                return $"{code.GetString()}: {normalized}";
            }
            catch (JsonException)
            {
                // Tool and model progress may contain braces. Only a complete
                // structured host error is suitable for a user-facing summary.
            }
        }

        return null;
    }
}
