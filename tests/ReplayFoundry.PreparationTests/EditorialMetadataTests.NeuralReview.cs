using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static async Task NeuralWordingKeepsDuplicateProtection()
    {
        var fallback = new RecordingFallbackMetadataGenerator();
        var provider = new NeuralWordingFixture();
        var service = new ClipEditorialMetadataGenerationService(fallback, provider);
        var requests = new[] { "one", "two" }.Select(id => new ClipEditorialMetadataRequest(
            CreateContext(candidateId:id), ClipEditorialProfile.Default, 0, ClipEditorialGenerationPreference.AiRequired)).ToArray();
        var drafts = await service.GenerateBatchAsync(requests, CancellationToken.None);
        TestAssert.True(provider.BatchSizes.SequenceEqual([2,1]),
            "Only the exact duplicate should retry; a literal musical beat and advisory phrasing must not override neural review.");
        TestAssert.Equal(2, drafts.Select(draft => draft.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Neural wording still requires distinct titles in a batch.");
        TestAssert.True(drafts.All(draft => draft.QualityIssues.Any(issue =>
            issue.Code == ClipEditorialMetadataQualityIssueCode.ThirdPersonCreatorFraming)),
            "An unmet writing preference remains visible for the user without controlling another model call.");
        TestAssert.Equal(0, fallback.Requests.Count, "Neural wording must not fall back to a heuristic author.");
    }

    private sealed class NeuralWordingFixture : IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } = new("Neural wording fixture", "1");
        public bool IsAvailable => true;
        public List<int> BatchSizes { get; } = [];
        public Task<ClipEditorialMetadataDraft> GenerateAsync(ClipEditorialMetadataRequest request, CancellationToken token) =>
            throw new InvalidOperationException("This fixture uses batch generation.");
        public Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken token)
        {
            BatchSizes.Add(requests.Count);
            return Task.FromResult<IReadOnlyList<ClipEditorialMetadataDraft>>(requests.Select(request => new ClipEditorialMetadataDraft(
                (request.Attempt == 0 ? "The Next Beat Ends the Drum Solo" : "The Final Note Rings Out") + " #ExampleGame",
                "A final cymbal strike brings the performance to a close.", ["ExampleGame"],
                ClipEditorialMetadataOrigin.AiAssisted, Identity, request.Attempt, request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity) with { NeuralCopyReview = new(.9,.9,new string('a',64)) },
                qualityIssues: [new(ClipEditorialMetadataQualityIssueCode.ThirdPersonCreatorFraming,
                    "The first-person writing preference was not used.")])).ToArray());
        }
    }
}
