using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static async Task RetryDiagnosticsMatchProviderCalls()
    {
        var ai = new NoveltyRetryMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            new RecordingFallbackMetadataGenerator(), ai);
        ClipEditorialMetadataRequest[] requests = Enumerable.Range(1, 3)
            .Select(index => DiagnosticRequest($"candidate-{index:00}"))
            .ToArray();
        var events = new List<ClipEditorialRetryDiagnostic>();
        using (ClipEditorialRetryDiagnostics.Capture(events.Add))
        {
            await service.GenerateBatchAsync(requests, CancellationToken.None);
        }

        TestAssert.Equal(3, ai.BatchRequests.Count, "The fixture exercises two actual corrective batches.");
        TestAssert.True(events.Select(static item => item.Event).SequenceEqual(
            ["Started", "Completed", "Started", "Completed"]),
            "Each submitted corrective batch has exactly one start and completion.");
        TestAssert.Equal(1, events.Select(static item => item.BatchId).Distinct().Count(),
            "One correlation identity spans this service invocation.");
        TestAssert.True(events.All(static item => double.IsFinite(item.ElapsedSeconds) && item.ElapsedSeconds >= 0),
            "Batch timing must be finite and monotonic-clock based.");
        for (int index = 0; index < 2; index++)
        {
            ClipEditorialRetryDiagnostic started = events[index * 2];
            ClipEditorialRetryDiagnostic completed = events[index * 2 + 1];
            ClipEditorialMetadataRequest[] submitted = ai.BatchRequests[index + 1];
            TestAssert.Equal(index + 1, started.RetryRound, "The diagnostic identifies the actual bounded retry round.");
            TestAssert.True(started.RetryElapsedSeconds is null &&
                completed.RetryElapsedSeconds is >= 0 &&
                completed.ElapsedSeconds >= started.ElapsedSeconds,
                "A pending retry cannot invent its final duration.");
            TestAssert.True(started.Cases.Select(static item => item.CandidateId).SequenceEqual(
                submitted.Select(static item => item.Context.CandidateId)),
                "Already accepted rows must not appear among submitted retry cases.");
            for (int position = 0; position < submitted.Length; position++)
            {
                ClipEditorialRetryCaseDiagnostic decision = started.Cases[position];
                ClipEditorialMetadataRequest next = submitted[position];
                ClipEditorialMetadataRequest previous = ai.BatchRequests[index]
                    .Single(item => item.Context.CandidateId == decision.CandidateId);
                TestAssert.Equal(previous.Attempt, decision.Attempt, "Decision timing belongs to the just-reviewed attempt.");
                TestAssert.Equal(previous.VariantIntent.ToString(), decision.Variant, "The prior variant is preserved.");
                TestAssert.Equal(next.Attempt, decision.NextAttempt, "The next attempt matches the provider request exactly.");
                TestAssert.Equal(next.VariantIntent.ToString(), decision.NextVariant, "The next variant matches the provider request exactly.");
                TestAssert.True(decision.TitleNoveltyRejected && decision.DifferentTitleRequired,
                    "Abstract and colliding titles retain their actual novelty decision.");
                TestAssert.Equal(decision.CandidateId == "candidate-02" ? "TitleCollision" : "BannedAbstractFamily",
                    decision.TitleNoveltyRule, "The diagnostic distinguishes collision and abstract-title triggers.");
            }
        }
    }

    private static async Task RetryDiagnosticCapturesAreScoped()
    {
        var outer = new List<ClipEditorialRetryDiagnostic>();
        var inner = new List<ClipEditorialRetryDiagnostic>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new ClipEditorialRetryDiagnostic("scope-probe", "scope", 1, 0, null, []);
        using (ClipEditorialRetryDiagnostics.Capture(outer.Add))
        {
            IDisposable innerScope = ClipEditorialRetryDiagnostics.Capture(inner.Add);
            await GenerateDiagnosticDescriptionRetryAsync();
            Task inheritedCapture = Task.Run(async () =>
            {
                await release.Task;
                ClipEditorialRetryDiagnostics.Report(probe);
            });
            innerScope.Dispose();
            innerScope.Dispose();
            TestAssert.Equal(0, outer.Count, "The nested capture owns only its own observations.");
            TestAssert.Equal(2, inner.Count, "The inner capture saw a real retry start and completion.");
            release.SetResult();
            await inheritedCapture;
            TestAssert.Equal(2, inner.Count, "Disposed captures cannot receive events from an inherited async context.");
            await GenerateDiagnosticDescriptionRetryAsync();
            TestAssert.Equal(2, outer.Count, "Disposal restores the previous observer exactly once.");
            using (ClipEditorialRetryDiagnostics.Capture(static _ => throw new InvalidOperationException("diagnostic sink failed")))
            {
                await GenerateDiagnosticDescriptionRetryAsync();
            }
        }
        ClipEditorialRetryDiagnostics.Report(probe);
        TestAssert.Equal(2, outer.Count, "No event reaches a disposed outer capture.");
    }

    private static async Task GenerateDiagnosticDescriptionRetryAsync()
    {
        var ai = new DescriptionOnlyRetryMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            new RecordingFallbackMetadataGenerator(), ai);
        IReadOnlyList<ClipEditorialMetadataDraft> result = await service.GenerateBatchAsync(
            [DiagnosticRequest("diagnostic-description")], CancellationToken.None);
        TestAssert.Equal(2, ai.BatchRequests.Count, "Diagnostics cannot change the description correction count.");
        TestAssert.Equal(1, result.Count, "A failed telemetry observer cannot fail or erase generated copy.");
    }

    private static async Task ReconciledProvenanceDoesNotTriggerRetry()
    {
        var ai = new ReconciledProvenanceMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            new RecordingFallbackMetadataGenerator(), ai);
        var events = new List<ClipEditorialRetryDiagnostic>();
        IReadOnlyList<ClipEditorialMetadataDraft> result;
        using (ClipEditorialRetryDiagnostics.Capture(events.Add))
        {
            result = await service.GenerateBatchAsync([DiagnosticRequest("reconciled")], CancellationToken.None);
        }
        TestAssert.Equal(1, ai.BatchCalls, "Verified local provenance bookkeeping alone must not repeat inference.");
        TestAssert.Equal(0, events.Count, "No automatic retry may be reported when none is submitted.");
        TestAssert.True(result.Single().QualityIssues.Any(static issue =>
                issue.SourceRuleCode == "RerollDiversityProvenanceRecomputed") &&
            result.Single().Warnings.Any(static warning => warning.Code == ClipEditorialWarningCode.MetadataReviewRequired),
            "The informational advisory and visible review warning remain on the accepted draft.");
    }

    private static async Task ReconciledProvenancePreservesRealRetries()
    {
        foreach (string rule in new[] { "RerollTitleTooSimilar", "UnstableReadableTextReuse" })
        {
            var ai = new ReconciledProvenanceMetadataGenerator(rule);
            var service = new ClipEditorialMetadataGenerationService(
                new RecordingFallbackMetadataGenerator(), ai);
            var events = new List<ClipEditorialRetryDiagnostic>();
            using (ClipEditorialRetryDiagnostics.Capture(events.Add))
            {
                await service.GenerateBatchAsync([DiagnosticRequest("substantive")], CancellationToken.None);
            }
            TestAssert.Equal(2, ai.BatchCalls, "A substantive issue still requires an actual provider correction.");
            ClipEditorialRetryCaseDiagnostic decision = events.Single(static item => item.Event == "Started").Cases.Single();
            TestAssert.True(decision.SourceRuleCodes.SequenceEqual([rule]) &&
                decision.IssueCodes.Contains(nameof(ClipEditorialMetadataQualityIssueCode.AudienceCopyReview)),
                "The diagnostic records the substantive trigger, excluding only reconciled bookkeeping.");
            TestAssert.Equal(rule == "RerollTitleTooSimilar", decision.DifferentTitleRequired,
                "A description or grounding review must not be reported as a title-novelty correction.");
        }
    }

    private static ClipEditorialMetadataRequest DiagnosticRequest(string candidateId) =>
        new(CreateContext(candidateId: candidateId), ClipEditorialProfile.Default, 0,
            ClipEditorialGenerationPreference.AiRequired,
            variantIntent: ClipEditorialVariantIntent.DirectAction);

    private sealed class ReconciledProvenanceMetadataGenerator(string? substantiveRule = null) :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } = new("Retry diagnostics fixture", "1.0");
        public bool IsAvailable => true;
        public int BatchCalls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This fixture must use the real batch retry path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            string[] rules = BatchCalls == 1 && substantiveRule is not null
                ? ["RerollDiversityProvenanceRecomputed", substantiveRule]
                : ["RerollDiversityProvenanceRecomputed"];
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = requests.Select(request => new ClipEditorialMetadataDraft(
                BatchCalls == 1 ? "The Reactor Opened a New Route #ExampleGame" : "A Hidden Door Finally Opened #ExampleGame",
                "I completed one concrete visible action in the selected clip.",
                [request.Context.GameContext.GameName], ClipEditorialMetadataOrigin.AiAssisted,
                Identity, request.Attempt, request.Context.Evidence,
                warnings: [new ClipEditorialWarning(ClipEditorialWarningCode.MetadataReviewRequired, "Review the retained provenance notice.")],
                aiProvenance: CreateTestAiProvenance(Identity),
                qualityIssues: ClipEditorialMetadataReview.BuildIssues(rules))).ToArray();
            return Task.FromResult(drafts);
        }
    }
}
