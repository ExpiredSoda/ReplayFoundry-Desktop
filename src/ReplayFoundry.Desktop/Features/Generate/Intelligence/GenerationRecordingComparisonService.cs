using System.IO;
using System.Diagnostics;
using System.Text.Json;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal sealed record RecordingReviewRegion(string Id, double Start, double End, double Priority);

internal sealed class GenerationRecordingComparisonService(Qwen3VlQualifiedEditorialRuntime runtime)
{
    internal const string Version = "recording-comparison-1";
    internal const string PromptHash = "c37771e3714b4dadc5967ff1d7fe0e8aa04edc2a465b6d4800e2c59182cfd8a9";

    public async Task<IReadOnlyList<RecordingReviewRegion>> CompareAsync(JsonElement[] windows, string sourceHash,
        object preferences, int maximumRegions, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (windows.Length == 0) return [];
        var timer = Stopwatch.StartNew();
        string directory = ReplayFoundryLocalDataPaths.ResolveTemporary("recording-comparison/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            progress?.Report("Comparing promising parts of the recording before choosing which moments to inspect closely.");
            string input = Path.Combine(directory, "input.json"), output = Path.Combine(directory, "output.json");
            await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new
            {
                schemaVersion = Version, sourceHash, modelHash = runtime.Model.ManifestSha256,
                maximumRegions, preferences,
                windows = windows.Select(row => new { ordinal = row.GetProperty("ordinal").GetInt32(),
                    start = row.GetProperty("start").GetDouble(), end = row.GetProperty("end").GetDouble(),
                    summary = row.GetProperty("prediction").GetProperty("summary").GetString() })
            }), cancellationToken);
            var host = runtime.Host;
            var process = await MediaWorkBudget.RunAsync(new WindowsProcessRunner(), new ProcessRunRequest(
                host.PythonExecutablePath, ["-B", "-m", "replayfoundry_visual_semantic.recording_comparison",
                    "--input", input, "--output", output, "--model", host.ModelDirectoryPath,
                    "--cache", ReplayFoundryLocalDataPaths.Resolve(null, "Cache/RecordingComparison")],
                TimeSpan.FromMinutes(15), Path.GetDirectoryName(host.HostScriptPath), 524288, 524288,
                host.EnvironmentVariables, inheritParentEnvironment: false),
                MediaWorkPriority.FinalOutput, MediaWorkKind.HeavyAi, cancellationToken);
            if (!process.Succeeded) throw new InvalidOperationException("Recording comparison did not finish.");
            await Task.Run(() => runtime.ModelIntegrity.Verify(cancellationToken), cancellationToken);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellationToken));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetString() != Version || root.GetProperty("promptHash").GetString() != PromptHash)
                throw new InvalidDataException("Recording comparison identity changed.");
            var rows = root.GetProperty("regions").EnumerateArray().ToArray();
            if (rows.Length < 1 || rows.Length > maximumRegions) throw new InvalidDataException("Comparison exceeded its review budget.");
            var mapped = windows.ToDictionary(row => row.GetProperty("ordinal").GetInt32());
            var regions = rows.Select((row, rank) =>
            {
                int first = row.GetProperty("firstWindow").GetInt32(), last = row.GetProperty("lastWindow").GetInt32();
                if (last < first || last-first > 3 || !mapped.ContainsKey(first) || !mapped.ContainsKey(last))
                    throw new InvalidDataException("Comparison nominated unavailable recording sections.");
                return new RecordingReviewRegion($"{Version}:region-{rank}", mapped[first].GetProperty("start").GetDouble(),
                    mapped[last].GetProperty("end").GetDouble(), 1d-(double)rank/rows.Length);
            }).ToArray();
            GenerationVisualReviewDiagnostics.Save(runtime.Provider.Identity,
                [new("recording-comparison", "Comparison", 1, true, timer.Elapsed.TotalSeconds,
                    $"cacheHit={root.GetProperty("cacheHit").GetBoolean()}")], timer.Elapsed, regions.Length, regions.Length);
            progress?.Report($"Compared the recording and nominated {regions.Length} distinct regions for closer review.");
            return regions;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            GenerationVisualReviewDiagnostics.Save(runtime.Provider.Identity,
                [new("recording-comparison", "Comparison", 1, false, timer.Elapsed.TotalSeconds, exception.GetType().Name)], timer.Elapsed, 0, 1);
            progress?.Report("The recording comparison could not finish. The saved map is still available, and individual picture checks will continue.");
            return [];
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
