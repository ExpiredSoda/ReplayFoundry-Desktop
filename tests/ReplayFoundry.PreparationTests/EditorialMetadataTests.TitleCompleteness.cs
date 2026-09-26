using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static async Task IncompleteModifierCannotOutrankCompleteDraft()
    {
        var ai = new IncompleteModifierMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            new RecordingFallbackMetadataGenerator(), ai);
        var request = new ClipEditorialMetadataRequest(CreateContext(), ClipEditorialProfile.Default, 0,
            ClipEditorialGenerationPreference.AiRequired);
        var events = new List<ClipEditorialRetryDiagnostic>();
        ClipEditorialMetadataDraft retained;
        using (ClipEditorialRetryDiagnostics.Capture(events.Add))
        {
            retained = (await service.GenerateBatchAsync([request], CancellationToken.None)).Single();
        }
        TestAssert.Equal(3, ai.BatchCalls, "The existing initial call plus two retries remains the hard limit.");
        TestAssert.Equal("Headlamp revealed a submerged tunnel #ExampleGame", retained.Title,
            "A complete earlier AI title must beat later incomplete clauses even when the provider omitted an incomplete-title warning.");
        TestAssert.Equal(0, retained.Attempt, "Retention must preserve the attempt that actually authored the complete package.");
        ClipEditorialRetryCaseDiagnostic secondDecision = events.Single(static item =>
            item.Event == "Started" && item.RetryRound == 2).Cases.Single();
        TestAssert.True(secondDecision.SourceRuleCodes.Contains("IncompleteTitle") && secondDecision.DifferentTitleRequired,
            "The shared advisory must flow through exact retry diagnostics and the existing different-title policy.");
    }

    private sealed class IncompleteModifierMetadataGenerator : IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } = new("Title completeness fixture", "1.0");
        public bool IsAvailable => true;
        public int BatchCalls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The fixture requires the shared batch retry path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = requests.Select(request => new ClipEditorialMetadataDraft(
                BatchCalls == 1
                    ? "Headlamp revealed a submerged tunnel #ExampleGame"
                    : "Figure swims through dark underwater tunnel with headlamp beam cutting #ExampleGame",
                BatchCalls == 1
                    ? "I swam past drifting debris before reaching a narrow opening."
                    : "A figure swims through a dark underwater tunnel, headlamp cutting through murky water and floating debris.",
                [request.Context.GameContext.GameName], ClipEditorialMetadataOrigin.AiAssisted,
                Identity, request.Attempt, request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity),
                qualityIssues: BatchCalls == 1
                    ? ClipEditorialMetadataReview.BuildIssues(["EditorialFrameDrift"])
                    : [])).ToArray();
            return Task.FromResult(drafts);
        }
    }
}
