using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Presentation.Workspaces;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static async Task ShellConnectivityStatusFollowsPermission()
    {
        var permission = new YouTubeConnectionPermissionState(
            new InMemoryYouTubeConnectionPermissionStore());
        var youtube = new ConnectivityYouTubeService();
        var publish = CreateConnectivityPublishViewModel(
            youtube,
            permission);
        var settings = new SettingsViewModel(permission);
        using var shell = CreateShell(
            publish: publish,
            settings: settings);

        TestAssert.Equal(
            "Local only",
            shell.ConnectivityStatusText,
            "The shell should not imply network access before opt-in.");

        settings.EnableYouTubeConnectionsCommand.Execute(null);
        TestAssert.Equal(
            "Online enabled",
            shell.ConnectivityStatusText,
            "Permission alone should say online is enabled without claiming a connection.");
        TestAssert.Equal(
            0,
            youtube.GetConnectionCalls,
            "Enabling permission must not make a connection attempt.");

        await publish.InitializeAsync();
        TestAssert.Equal(
            "YouTube connected",
            shell.ConnectivityStatusText,
            "A validated channel should be reflected separately from permission.");

        settings.DisableYouTubeConnectionsCommand.Execute(null);
        await youtube.Disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        TestAssert.Equal(
            "Local only",
            shell.ConnectivityStatusText,
            "Disabling should immediately restore truthful local-only chrome.");
        TestAssert.False(
            publish.IsConnected,
            "Disabling should clear the Publish connection projection.");
    }

    private static async Task PublishInitializationRespectsPermission()
    {
        var permission = new YouTubeConnectionPermissionState(
            new InMemoryYouTubeConnectionPermissionStore());
        var youtube = new ConnectivityYouTubeService();
        using var publish = CreateConnectivityPublishViewModel(
            youtube,
            permission);

        await publish.InitializeAsync();

        TestAssert.Equal(
            0,
            youtube.GetConnectionCalls,
            "Opening Publish in local-only mode must not call a network-facing service.");
        TestAssert.False(
            publish.ConnectCommand.CanExecute(null),
            "Connect must remain disabled until Settings grants permission.");
        TestAssert.True(
            publish.ConnectionStatus.Contains(
                "Settings",
                StringComparison.Ordinal),
            "Publish should direct the user to the explicit privacy control.");
    }

    private static async Task PublishDisposalCancelsInitialization()
    {
        var permission = new YouTubeConnectionPermissionState(
            new InMemoryYouTubeConnectionPermissionStore());
        permission.Enable(DateTimeOffset.UtcNow);
        var youtube = new BlockingConnectivityYouTubeService();
        var publish = CreateConnectivityPublishViewModel(
            youtube,
            permission);

        Task initialization = publish.InitializeAsync();
        await youtube.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        publish.Dispose();
        await initialization.WaitAsync(TimeSpan.FromSeconds(5));

        TestAssert.True(
            youtube.CancellationObserved,
            "Publish disposal must cancel a connection operation rather than leaving it running after navigation or shutdown.");
    }

    private static PublishViewModel CreateConnectivityPublishViewModel(
        IYouTubePublishingService youtube,
        IYouTubeConnectionPermission permission) =>
        new(
            EmptyLibraryCatalog.Instance,
            youtube,
            new InMemoryYouTubePublishPreferencesStore(),
            thumbnailPicker: null,
            WorkspaceSurfaceState.Empty,
            static () => DateTimeOffset.UtcNow,
            TimeZoneInfo.Utc,
            permission);

    private sealed class ConnectivityYouTubeService :
        IYouTubePublishingService
    {
        public bool IsConfigured => true;
        public IReadOnlyList<YouTubePublishHistoryEntry> History => [];
        public int GetConnectionCalls { get; private set; }
        public TaskCompletionSource Disconnected { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<YouTubeAccountConnection?> GetConnectionAsync(
            CancellationToken cancellationToken)
        {
            GetConnectionCalls++;
            return Task.FromResult<YouTubeAccountConnection?>(
                new YouTubeAccountConnection(
                    "channel-1",
                    "Creator channel",
                    DateTimeOffset.UtcNow));
        }

        public Task<YouTubeAccountConnection> ConnectAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new YouTubeAccountConnection(
                "channel-1",
                "Creator channel",
                DateTimeOffset.UtcNow));

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Disconnected.TrySetResult();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<YouTubePlaylist>> GetPlaylistsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<YouTubePlaylist>>([]);

        public Task<IReadOnlyList<YouTubeVideoCategory>> GetCategoriesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<YouTubeVideoCategory>>(
                [new YouTubeVideoCategory("20", "Gaming")]);

        public Task<YouTubePublishResult> PublishAsync(
            YouTubePublishRequest request,
            IProgress<YouTubePublishProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ReconcileHistoryAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public void ClearHistory()
        {
        }
    }

    private sealed class BlockingConnectivityYouTubeService :
        IYouTubePublishingService
    {
        public bool IsConfigured => true;
        public IReadOnlyList<YouTubePublishHistoryEntry> History => [];
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task<YouTubeAccountConnection?> GetConnectionAsync(
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            return null;
        }

        public Task<YouTubeAccountConnection> ConnectAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<YouTubePlaylist>> GetPlaylistsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<YouTubePlaylist>>([]);

        public Task<IReadOnlyList<YouTubeVideoCategory>> GetCategoriesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<YouTubeVideoCategory>>([]);

        public Task<YouTubePublishResult> PublishAsync(
            YouTubePublishRequest request,
            IProgress<YouTubePublishProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ReconcileHistoryAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public void ClearHistory()
        {
        }
    }
}
