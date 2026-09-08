using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal sealed record GenerationVisualReviewDiagnostic(
    string CandidateId, string Stage, int Attempt, bool Succeeded, double ElapsedSeconds, string? Failure);

internal static class GenerationVisualReviewDiagnostics
{
    public static void Save(object provider, IReadOnlyList<GenerationVisualReviewDiagnostic> cases,
        TimeSpan elapsed, int completed, int requested)
    {
        // Local diagnostics contain identifiers and timings, never frames, transcript text or source paths.
        try
        {
            string directory = ReplayFoundryLocalDataPaths.Resolve(null, "Diagnostics/VisualReview");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { schemaVersion = 1, provider,
                elapsedSeconds = elapsed.TotalSeconds, completed, requested, cases }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
