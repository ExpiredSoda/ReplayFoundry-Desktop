using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Platform.YouTube;

internal sealed partial class YouTubeDataApiClient
{
    private async Task<UploadStatus> QueryRecoverableUploadStatusAsync(
        Uri uploadSession,
        string accessToken,
        long totalLength,
        CancellationToken cancellationToken)
    {
        UploadStatus status = await QueryUploadStatusAsync(
                uploadSession,
                accessToken,
                totalLength,
                cancellationToken)
            .ConfigureAwait(false);
        for (int attempt = 1;
             status.VideoId is null &&
             status.Offset == totalLength &&
             attempt < MaximumFinalizationStatusAttempts;
             attempt++)
        {
            await Task.Delay(
                    TimeSpan.FromMilliseconds(250 * attempt),
                    cancellationToken)
                .ConfigureAwait(false);
            status = await QueryUploadStatusAsync(
                    uploadSession,
                    accessToken,
                    totalLength,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (status.VideoId is null && status.Offset == totalLength)
        {
            throw new YouTubePublishingException(
                "YouTube received every upload byte but did not finalize the video.",
                "youtube.upload.finalization-incomplete");
        }

        return status;
    }
}
