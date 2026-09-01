namespace ReplayFoundry.Desktop.Media.Preview;

public interface IVideoPreviewFrameProvider
{
    Task<VideoPreviewFrame> GetFrameAsync(
        VideoPreviewFrameRequest request,
        CancellationToken cancellationToken);
}
