using ReplayFoundry.Desktop.Features.Generate.Handoff;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

public interface IStudioPreviewPrewarmer
{
    Task PrewarmAsync(
        GenerationOutputProject project,
        string? priorityAssetId,
        CancellationToken cancellationToken);
}

public sealed class StudioPreviewPrewarmer : IStudioPreviewPrewarmer
{
    private readonly IStudioPreviewMediaService _mediaService;

    public StudioPreviewPrewarmer(IStudioPreviewMediaService mediaService)
    {
        _mediaService = mediaService ??
            throw new ArgumentNullException(nameof(mediaService));
    }

    public async Task PrewarmAsync(
        GenerationOutputProject project,
        string? priorityAssetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        GenerationOutputAsset[] ordered = project.Assets
            .OrderByDescending(asset =>
                asset.Id.Equals(priorityAssetId, StringComparison.Ordinal))
            .ThenBy(static asset => asset.Rank)
            .ToArray();
        foreach (GenerationOutputAsset asset in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using StudioPreviewMediaLease lease = await _mediaService.MaterializeAsync(
                    new StudioPreviewMediaRequest(asset, StudioPreviewRangeMode.ExactSelection,
                        StudioPreviewWorkIntent.BackgroundPrewarm),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A real foreground request superseded this optional same-key prewarm.
                // Its owner will prepare the entry; continue with the other candidates.
            }
        }
    }
}
