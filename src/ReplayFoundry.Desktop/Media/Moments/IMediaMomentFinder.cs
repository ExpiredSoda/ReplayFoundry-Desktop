namespace ReplayFoundry.Desktop.Media.Moments;

public interface IMediaMomentFinder
{
    MediaMomentFinderIdentity Identity { get; }

    MediaMomentFindingResult Find(
        MediaMomentFindingRequest request,
        CancellationToken cancellationToken = default);
}
