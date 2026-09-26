using System.Text.Json;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static class QwenEditorialPassDiagnosticsTests
{
    public static IEnumerable<TestCase> GetTests()
    {
        yield return new("Editorial pass timings reconcile submitted identities and nested phase totals", ParseRequiresOwnedIdentityAndCoherentPhases);
        yield return new("Editorial pass captures isolate process environments and suppress disposed async sinks", CaptureIsOptInAndScoped);
        yield return new("Editorial pass timing allows bounded independent-clock quantization and reports dropped records", IndependentClockQuantizationIsBounded);
        yield return new("Isolated editorial fields retain separate measured pass identities", IsolatedFieldsRemainDistinct);
    }

    internal static string Line(string candidateId = "candidate-1", int attempt = 2, int ordinal = 1, bool preparationFailure = false,
        string passKind = "Synthesis") =>
        "replayfoundry-editorial-pass: " + JsonSerializer.Serialize(new
        {
            schemaVersion = "editorial-pass-diagnostic-1.0", candidateId, attempt, caseOrdinal = 1,
            passOrdinal = ordinal, passKind, status = preparationFailure ? "Raised" : "Returned",
            preparationWallSeconds = 2d,
            generationCallWallSeconds = preparationFailure ? (double?)null : 5d,
            watchdogGenerationWallSeconds = preparationFailure ? (double?)null : 4d,
            postprocessingWallSeconds = preparationFailure ? (double?)null : 3d,
            totalWallSeconds = preparationFailure ? 2d : 10d,
            inputTokenCount = preparationFailure ? (int?)null : 100,
            generatedTokenCount = preparationFailure ? (int?)null : 20
        });

    private static Task IsolatedFieldsRemainDistinct()
    {
        var passes = new List<QwenEditorialPassDiagnostic>();
        var captures = new List<QwenEditorialPassCaptureDiagnostic>();
        using (QwenEditorialPassDiagnostics.Capture(passes.Add, captures.Add))
        {
            QwenEditorialPassDiagnostics.Report(string.Join('\n',
                Line(ordinal: 1, passKind: "IsolatedVisualField"),
                Line(ordinal: 2, passKind: "IsolatedCommentaryField"),
                Line(ordinal: 3, passKind: "InventedField")), [("candidate-1", 2)]);
        }
        TestAssert.True(passes.Select(static pass => pass.PassKind).SequenceEqual(
            new[] { "IsolatedVisualField", "IsolatedCommentaryField" }),
            "Each component retains its measured identity without being relabeled as a rephrase.");
        TestAssert.True(passes.All(pass => pass.ProcessBatchId == captures.Single().ProcessBatchId),
            "Both component calls reconcile to the same captured provider process.");
        TestAssert.Equal(2, captures.Single().AcceptedRecords, "Both actual component kinds are recognized.");
        TestAssert.Equal(1, captures.Single().RejectedRecords, "Unknown names still fail diagnostic validation.");
        return Task.CompletedTask;
    }

    private static Task ParseRequiresOwnedIdentityAndCoherentPhases()
    {
        var results = new List<QwenEditorialPassDiagnostic>();
        string valid = Line();
        (string CandidateId, int Attempt)[] expected = [("candidate-1", 2)];
        using (QwenEditorialPassDiagnostics.Capture(results.Add))
        {
            QwenEditorialPassDiagnostics.Report(string.Join('\n',
                "ordinary log", "replayfoundry-editorial-pass: invalid",
                valid.Replace("\"totalWallSeconds\":10", "\"totalWallSeconds\":11", StringComparison.Ordinal),
                valid.Replace("\"watchdogGenerationWallSeconds\":4", "\"watchdogGenerationWallSeconds\":6", StringComparison.Ordinal),
                valid.Replace("\"inputTokenCount\":100", "\"inputTokenCount\":null", StringComparison.Ordinal),
                valid.Replace("\"postprocessingWallSeconds\":3", "\"postprocessingWallSeconds\":-1", StringComparison.Ordinal),
                Line(candidateId: "different-case"), Line(attempt: 3),
                valid.Replace("\"caseOrdinal\":1", "\"caseOrdinal\":2", StringComparison.Ordinal),
                valid, valid, Line(ordinal: 2, preparationFailure: true)), expected);
            QwenEditorialPassDiagnostics.Report(valid, expected);
        }
        QwenEditorialPassDiagnostics.Report(valid, expected);
        TestAssert.Equal(3, results.Count, "Only coherent submitted passes are observed; a repeated ordinal is allowed only in a different process report.");
        var first = results[0];
        TestAssert.Equal(10d, first.PreparationWallSeconds + first.GenerationCallWallSeconds!.Value + first.PostprocessingWallSeconds!.Value,
            "Preparation, whole generate call, and postprocessing partition total time.");
        TestAssert.Equal(4d, first.WatchdogGenerationWallSeconds!.Value, "Watchdog time remains a nested duration, not another additive stage.");
        TestAssert.Equal(first.ProcessBatchId, results[1].ProcessBatchId, "One parsed process has one grouping identity.");
        TestAssert.True(first.ProcessBatchId != results[2].ProcessBatchId, "A later provider process gets a distinct identity.");
        TestAssert.True(results[1].GenerationCallWallSeconds is null && results[1].PostprocessingWallSeconds is null &&
            results[1].InputTokenCount is null && results[1].GeneratedTokenCount is null,
            "Preparation failure cannot fabricate zero generation times or token counts.");
        return Task.CompletedTask;
    }

    private static async Task CaptureIsOptInAndScoped()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PATH"] = "owned-runtime" };
        TestAssert.True(ReferenceEquals(environment, QwenEditorialPassDiagnostics.ProcessEnvironment(environment)),
            "No active capture changes the process environment.");
        var outer = new List<QwenEditorialPassDiagnostic>();
        var inner = new List<QwenEditorialPassDiagnostic>();
        (string CandidateId, int Attempt)[] expected = [("candidate-1", 2)];
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (QwenEditorialPassDiagnostics.Capture(outer.Add))
        {
            var modified = QwenEditorialPassDiagnostics.ProcessEnvironment(environment);
            TestAssert.Equal("1", modified[QwenEditorialPassDiagnostics.EnvironmentFlag], "Only the child environment opts in.");
            TestAssert.Equal("owned-runtime", modified["PATH"], "Pinned runtime environment values remain intact.");
            TestAssert.True(!environment.ContainsKey(QwenEditorialPassDiagnostics.EnvironmentFlag), "The original shared environment is immutable.");
            IDisposable nested = QwenEditorialPassDiagnostics.Capture(inner.Add);
            QwenEditorialPassDiagnostics.Report(Line(), expected);
            Task inherited = Task.Run(async () =>
            {
                await release.Task;
                TestAssert.True(ReferenceEquals(environment, QwenEditorialPassDiagnostics.ProcessEnvironment(environment)),
                    "Disposed inherited scopes cannot opt another process in.");
                QwenEditorialPassDiagnostics.Report(Line(), expected);
            });
            nested.Dispose();
            nested.Dispose();
            release.SetResult();
            await inherited;
            QwenEditorialPassDiagnostics.Report(Line(), expected);
            using (QwenEditorialPassDiagnostics.Capture(static _ => throw new InvalidOperationException("sink")))
                QwenEditorialPassDiagnostics.Report(Line(), expected);
        }
        TestAssert.Equal(1, outer.Count, "Disposal restores the active outer observer exactly once.");
        TestAssert.Equal(1, inner.Count, "Disposed async captures receive no later observations.");
        TestAssert.True(ReferenceEquals(environment, QwenEditorialPassDiagnostics.ProcessEnvironment(environment)),
            "An ended developer capture cannot enable production process timing.");
    }

    private static Task IndependentClockQuantizationIsBounded()
    {
        // The real provider's watchdog uses monotonic(), while the outer diagnostic uses
        // perf_counter(). Model the observed millisecond-shaped watchdog against a precise
        // call clock, not a synthesis-request identity failure or a change to model behavior.
        var passes = new List<QwenEditorialPassDiagnostic>();
        var captures = new List<QwenEditorialPassCaptureDiagnostic>();
        string actualShaped = Line()
            .Replace("\"generationCallWallSeconds\":5", "\"generationCallWallSeconds\":60.469536", StringComparison.Ordinal)
            .Replace("\"watchdogGenerationWallSeconds\":4", "\"watchdogGenerationWallSeconds\":60.484", StringComparison.Ordinal)
            .Replace("\"totalWallSeconds\":10", "\"totalWallSeconds\":65.469536", StringComparison.Ordinal);
        string tooLarge = Line(ordinal: 2)
            .Replace("\"watchdogGenerationWallSeconds\":4", "\"watchdogGenerationWallSeconds\":5.020001", StringComparison.Ordinal);
        using (QwenEditorialPassDiagnostics.Capture(passes.Add, captures.Add))
        {
            QwenEditorialPassDiagnostics.Report(string.Join('\n', actualShaped, tooLarge,
                "replayfoundry-editorial-pass: malformed", "ordinary non-diagnostic log"), [("candidate-1", 2)]);
            QwenEditorialPassDiagnostics.Report("ordinary log", [("candidate-1", 2)]);
        }
        TestAssert.Equal(1, passes.Count, "A small cross-clock quantization difference cannot silently discard a completed pass.");
        TestAssert.Equal(60.484, passes[0].WatchdogGenerationWallSeconds!.Value, "The original watchdog value is retained exactly, without clamping.");
        TestAssert.Equal(60.469536, passes[0].GenerationCallWallSeconds!.Value, "The precise containing-call measurement is also unchanged.");
        TestAssert.Equal(2, captures.Count, "Every captured process reports content-free parsing coverage, even with no timing records.");
        TestAssert.Equal(3, captures[0].ObservedLines, "Only prefixed diagnostic lines enter the coverage denominator.");
        TestAssert.Equal(1, captures[0].AcceptedRecords, "The summary counts the retained valid diagnostic.");
        TestAssert.Equal(2, captures[0].RejectedRecords, "Malformed and beyond-tolerance records are explicitly counted.");
        TestAssert.Equal(passes[0].ProcessBatchId, captures[0].ProcessBatchId, "Capture coverage belongs to its exact process group.");
        TestAssert.Equal(0, captures[1].ObservedLines, "An empty process report records no observed lines, not fabricated generation work.");
        return Task.CompletedTask;
    }
}
