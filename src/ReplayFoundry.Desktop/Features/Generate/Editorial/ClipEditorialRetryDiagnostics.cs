namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public sealed record ClipEditorialRetryCaseDiagnostic(
    string CandidateId,
    int Attempt,
    string Variant,
    int NextAttempt,
    string NextVariant,
    bool TitleNoveltyRejected,
    string? TitleNoveltyRule,
    bool DifferentTitleRequired,
    IReadOnlyList<string> IssueCodes,
    IReadOnlyList<string> SourceRuleCodes);

/// <summary>
/// Observes a service-level retry invocation, not a model-process start.
/// ElapsedSeconds is monotonic time since this editorial batch began;
/// RetryElapsedSeconds includes admission, inference, parsing and validation.
/// Audience copy and transcripts are deliberately excluded.
/// </summary>
public sealed record ClipEditorialRetryDiagnostic(
    string Event,
    string BatchId,
    int RetryRound,
    double ElapsedSeconds,
    double? RetryElapsedSeconds,
    IReadOnlyList<ClipEditorialRetryCaseDiagnostic> Cases,
    string? FailureType = null);

/// <summary>Optional scoped developer observations that cannot change generation.</summary>
public static class ClipEditorialRetryDiagnostics
{
    private static readonly AsyncLocal<CaptureScope?> Observer = new();

    public static IDisposable Capture(Action<ClipEditorialRetryDiagnostic> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var scope = new CaptureScope(observer, Observer.Value);
        Observer.Value = scope;
        return scope;
    }

    internal static void Report(ClipEditorialRetryDiagnostic diagnostic) =>
        Observer.Value?.Report(diagnostic);

    private sealed class CaptureScope(
        Action<ClipEditorialRetryDiagnostic> observer,
        CaptureScope? prior) : IDisposable
    {
        private int _disposed;

        public void Report(ClipEditorialRetryDiagnostic diagnostic)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            try { observer(diagnostic); }
            catch (Exception) { /* Diagnostic failures must not change AI acceptance. */ }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (ReferenceEquals(Observer.Value, this)) Observer.Value = prior;
        }
    }
}
