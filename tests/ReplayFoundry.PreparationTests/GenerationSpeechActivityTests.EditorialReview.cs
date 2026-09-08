using System.Security.Cryptography;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static async Task ExistingCutsRefreshSceneFacts()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ReplayFoundry", "SceneContextTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.mkv"), video = Path.Combine(directory, "review.mp4");
            await File.WriteAllBytesAsync(source, [1, 2]); await File.WriteAllBytesAsync(video, [3, 4]);
            var media = TestMediaFactory.Create(source, TimeSpan.FromMinutes(2));
            var input = new VisualSemanticInputManifest(video, Convert.ToHexString(SHA256.HashData([3, 4])), 2,
                TimeSpan.FromSeconds(30), new DateTimeOffset(File.GetLastWriteTimeUtc(video)));
            var context = new ClipEditorialContext("existing-cut", source, "Source", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40),
                media.Duration, 90, "Timing candidate");
            var request = new ClipEditorialMetadataRequest(context, ClipEditorialProfile.Default, 2,
                sourceMedia: media, reviewVideo: input);
            var settings = CreateVisualSettings(); var provider = new SceneContextTestProvider();
            var reviewer = new Qwen3VlEditorialSceneContextReviewer(provider, settings.Prompt, settings.Model);
            var refreshed = (await reviewer.ReviewAsync([request], CancellationToken.None))[0];
            TestAssert.True(Qwen3VlSceneCopyGenerator.CanUse(refreshed), "A current exact-cut review should authorize the scene writer.");
            TestAssert.Equal(2, refreshed.Attempt, "Refreshing facts must preserve the requested writing attempt.");
            await reviewer.ReviewAsync([refreshed], CancellationToken.None);
            TestAssert.Equal(1, provider.Calls, "An unchanged scene should reuse its verified facts.");
            await File.AppendAllTextAsync(source, "changed");
            TestAssert.False(Qwen3VlSceneCopyGenerator.CanUse(refreshed), "Changed source bytes invalidate retained facts.");
            provider.DuringReview = () => File.AppendAllText(source, "changed while reading");
            await TestAssert.ThrowsAsync<InvalidDataException>(() => reviewer.ReviewAsync([refreshed], CancellationToken.None),
                "A source changing during inference must not receive a fresh trusted binding.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class SceneContextTestProvider : IVisualSemanticEditorialProvider
    {
        public InferenceProviderIdentity Identity { get; } = new("Scene test", "1", "1");
        public int Calls { get; private set; }
        public Action? DuringReview { get; set; }
        public Task<VisualSemanticEditorialBatchResult> ObserveAsync(VisualSemanticBatchRequest request, CancellationToken cancellationToken)
        {
            Calls++; DuringReview?.Invoke();
            var value = JsonSerializer.SerializeToElement(new { setup = "A vehicle approaches an aircraft.",
                @event = "The vehicle hits the aircraft and bursts into flames.", outcome = "Flames cover the vehicle.",
                firstFrame = 0, lastFrame = 11, kind = "Action", hasDistinctEvent = "Yes", hasPayoff = "Yes",
                onlyRoutineMovementOrMenus = "No", needsEarlierContext = "No", onlyLightingOrCameraChanges = "No",
                transcriptSupport = "NotSupplied", editorialValue = 85, recommendation = "Keep" });
            var results = request.Requests.Select(item => Qwen3VlSceneReviewProvider.ParseAssessment(item, value,
                JsonSerializer.SerializeToElement(Enumerable.Range(0, 12).Select(i => i * (item.CandidateEndRelative.TotalSeconds - .1) / 11)), TimeSpan.Zero));
            return Task.FromResult(new VisualSemanticEditorialBatchResult(request, results, TimeSpan.Zero, null));
        }
    }
}
