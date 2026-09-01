using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation.Workspaces;
using ReplayFoundry.Desktop.Shell;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task ShellServiceIconsReportTruthfulState()
    {
        var wikidata = new MutableGameKnowledgePermissionStatus();
        using var offline = CreateShell(
            gameKnowledgePermissionStatus: wikidata,
            localAiCapabilities:
                GenerationRuntimeCapabilities.DeterministicOnly);
        TestAssert.Equal(
            ShellServiceState.Offline,
            offline.LocalAiServiceState,
            "A local-only installation should use the quiet non-AI icon treatment.");
        TestAssert.Equal(
            "Local tools only",
            offline.LocalAiStatusLabel,
            "The top chrome should present local-only as a valid installed toolset.");
        TestAssert.Equal(
            "L",
            offline.LocalAiStatusMarker,
            "The local-only marker must identify the installed toolset without implying a failure.");
        TestAssert.Equal(
            "Icon.Local",
            offline.LocalAiStatusIcon,
            "A local-only installation should use the local-tools icon rather than an AI spark.");
        TestAssert.Equal(
            "Installed tools: Local tools only",
            offline.LocalAiAccessibleName,
            "Assistive technology should hear the same valid local-only state as sighted users.");
        TestAssert.Equal(
            "Local tools ready; AI tools unavailable",
            offline.LocalAiAccessibleStatus,
            "The non-color accessibility state must distinguish ready local tools from unavailable AI.");
        TestAssert.Equal(
            "Local tools are ready. AI tools are not available.",
            offline.LocalAiStatusDetail,
            "The top status should distinguish ready local tools from unavailable AI.");
        TestAssert.Equal(
            ShellServiceState.Offline,
            offline.WikidataServiceState,
            "Known disabled public lookup must show Wikidata offline.");
        TestAssert.Equal(
            "Wikidata status: Offline",
            offline.WikidataAccessibleName,
            "Assistive technology must receive both service identity and state.");

        wikidata.SetRememberedPermission(true);
        TestAssert.Equal(
            ShellServiceState.Ready,
            offline.WikidataServiceState,
            "Remembered permission must update Wikidata availability immediately.");
        TestAssert.Equal(
            "✓",
            offline.WikidataStatusMarker,
            "Available Wikidata must carry a check marker independent of color.");
        TestAssert.True(
            offline.WikidataStatusDetail.Contains(
                "confirmed game name",
                StringComparison.Ordinal),
            "The tooltip must explain what Wikidata receives rather than showing a bare state.");

        using var limited = CreateShell(
            gameKnowledgePermissionStatus: wikidata,
            localAiCapabilities: new GenerationRuntimeCapabilities(
                IsCaptionTranscriptionAvailable: true,
                IsSpeechActivityAvailable: true,
                IsVisualSemanticReviewAvailable: false,
                IsEditorialAiAvailable: false));
        TestAssert.Equal(
            ShellServiceState.Ready,
            limited.LocalAiServiceState,
            "Any installed AI tooling should select the AI-and-local top-chrome projection.");
        TestAssert.Equal(
            "✓",
            limited.LocalAiStatusMarker,
            "Installed AI tooling should use the positive installed marker rather than a warning.");
        TestAssert.Equal(
            "AI and local tools",
            limited.LocalAiStatusLabel,
            "The top chrome should not expose partial runtime diagnostics as a third user-facing state.");
        TestAssert.Equal(
            "Icon.Spark",
            limited.LocalAiStatusIcon,
            "An installation with any AI tooling should use the AI icon.");
        TestAssert.Equal(
            "Local AI ready",
            limited.LocalAiAccessibleStatus,
            "Assistive technology should identify the usable local-AI tier.");
        TestAssert.Equal(
            "Local AI is ready for captions and speech timing. Local tools are also ready.",
            limited.LocalAiStatusDetail,
            "The tooltip should name the usable local-AI tier and its ready work.");

        using var ready = CreateShell(
            gameKnowledgePermissionStatus: wikidata,
            localAiCapabilities: new GenerationRuntimeCapabilities(
                IsCaptionTranscriptionAvailable: true,
                IsSpeechActivityAvailable: true,
                IsVisualSemanticReviewAvailable: true,
                IsEditorialAiAvailable: true));
        TestAssert.Equal(
            ShellServiceState.Ready,
            ready.LocalAiServiceState,
            "Complete local visual and metadata AI must show Ready.");
        TestAssert.Equal(
            "Installed tools: AI and local tools",
            ready.LocalAiAccessibleName,
            "AI tooling accessibility text should describe the installed toolset without runtime jargon.");
        TestAssert.Equal(
            "Advanced AI ready",
            ready.LocalAiAccessibleStatus,
            "Assistive technology should identify the usable Advanced AI tier.");
        TestAssert.Equal(
            "Advanced AI is ready for picture review, title writing, captions, and speech timing. Local tools are also ready.",
            ready.LocalAiStatusDetail,
            "The ready state should identify Advanced AI and its user-facing work.");

        using var unknown = CreateShell();
        TestAssert.Equal(
            ShellServiceState.Degraded,
            unknown.LocalAiServiceState,
            "A missing runtime projection must not be presented as a known offline installation.");
        TestAssert.Equal(
            "Local tools are ready. AI availability could not be checked.",
            unknown.LocalAiStatusDetail,
            "An unknown AI state should stay plain and truthful.");
        TestAssert.Equal(
            ShellServiceState.Degraded,
            unknown.WikidataServiceState,
            "A missing permission projection must show status unavailable rather than inventing Offline or Available.");
        return Task.CompletedTask;
    }

    private static Task ShellAiIndicatorCapabilityMatrix()
    {
        for (int mask = 0; mask < 16; mask++)
        {
            bool captions = (mask & 1) != 0;
            bool speech = (mask & 2) != 0;
            bool picture = (mask & 4) != 0;
            bool editorial = (mask & 8) != 0;
            using var shell = CreateShell(
                localAiCapabilities: new GenerationRuntimeCapabilities(
                    IsCaptionTranscriptionAvailable: captions,
                    IsSpeechActivityAvailable: speech,
                    IsVisualSemanticReviewAvailable: picture,
                    IsEditorialAiAvailable: editorial));
            bool hasUsableAi = mask != 0;

            TestAssert.Equal(
                hasUsableAi
                    ? ShellServiceState.Ready
                    : ShellServiceState.Offline,
                shell.LocalAiServiceState,
                $"AI capability mask {mask} projected the wrong shell state.");
            TestAssert.Equal(
                hasUsableAi ? "✓" : "L",
                shell.LocalAiStatusMarker,
                $"AI capability mask {mask} projected the wrong non-color marker.");
            TestAssert.Equal(
                hasUsableAi ? "Icon.Spark" : "Icon.Local",
                shell.LocalAiStatusIcon,
                $"AI capability mask {mask} projected the wrong icon.");

            if (!hasUsableAi)
            {
                TestAssert.True(
                    shell.LocalAiStatusDetail.Contains(
                        "not available",
                        StringComparison.Ordinal),
                    "A confirmed installation without usable AI must remain visibly offline.");
                continue;
            }

            string expectedTier = picture || editorial
                ? "Advanced AI"
                : "Local AI";
            TestAssert.True(
                shell.LocalAiStatusDetail.StartsWith(
                    expectedTier + " is ready",
                    StringComparison.Ordinal),
                $"AI capability mask {mask} did not name the usable {expectedTier} tier.");
        }

        return Task.CompletedTask;
    }

    private sealed class MutableGameKnowledgePermissionStatus :
        IGameKnowledgePermissionStatus
    {
        public event EventHandler? Changed;

        public bool HasRememberedWikimediaPermission { get; private set; }

        public void SetRememberedPermission(bool value)
        {
            if (HasRememberedWikimediaPermission == value) return;
            HasRememberedWikimediaPermission = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
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

    private static async Task PublishStopWaitsForViewModelWorkflow()
    {
        var permission = new YouTubeConnectionPermissionState(
            new InMemoryYouTubeConnectionPermissionStore());
        permission.Enable(DateTimeOffset.UtcNow);
        var publish = CreateConnectivityPublishViewModel(
            new ConnectivityYouTubeService(),
            permission);
        var continuationReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseContinuation = new ManualResetEventSlim();
        publish.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName != nameof(PublishViewModel.IsInitializing) ||
                publish.IsInitializing) return;
            continuationReached.TrySetResult();
            releaseContinuation.Wait();
        };

        Task initialization = Task.Run(publish.InitializeAsync);
        try
        {
            await continuationReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task stop = ((IApplicationStopParticipant)publish).StopAsync(
                CancellationToken.None);
            TestAssert.False(
                stop.IsCompleted,
                "Publish stop must not finish while post-service view-model work is still running.");
            releaseContinuation.Set();
            await Task.WhenAll(initialization, stop)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseContinuation.Set();
            await initialization.WaitAsync(TimeSpan.FromSeconds(5));
            publish.Dispose();
        }
    }

    private static async Task PublishStopWaitsForCommandFinalization()
    {
        var permission = new YouTubeConnectionPermissionState(
            new InMemoryYouTubeConnectionPermissionStore());
        permission.Enable(DateTimeOffset.UtcNow);
        var publish = CreateConnectivityPublishViewModel(
            new ConnectivityYouTubeService(),
            permission);
        await publish.InitializeAsync();
        var command = (AsyncDelegateCommand)publish.RefreshYouTubeCommand;
        var finalizationReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFinalization = new ManualResetEventSlim();
        command.CanExecuteChanged += (_, _) =>
        {
            if (!command.CanExecute(null)) return;
            finalizationReached.TrySetResult();
            releaseFinalization.Wait();
        };

        Task execution = Task.Run(command.ExecuteAsync);
        Task stop = Task.CompletedTask;
        try
        {
            await finalizationReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stop = ((IApplicationStopParticipant)publish).StopAsync(
                CancellationToken.None);
            TestAssert.False(
                stop.IsCompleted,
                "Publish stop must await the command's final CanExecute notification.");
        }
        finally
        {
            releaseFinalization.Set();
            await Task.WhenAll(execution, stop)
                .WaitAsync(TimeSpan.FromSeconds(5));
            publish.Dispose();
        }
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
