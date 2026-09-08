using System.IO;
using System.Diagnostics;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal sealed class GenerationRecordingIndexService(Qwen3VlQualifiedEditorialRuntime runtime)
{
    public async Task<GenerationCandidateIntelligenceResult> AnalyzeAsync(GenerationCandidateIntelligenceResult intelligence,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var refinements = intelligence.Refinements.ToDictionary(item => item.Candidate);
        var sources = intelligence.BaseMoments.Sources.ToDictionary(source => source.AnalyzedSource);
        foreach (var source in intelligence.BaseMoments.Sources)
        {
            var media = source.AnalyzedSource.PreparedSource.Media;
            var layouts = source.AnalyzedSource.CompositionPlan.Plan;
            var region = CompositionRegionSelector.FindPrimary(layouts.GetLayoutAt(TimeSpan.Zero), CompositionRegionRole.Gameplay)?.Geometry;
            var presenter = CompositionRegionSelector.FindPrimary(layouts.GetLayoutAt(TimeSpan.Zero), CompositionRegionRole.Presenter)?.Geometry;
            if (region is null) continue;
            string directory = ReplayFoundryLocalDataPaths.ResolveTemporary("recording-index/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var timer = Stopwatch.StartNew();
            try
            {
                progress?.Report("Finding gameplay, humor, lore and creator commentary. Saved results will be reused next time.");
                string input = Path.Combine(directory, "input.json"), output = Path.Combine(directory, "output.json");
                var transcript = intelligence.Transcripts?.Sources.SingleOrDefault(item =>
                    item.SourceFullPath.Equals(media.FullPath, StringComparison.OrdinalIgnoreCase));
                await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new
                {
                    schemaVersion = "recording-index-5", sourcePath = media.FullPath,
                    durationSeconds = media.Duration.TotalSeconds, modelHash = runtime.Model.ManifestSha256,
                    region = new[] { region.X, region.Y, region.Width, region.Height },
                    contextRegion = presenter is null ? null : new[] { presenter.X, presenter.Y, presenter.Width, presenter.Height },
                    preferences = new { mode = intelligence.BaseMoments.Request.Setup.Mode.ToString(),
                        emphasis = intelligence.BaseMoments.Request.Setup.ContentEmphasis.ToString(),
                        intent = intelligence.BaseMoments.Request.Setup.DiscoveryIntent.MomentType.ToString() },
                    transcript = (transcript?.Segments ?? []).Select(item => new
                    { start = item.AbsoluteSourceStart.TotalSeconds, end = item.AbsoluteSourceEnd.TotalSeconds, text = item.Text }),
                }), cancellationToken);
                await Task.Run(() => runtime.ModelIntegrity.Verify(cancellationToken), cancellationToken);
                var host = runtime.Host;
                var process = await MediaWorkBudget.RunAsync(new WindowsProcessRunner(), new ProcessRunRequest(
                    host.PythonExecutablePath, ["-B", "-m", "replayfoundry_visual_semantic.recording_index",
                        "--input", input, "--output", output, "--model", host.ModelDirectoryPath,
                        "--ffmpeg", new FfmpegToolLocator().LocateFfmpeg(), "--cache",
                        ReplayFoundryLocalDataPaths.Resolve(null, "Cache/RecordingIndex")],
                    TimeSpan.FromHours(2), Path.GetDirectoryName(host.HostScriptPath),
                    524288, 524288, host.EnvironmentVariables, inheritParentEnvironment: false,
                    standardOutputLine: line =>
                    {
                        if (DescribeProgress(line) is string detail) progress?.Report(detail);
                    }),
                    MediaWorkPriority.FinalOutput, MediaWorkKind.HeavyAi, cancellationToken);
                if (!process.Succeeded) throw new InvalidOperationException("Neural recording index did not complete.");
                QwenModelLoadDiagnostics.Report(process.StandardError);
                await Task.Run(() => runtime.ModelIntegrity.Verify(cancellationToken), cancellationToken);
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellationToken));
                var root = document.RootElement;
                if (root.GetProperty("schemaVersion").GetString() != "recording-index-5")
                    throw new InvalidDataException("Recording index version changed.");
                TimeSpan indexElapsed = timer.Elapsed;
                var windows = root.GetProperty("windows").EnumerateArray().ToArray();
                var setup = intelligence.BaseMoments.Request.Setup;
                var reviewRegions = await new GenerationRecordingComparisonService(runtime).CompareAsync(windows,
                    root.GetProperty("sourceHash").GetString()!,
                    new { mode = setup.Mode.ToString(), emphasis = setup.ContentEmphasis.ToString(),
                        intent = setup.DiscoveryIntent.MomentType.ToString(), desiredClips = setup.DesiredResultCount },
                    Math.Clamp(setup.DesiredResultCount + 3, 8, 20), progress, cancellationToken);
                string? selectedGame = intelligence.BaseMoments.Request.Setup.GameContextSettings.Find(media.FullPath)?.GameName;
                string? visibleGame = windows.Select(row => row.GetProperty("prediction").GetProperty("visibleGameTitle").GetString())
                    .Where(title => !string.IsNullOrWhiteSpace(title)).GroupBy(title => NormalizeTitle(title!))
                    .Where(group => group.Count() >= 2).OrderByDescending(group => group.Count()).Select(group => group.First()).FirstOrDefault();
                bool identityConflict = selectedGame is not null && visibleGame is not null &&
                    NormalizeTitle(selectedGame) != NormalizeTitle(visibleGame);
                var expanded = source.Moments;
                if (transcript is not null)
                {
                    var seeds = windows.Where(row => row.GetProperty("prediction").GetProperty("funny").GetBoolean() ||
                            row.GetProperty("prediction").GetProperty("commentary").GetBoolean())
                        .Select(row => row.GetProperty("prediction").GetProperty("speechMomentIds").EnumerateArray()
                            .Select(value => value.GetInt32()).Distinct().Where(id => id >= 0 && id < transcript.Segments.Count)
                            .Select(id => transcript.Segments[id]).OrderBy(segment => segment.AbsoluteSourceStart).ToArray())
                        .Where(segments => segments.Length > 0)
                        .Select(segments => new GenerationTimedExplorationSeed(segments[0].AbsoluteSourceStart,
                            segments[^1].AbsoluteSourceEnd, "A local neural review nominated this complete spoken passage; close review is still required."))
                        .DistinctBy(seed => (seed.Start, seed.End)).ToArray();
                    foreach (var chunk in seeds.Chunk(GenerationSemanticReviewBudgetPolicy.MaximumCandidates))
                        expanded = GenerationSemanticExplorationPlanner.Expand(expanded, chunk.Length, cancellationToken, semanticSeeds: chunk);
                }
                var sourceRefinements = new Dictionary<MomentCandidate, GenerationCandidateRefinement>();
                foreach (var candidate in expanded.Proposals)
                {
                    var existing = refinements.TryGetValue(candidate, out var retained) ? retained : new(candidate, [], "neural-speech-nomination-1");
                    var components = existing.Components.ToList();
                    if (identityConflict)
                        components.Add(new(GenerationCandidateRefinementComponentCode.GameIdentityConflict, 1, 0,
                            $"Check the game name: the recording may show {visibleGame}, while {selectedGame} was selected. " +
                            "Game-specific tags and background information are paused until you confirm the game.",
                            ["recording-index-5:visible-title-conflict"]));
                    double start = candidate.Window.Start.TotalSeconds, end = candidate.Window.End.TotalSeconds;
                    double Coverage(Func<JsonElement, bool> predicate) => Math.Clamp(windows.Where(predicate).Sum(row =>
                        Math.Max(0, Math.Min(end, row.GetProperty("end").GetDouble()) - Math.Max(start, row.GetProperty("start").GetDouble()))) / (end-start), 0, 1);
                    double covered = Coverage(_ => true);
                    Add(GenerationCandidateRefinementComponentCode.NeuralIndexCoverage, covered, 0, "Recording timeline checked by the local vision model.");
                    if (covered < .8) { sourceRefinements[candidate] = new(candidate, components, existing.PolicyVersion); continue; }
                    double gameplay = Coverage(row => row.GetProperty("prediction").GetProperty("gameplay").GetBoolean());
                    double funny = Coverage(row => row.GetProperty("prediction").GetProperty("funny").GetBoolean());
                    double commentary = Coverage(row => row.GetProperty("prediction").GetProperty("commentary").GetBoolean());
                    double menu = Coverage(row => row.GetProperty("prediction").GetProperty("menu").GetBoolean());
                    double lore = Coverage(row => row.GetProperty("prediction").GetProperty("lore").GetBoolean());
                    Add(GenerationCandidateRefinementComponentCode.NeuralGameplay, gameplay, 0, "The timeline model observed gameplay; this category has no fixed ranking bonus.");
                    Add(GenerationCandidateRefinementComponentCode.NeuralHumor, funny, 0,
                        "The timeline model observed possible humor; its value is learned from context and feedback.");
                    Add(GenerationCandidateRefinementComponentCode.NeuralCommentary, commentary, 0, "The timeline model observed commentary; this is a neural learning input.");
                    Add(GenerationCandidateRefinementComponentCode.NeuralLore, lore, 0, "The timeline model observed lore or story; this category has no fixed ranking bonus.");
                    Add(GenerationCandidateRefinementComponentCode.NeuralMenu, menu, 0, "The timeline model observed interface content; this is not an automatic exclusion.");
                    double interest = windows.Sum(row => Math.Max(0, Math.Min(end, row.GetProperty("end").GetDouble()) -
                            Math.Max(start, row.GetProperty("start").GetDouble())) *
                            row.GetProperty("prediction").GetProperty("editorialValue").GetDouble() / 100) / ((end-start)*covered);
                    Add(GenerationCandidateRefinementComponentCode.NeuralTimelineValue, interest, 0,
                        "Clip potential estimated by the pretrained timeline model; personal training is separate.");
                    var nomination = reviewRegions.Select(region => new { Region = region,
                            Overlap = Math.Max(0, Math.Min(end, region.End) - Math.Max(start, region.Start)) /
                                (Math.Max(end, region.End) - Math.Min(start, region.Start)) })
                        .Where(item => item.Overlap > 0).OrderByDescending(item => item.Overlap)
                        .ThenByDescending(item => item.Region.Priority).FirstOrDefault();
                    if (nomination is not null)
                    {
                        // This order allocates close-review work; it does not add
                        // a category bonus or replace the eventual scene/personal score.
                        components.Add(new(GenerationCandidateRefinementComponentCode.NeuralReviewPriority,
                            nomination.Region.Priority, 0, "A comparison with the rest of the recording nominated this region for closer review.", [nomination.Region.Id]));
                        components.Add(new(GenerationCandidateRefinementComponentCode.NeuralRegionCoverage,
                            nomination.Overlap, 0, "How much of the nominated region this cut covers.", [nomination.Region.Id]));
                    }
                    sourceRefinements[candidate] = new(candidate, components, existing.PolicyVersion);
                    void Add(GenerationCandidateRefinementComponentCode code, double value, double weight, string explanation) =>
                        components.Add(new(code, value, weight, explanation, ["recording-index-5"]));
                }
                foreach (var item in sourceRefinements) refinements[item.Key] = item.Value;
                sources[source.AnalyzedSource] = new(source.AnalyzedSource, expanded);
                GenerationVisualReviewDiagnostics.Save(runtime.Provider.Identity,
                    [new("recording-index", "Index", 1, true, indexElapsed.TotalSeconds,
                        $"cacheHits={root.GetProperty("cacheHits").GetInt32()}")],
                    indexElapsed, windows.Length,
                    root.GetProperty("requested").GetInt32());
                progress?.Report($"Mapped {windows.Length} of {root.GetProperty("requested").GetInt32()} recording sections; " +
                    $"{root.GetProperty("cacheHits").GetInt32()} reused from saved analysis.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                GenerationVisualReviewDiagnostics.Save(runtime.Provider.Identity,
                    [new("recording-index", "Index", 1, false, timer.Elapsed.TotalSeconds, exception.GetType().Name)], timer.Elapsed, 0, 1);
                progress?.Report("The recording map could not finish. Unchecked sections will remain unclassified; close picture checks will continue.");
            }
            finally
            {
                // This directory is a fresh, fixed child of the OS temp directory, never a user source.
                try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        var expandedSources = intelligence.BaseMoments.Sources.Select(source => sources[source.AnalyzedSource]).ToArray();
        var baseline = new GenerationMomentFindingResult(intelligence.BaseMoments.Request, expandedSources,
            intelligence.BaseMoments.SelectedCandidates);
        var selected = new GenerationMomentPortfolioSelector().Select(baseline.Request,
            expandedSources, refinements, cancellationToken);
        return new(baseline, intelligence.SpeechActivity, refinements.Values,
            new GenerationMomentFindingResult(baseline.Request, expandedSources,
                selected, refinements), intelligence.VisualSemantic, intelligence.Transcripts);
    }

    internal static string NormalizeTitle(string title) => new string(title.ToUpperInvariant()
        .Where(char.IsLetterOrDigit).ToArray());

    internal static string? DescribeProgress(string line)
    {
        if (line.Length > 2048) return null;
        try
        {
            using var json = JsonDocument.Parse(line);
            var row = json.RootElement;
            if (row.GetProperty("stage").GetString() != "recording-index-progress") return null;
            int checkedCount = row.GetProperty("checked").GetInt32(), total = row.GetProperty("total").GetInt32();
            int mapped = row.GetProperty("mapped").GetInt32(), reused = row.GetProperty("reused").GetInt32();
            if (total < 1 || checkedCount < 0 || checkedCount > total || mapped < 0 || mapped > total || reused < 0 || reused > mapped) return null;
            return $"Checked {checkedCount} of {total} recording sections · {mapped} mapped · {reused} reused from saved analysis.";
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return null; }
    }
}
