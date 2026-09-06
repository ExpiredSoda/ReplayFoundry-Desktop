using System.IO;
using System.Text.Json;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

/// <summary>Host pass walls, delivered after its process exits. Watchdog time is nested in GenerationCallWallSeconds.</summary>
public sealed record QwenEditorialPassDiagnostic(
    string ProcessBatchId, string CandidateId, int Attempt, int CaseOrdinal, int PassOrdinal,
    string PassKind, string Status, double PreparationWallSeconds, double? GenerationCallWallSeconds,
    double? WatchdogGenerationWallSeconds, double? PostprocessingWallSeconds, double TotalWallSeconds,
    int? InputTokenCount, int? GeneratedTokenCount);

public sealed record QwenEditorialPassCaptureDiagnostic(
    string ProcessBatchId, int ObservedLines, int AcceptedRecords, int RejectedRecords);

/// <summary>Opt-in, content-free diagnostics. This never changes inference acceptance or global environment.</summary>
public static class QwenEditorialPassDiagnostics
{
    internal const string EnvironmentFlag = "REPLAYFOUNDRY_EDITORIAL_PASS_TIMING";
    private const string Prefix = "replayfoundry-editorial-pass: ";
    private const int MaximumRecords = 1024;
    // Existing watchdog uses time.monotonic; outer diagnostics use perf_counter.
    // Permit one coarse Windows timer quantum plus rounding without changing either clock.
    internal const double WatchdogClockToleranceSeconds = 0.020;
    private static readonly AsyncLocal<CaptureScope?> Observer = new();

    public static IDisposable Capture(Action<QwenEditorialPassDiagnostic> observer,
        Action<QwenEditorialPassCaptureDiagnostic>? captureObserver = null)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var scope = new CaptureScope(observer, captureObserver, Observer.Value);
        Observer.Value = scope;
        return scope;
    }

    internal static IReadOnlyDictionary<string, string> ProcessEnvironment(IReadOnlyDictionary<string, string> original)
    {
        if (Observer.Value is not { IsActive: true }) return original;
        var copy = new Dictionary<string, string>(original, StringComparer.OrdinalIgnoreCase)
        {
            [EnvironmentFlag] = "1"
        };
        return copy;
    }

    internal static void Report(string standardError, IEnumerable<(string CandidateId, int Attempt)> submittedCases)
    {
        if (Observer.Value is not { IsActive: true } observer) return;
        var expected = submittedCases.Select((value, index) => (value.CandidateId, value.Attempt, CaseOrdinal: index + 1)).ToHashSet();
        var seen = new HashSet<int>();
        int observedLines = 0, rejectedRecords = 0;
        string processBatchId = Guid.NewGuid().ToString("N");
        using var reader = new StringReader(standardError);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            observedLines++;
            if (seen.Count >= MaximumRecords || line.Length > 2048)
            {
                rejectedRecords++;
                continue;
            }
            try
            {
                using var json = JsonDocument.Parse(line[Prefix.Length..]);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    Text(root, "schemaVersion") != "editorial-pass-diagnostic-1.0" ||
                    Text(root, "candidateId") is not { } candidate ||
                    !Integer(root, "attempt", 0, 10_000, out int attempt) ||
                    !Integer(root, "caseOrdinal", 1, 32, out int caseOrdinal) ||
                    !expected.Contains((candidate, attempt, caseOrdinal)) ||
                    !Integer(root, "passOrdinal", 1, MaximumRecords, out int passOrdinal) ||
                    Text(root, "passKind") is not ("Unknown" or "VisualDraft" or "VisualEventSelection" or
                        "KnowledgeSelection" or "Synthesis" or "SynthesisRecovery" or "EditorialRephrase" or
                        "IsolatedVisualField" or "IsolatedCommentaryField") ||
                    Text(root, "status") is not ("Returned" or "Raised") ||
                    !Seconds(root, "preparationWallSeconds", out double? preparation) || preparation is null ||
                    !Seconds(root, "generationCallWallSeconds", out double? generation) ||
                    !Seconds(root, "watchdogGenerationWallSeconds", out double? watchdog) ||
                    !Seconds(root, "postprocessingWallSeconds", out double? postprocessing) ||
                    !Seconds(root, "totalWallSeconds", out double? total) || total is null ||
                    !TokenCount(root, "inputTokenCount", out int? inputCount) ||
                    !TokenCount(root, "generatedTokenCount", out int? generatedCount))
                {
                    rejectedRecords++;
                    continue;
                }
                // No invented zero phases: null means generation did not finish with an observable trace.
                if (generation.HasValue != postprocessing.HasValue || inputCount.HasValue != generatedCount.HasValue ||
                    (generation is null && (watchdog is not null || inputCount is not null)) ||
                    (watchdog is { } inner && generation is { } outer && inner > outer + WatchdogClockToleranceSeconds) ||
                    Math.Abs(preparation.Value + (generation ?? 0) + (postprocessing ?? 0) - total.Value) > 0.00001 ||
                    (Text(root, "status") == "Returned" && (generation is null || inputCount is null)) ||
                    !seen.Add(passOrdinal))
                {
                    rejectedRecords++;
                    continue;
                }
                observer.Report(new(processBatchId, candidate, attempt, caseOrdinal, passOrdinal,
                    Text(root, "passKind")!, Text(root, "status")!, preparation.Value, generation, watchdog,
                    postprocessing, total.Value, inputCount, generatedCount));
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or IOException or FormatException or OverflowException)
            {
                // Untrusted/malformed diagnostics cannot replace the provider's own validation.
                rejectedRecords++;
            }
        }
        observer.ReportCapture(new(processBatchId, observedLines, seen.Count, rejectedRecords));
    }

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Integer(JsonElement root, string property, int minimum, int maximum, out int value)
    {
        value = 0;
        return root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) && value >= minimum && value <= maximum;
    }

    private static bool Seconds(JsonElement root, string property, out double? value)
    {
        value = null;
        if (!root.TryGetProperty(property, out var element)) return false;
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double number) ||
            !double.IsFinite(number) || number is < 0 or > 86_400) return false;
        value = number;
        return true;
    }

    private static bool TokenCount(JsonElement root, string property, out int? value)
    {
        value = null;
        if (!root.TryGetProperty(property, out var element)) return false;
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (!Integer(root, property, 1, 10_000_000, out int count)) return false;
        value = count;
        return true;
    }

    private sealed class CaptureScope(Action<QwenEditorialPassDiagnostic> observer,
        Action<QwenEditorialPassCaptureDiagnostic>? captureObserver, CaptureScope? prior) : IDisposable
    {
        private int _disposed;
        public bool IsActive => Volatile.Read(ref _disposed) == 0;

        public void Report(QwenEditorialPassDiagnostic diagnostic)
        {
            if (!IsActive) return;
            try { observer(diagnostic); }
            catch (Exception) { /* Diagnostic sinks cannot change inference acceptance. */ }
        }

        public void ReportCapture(QwenEditorialPassCaptureDiagnostic diagnostic)
        {
            if (!IsActive || captureObserver is null) return;
            try { captureObserver(diagnostic); }
            catch (Exception) { /* Capture summaries cannot change inference acceptance. */ }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (ReferenceEquals(Observer.Value, this)) Observer.Value = prior;
        }
    }
}
