using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using ReplayFoundry.Desktop;
using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Library.Sections;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.Sections;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Browser;
using ReplayFoundry.Desktop.Features.Studio.Inspector;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Presentation.Controls;
using ReplayFoundry.Desktop.Presentation.Converters;
using ReplayFoundry.Desktop.Presentation.Feedback;
using ReplayFoundry.Desktop.Presentation.Accessibility;
using ReplayFoundry.Desktop.Presentation.Workspaces;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Shell;
using ReplayFoundry.Desktop.Shell.Guidance;
using ReplayFoundry.Desktop.Shell.Navigation;
using ReplayFoundry.Desktop.Shell.Windowing;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static MainWindowViewModel CreateShell(
        StudioViewModel? studio = null,
        LibraryViewModel? library = null,
        PublishViewModel? publish = null,
        SettingsViewModel? settings = null) =>
        new(
            CreateGenerate(),
            studio ?? new StudioViewModel(),
            library ?? new LibraryViewModel(),
            publish ?? new PublishViewModel(),
            settings ?? new SettingsViewModel());

    private static object GetWorkspace(MainWindowViewModel shell, ShellDestination destination)
    {
        shell.NavigateCommand.Execute(destination);
        return shell.CurrentWorkspace;
    }

    private static int CountStates(params bool[] states) => states.Count(value => value);

    private static Application EnsureApplication()
    {
        Application application = Application.Current ??
            new App(suppressCompositionForResourceTests: true);
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (application.Resources.Count == 0)
        {
            ((App)application).InitializeComponent();
        }

        return application;
    }

    private static Dispatcher? _uiDispatcher;

    private static void RunOnSta(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_uiDispatcher is null)
        {
            var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                _uiDispatcher = dispatcher;
                dispatcher.BeginInvoke(
                    DispatcherPriority.Send,
                    new Action(ready.Set));
                Dispatcher.Run();
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
        }

        Dispatcher dispatcher = _uiDispatcher ??
            throw new InvalidOperationException(
                "The STA dispatcher was not initialized.");
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException(
                "The STA test dispatcher shut down before the UI harness completed.");
        }
        dispatcher.Invoke(action);
    }

    private static GenerateViewModel CreateGenerate()
    {
        var preparation = new EmptyPreparationCoordinator();
        var evidence = new EmptyEvidenceCoordinator();

        return new GenerateViewModel(
            new EmptyVideoFilePicker(),
            new TestMediaRightsConfirmation(),
            new EmptyGenerationSetupDialogService(),
            new EmptyCompositionReviewDialogService(),
            preparation,
            evidence,
            new EmptyGenerationRunner(),
            new GenerationSourceSelectionState(new VideoSourceValidator()),
            new GenerationWorkflowSessionState(preparation, evidence),
            new GenerationOperationController());
    }

    private sealed class EmptyVideoFilePicker : IVideoFilePicker
    {
        public IReadOnlyList<string> PickSingleVideo() => [];

        public IReadOnlyList<string> PickMultipleVideos() => [];
    }

    private sealed class EmptyGenerationSetupDialogService :
        IGenerationSetupDialogService
    {
        public GenerationSetupOptions? Show(
            GenerationSetupRequest request,
            GenerationSetupOptions? initialOptions) => null;
    }

    private sealed class EmptyCompositionReviewDialogService :
        IGenerationCompositionReviewDialogService
    {
        public GenerationCompositionReviewResult? Show(
            GenerationCompositionReviewRequest request,
            GenerationCompositionReviewResult? initialResult) => null;
    }

    private sealed class EmptyPreparationCoordinator :
        IGenerationSourcePreparationCoordinator
    {
        public GenerationSourcePreparationResult? Current => null;

        public Task<GenerationSourcePreparationResult> GetOrPrepareAsync(
            GenerationSourcePreparationRequest request,
            IProgress<GenerationSourcePreparationProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void EnsureFresh(GenerationSourcePreparationResult preparation)
        {
        }

        public void Invalidate()
        {
        }
    }

    private sealed class EmptyEvidenceCoordinator :
        IGenerationEvidenceAnalysisCoordinator
    {
        public GenerationEvidenceAnalysisSettings Settings { get; } =
            GenerationEvidenceAnalysisSettings.CreateDefault();

        public GenerationEvidenceAnalysisResult? Current => null;

        public Task<GenerationEvidenceAnalysisResult> GetOrAnalyzeAsync(
            GenerationEvidenceAnalysisRequest request,
            IProgress<GenerationEvidenceAnalysisProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Invalidate()
        {
        }
    }

    private sealed class EmptyGenerationRunner : IGenerationRunner
    {
        public Task<GenerationResult> RunAsync(
            GenerationRequest request,
            IProgress<GenerationProgressUpdate> progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingStudioClipRenderer :
        IStudioProjectRenderingService
    {
        public int CallCount { get; private set; }
        public int AcceptCallCount { get; private set; }
        public int DiscardCallCount { get; private set; }
        public GenerationOutputProject? LastDraft { get; private set; }

        public Task<StudioProjectRenderResult> FinalizeAsync(
            GenerationOutputProject draft,
            IProgress<StudioProjectRenderProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastDraft = draft;
            GenerationOutputAsset[] rendered = draft.IncludedAssets
                .Select(asset => asset.WithRenderedOutput(
                    Path.Combine(
                        draft.OutputDirectory,
                        $"studio-{asset.Rank:D3}.mp4")))
                .ToArray();
            GenerationOutputProject finalized = draft.Finalize(
                rendered,
                DateTimeOffset.UtcNow);
            return Task.FromResult(
                new StudioProjectRenderResult(
                    draft,
                    finalized,
                    TimeSpan.Zero));
        }

        public void AcceptCompletedRender(StudioProjectRenderResult result)
        {
            AcceptCallCount++;
        }

        public void DiscardCompletedRender(StudioProjectRenderResult result)
        {
            DiscardCallCount++;
        }
    }
}
