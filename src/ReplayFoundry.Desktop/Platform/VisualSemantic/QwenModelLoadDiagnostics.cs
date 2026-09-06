using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

public sealed record QwenModelLoadDiagnostic(double ModelLoadSeconds);

/// <summary>Optional developer-only observations, independent of the inference protocol.</summary>
public static class QwenModelLoadDiagnostics
{
    private const string Prefix = "replayfoundry-model-load: ";
    private static readonly AsyncLocal<Action<QwenModelLoadDiagnostic>?> Observer = new();

    public static IDisposable Capture(Action<QwenModelLoadDiagnostic> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var prior = Observer.Value;
        Observer.Value = observer;
        return new CaptureScope(prior);
    }

    internal static void Report(string standardError)
    {
        if (Observer.Value is not { } observer) return;
        foreach (string line in standardError.Split('\n'))
        {
            if (!line.StartsWith(Prefix, StringComparison.Ordinal) || line.Length > 512) continue;
            try
            {
                using var json = JsonDocument.Parse(line[Prefix.Length..]);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.String ||
                    schema.GetString() != "model-load-diagnostic-1.0" ||
                    !root.TryGetProperty("modelLoadSeconds", out var seconds) || !seconds.TryGetDouble(out double value) ||
                    !double.IsFinite(value) || value is < 0 or > 86_400) continue;
                try { observer(new(value)); }
                catch (Exception) { /* Developer telemetry never changes inference acceptance. */ }
                break; // At most one bounded load observation per process.
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or System.IO.IOException) { }
        }
    }

    private sealed class CaptureScope(Action<QwenModelLoadDiagnostic>? prior) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            Observer.Value = prior;
            _disposed = true;
        }
    }
}
