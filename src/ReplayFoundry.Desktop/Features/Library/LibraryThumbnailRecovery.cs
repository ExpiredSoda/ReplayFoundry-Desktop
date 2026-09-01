namespace ReplayFoundry.Desktop.Features.Library;

internal interface ILibraryThumbnailRecoveryService
{
    Task<bool> TryRecoverAsync(
        LibraryMediaAsset asset,
        CancellationToken cancellationToken);
}
