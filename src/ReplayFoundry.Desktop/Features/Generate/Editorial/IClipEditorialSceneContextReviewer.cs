using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public interface IClipEditorialSceneContextReviewer
{
    Task<IReadOnlyList<ClipEditorialMetadataRequest>> ReviewAsync(
        IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken);
}
