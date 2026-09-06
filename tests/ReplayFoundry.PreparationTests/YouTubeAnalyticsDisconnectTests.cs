using System.Net.Http;
using ReplayFoundry.Desktop.Platform.YouTube;

namespace ReplayFoundry.PreparationTests;

internal static partial class YouTubeAnalyticsTests
{
    private static async Task AnalyticsDisconnectIsLocal()
    {
        var store = new AnalyticsDisconnectStore();
        using var handler = new AnalyticsDisconnectHandler();
        using var http = new HttpClient(handler);
        using var authorization = new GoogleYouTubeAuthorizationService(
            new YouTubeOAuthClientConfiguration("fixture.apps.googleusercontent.com", "fixture", analyticsOnly: true),
            store, new AnalyticsDisconnectBrowser(), http, revokeOnDisconnect: false);
        await authorization.DisconnectAsync(CancellationToken.None);
        TestAssert.True(store.Deleted, "Disconnecting analytics must remove its local credential.");
        TestAssert.Equal(0, handler.Calls, "An analytics-only disconnect cannot revoke Google's project-wide grants or contact the network.");
        TestAssert.True(await authorization.GetAccessCredentialAsync(false, CancellationToken.None) is null,
            "The removed analytics credential cannot silently reconnect on the next refresh.");
    }
    private sealed class AnalyticsDisconnectStore : IYouTubeCredentialStore
    {
        public bool Deleted { get; private set; }
        public YouTubeStoredCredential? Read() => Deleted ? null : new("fixture-refresh", DateTimeOffset.UnixEpoch,
            [YouTubeOAuthClientConfiguration.AnalyticsReadOnlyScope]);
        public void Delete() => Deleted = true;
        public void Write(YouTubeStoredCredential credential) => throw new InvalidOperationException("Unexpected write.");
    }
    private sealed class AnalyticsDisconnectHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Unexpected request."); }
    }
    private sealed class AnalyticsDisconnectBrowser : ISystemBrowser
    {
        public void Open(Uri uri) => throw new InvalidOperationException("Unexpected browser launch.");
    }
}
