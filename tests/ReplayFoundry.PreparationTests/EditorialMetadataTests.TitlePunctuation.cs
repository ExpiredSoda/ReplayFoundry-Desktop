using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static Task DanglingTitlePunctuationNeedsReview()
    {
        const string observed = "Boat approaches red light in dark water”,";
        const string description = "I followed the channel while waves moved around the hull.";
        var context = CreateContext();
        var request = new ClipEditorialMetadataRequest(context, ClipEditorialProfile.Default, 0,
            ClipEditorialGenerationPreference.AiRequired);
        string finalized = Qwen3VlGroundedMetadataResultParser.FinalizeAudienceTitle(
            observed, request, Qwen3VlGroundedMetadataGenerator.OutputSchema);
        TestAssert.Equal(observed + " #ExampleGame", finalized,
            "The observed punctuation survives existing tag finalization; the advisory must not silently rewrite provider copy.");
        foreach (string invalid in new[]
        {
            finalized,
            "The boat reached the light, #ExampleGame",
            "The boat reached the light; #ExampleGame",
            "The boat reached the light: #ExampleGame",
            "The boat reached the light” #ExampleGame",
            "The boat” reached the light #ExampleGame",
            "The boat reached the light\" #ExampleGame",
        })
        {
            TestAssert.True(ClipEditorialTitleCompletenessPolicy.HasDanglingPunctuation(invalid),
                "A dangling delimiter or unmatched closing double quote requires review: " + invalid);
            TestAssert.True(ClipEditorialMetadataQuality.Evaluate(invalid, description, context)
                    .Any(static issue => issue.SourceRuleCode == "IncompleteTitle"),
                "The mechanical defect must reach the shared advisory and existing retry/retention policy.");
            ClipAudiencePackagingAssessment assessment = ClipAudiencePackagingAssessment.Evaluate(invalid, description);
            TestAssert.True(assessment.Penalty >= 20 && assessment.Suggestions.Any(static value =>
                    value.Contains("complete supported thought", StringComparison.Ordinal)),
                "Packaging must expose useful complete-thought guidance without supplying an invented ending.");
        }
        foreach (string valid in new[]
        {
            "“The light changed” #ExampleGame", "\"The light changed\" #ExampleGame",
            "'The light changed' #ExampleGame", "‘The light changed’ #ExampleGame",
            "The boat’s lights changed #ExampleGame", "The boat's lights changed #ExampleGame",
            "The players’ boats returned #ExampleGame", "The players' boats returned #ExampleGame",
            "Did the light change? #ExampleGame", "The light changed! #ExampleGame",
            "The light changed… #ExampleGame", "The light changed... #ExampleGame",
            "“Did the light change?” #ExampleGame", "The \"red light\" changed #ExampleGame",
            "Warning: the light changed #ExampleGame", "The light changed, then stopped #ExampleGame",
            "The clearance measured 5\" #ExampleGame",
            "The 5\" panel \"fell\" #ExampleGame",
            "Panel \"13\" displayed \"Ready\" #ExampleGame",
        })
        {
            TestAssert.False(ClipEditorialTitleCompletenessPolicy.HasDanglingPunctuation(valid),
                "Valid quotations, apostrophes, internal punctuation and supported endings must remain intact: " + valid);
            TestAssert.False(ClipEditorialMetadataQuality.Evaluate(valid, description, context)
                    .Any(static issue => issue.SourceRuleCode == "IncompleteTitle"),
                "Other wording review must not turn valid punctuation into a completeness failure.");
        }
        return Task.CompletedTask;
    }

    private static async Task DanglingTitlePunctuationRetries()
    {
        var ai = new DanglingTitleMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(new RecordingFallbackMetadataGenerator(), ai);
        var request = new ClipEditorialMetadataRequest(CreateContext(), ClipEditorialProfile.Default, 0,
            ClipEditorialGenerationPreference.AiRequired);
        var diagnostics = new List<ClipEditorialRetryDiagnostic>();
        ClipEditorialMetadataDraft retained;
        using (ClipEditorialRetryDiagnostics.Capture(diagnostics.Add))
            retained = (await service.GenerateBatchAsync([request], CancellationToken.None)).Single();
        TestAssert.Equal(2, ai.BatchCalls, "The defective package needs one actual provider correction, with no blanket extra rewrite.");
        TestAssert.Equal("Red beacon marked the route #ExampleGame", retained.Title,
            "The provider-authored complete correction should be retained without invented local words.");
        TestAssert.Equal(1, retained.Attempt, "Retained provenance must identify the actual corrective attempt.");
        ClipEditorialRetryCaseDiagnostic decision = diagnostics.Single(static item => item.Event == "Started").Cases.Single();
        TestAssert.True(decision.SourceRuleCodes.Contains("IncompleteTitle") && decision.DifferentTitleRequired,
            "The exact punctuation defect should use existing completeness retry/retention semantics.");
    }

    private sealed class DanglingTitleMetadataGenerator : IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } = new("Title punctuation fixture", "1.0");
        public bool IsAvailable => true;
        public int BatchCalls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Batch fixture only.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = requests.Select(request => new ClipEditorialMetadataDraft(
                BatchCalls == 1 ? "Boat approaches red light in dark water”, #ExampleGame" : "Red beacon marked the route #ExampleGame",
                "I followed the channel while waves moved around the hull.", [request.Context.GameContext.GameName],
                ClipEditorialMetadataOrigin.AiAssisted, Identity, request.Attempt, request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity))).ToArray();
            return Task.FromResult(drafts);
        }
    }
}
