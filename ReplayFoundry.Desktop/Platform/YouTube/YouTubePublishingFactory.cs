using System.Net;
using System.Net.Http;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.YouTube;

public static class YouTubePublishingFactory
{
    public static IYouTubePublishingService? CreateDefault(
        IYouTubeConnectionPermission connectionPermission)
    {
        ArgumentNullException.ThrowIfNull(connectionPermission);
        YouTubeOAuthClientConfiguration? configuration =
            YouTubeOAuthConfigurationLoader.TryLoad();
        if (configuration is null)
        {
            return null;
        }
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        var httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ReplayFoundry/1.0");
        var authorization = new GoogleYouTubeAuthorizationService(
            configuration,
            new WindowsCredentialYouTubeTokenStore(configuration.ClientId),
            new WindowsSystemBrowser(),
            httpClient);
        return new YouTubePublishingService(
            authorization,
            new YouTubeDataApiClient(httpClient),
            new JsonYouTubePublishHistoryStore(),
            connectionPermission,
            httpClient);
    }
}
