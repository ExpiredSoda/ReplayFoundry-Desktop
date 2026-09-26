using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public static class MontageEditorialRequests
{
    public static IReadOnlyList<ClipEditorialMetadataRequest> Create(GenerationOutputProject project, ClipEditorialProfile profile)
    {
        if (project.IncludedCount == 0) throw new InvalidOperationException("Keep at least one section in the montage.");
        return project.IncludedAssets.Select(asset => new ClipEditorialMetadataRequest(
            asset.CreateCurrentCutEditorialContext().PrepareForEditorialGeneration(), profile,
            (project.MontageMetadata?.Attempt ?? 0) + 1, ClipEditorialGenerationPreference.AiRequired, asset.SourceMedia,
            priorAcceptedTitleExclusions: project.IsMontageMetadataCurrent
                ? project.MontageMetadata!.CreatePriorTitleExclusions(asset.CreateCurrentCutEditorialContext()) : [])).ToArray();
    }
}

public sealed partial class ClipEditorialMetadataGenerationService
{
    public async Task<ClipEditorialMetadataDraft> GenerateMontageAsync(
        IReadOnlyList<ClipEditorialMetadataRequest> sequence, CancellationToken cancellationToken)
    {
        if (sequence.Count is < 1 or > 30) throw new ArgumentException("A montage needs between one and thirty reviewed cuts.");
        if (!IsAiAvailable || _ai is not Qwen3VlGroundedMetadataGenerator writer || _sceneReviewer is null)
            throw new InvalidOperationException("The local scene writer must be available to write the whole montage.");
        var media = new List<MaterializedVisualSemanticReviewVideo>();
        try
        {
            var requests = new List<ClipEditorialMetadataRequest>();
            foreach (var request in sequence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clip = await MaterializeReviewVideoAsync(request, cancellationToken);
                media.Add(clip);
                requests.Add(request.WithReviewVideo(clip.Input));
            }
            var reviewed = await _sceneReviewer.ReviewAsync(requests, cancellationToken);
            if (reviewed.Count != sequence.Count) throw new InvalidOperationException("Every montage section must retain its own evidence.");
            return await writer.GenerateSequenceAsync(reviewed, cancellationToken);
        }
        finally { foreach (var clip in media) clip.Dispose(); }
    }
}
