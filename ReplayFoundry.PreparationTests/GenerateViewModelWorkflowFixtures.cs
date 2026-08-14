using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.RecentProjects;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerateViewModelWorkflowTests
{
    private static GenerationCompositionReviewResult
        CreateChangedCompositionReview(
            GenerationSourcePreparationResult preparation)
    {
        var request =
            new GenerationCompositionReviewRequest(
                preparation);

        return new GenerationCompositionReviewResult(
            request,
            preparation.Sources.Select(
                source =>
                {
                    var region =
                        new CompositionRegion(
                            "gameplay",
                            new NormalizedRectangle(
                                0.05,
                                0,
                                0.95,
                                1),
                            CompositionRegionRole.Gameplay,
                            CompositionRegionTraits.Dynamic,
                            CompositionConfidence.Certain,
                            CompositionConfidence.Certain,
                            CompositionValueSource.UserConfirmed,
                            CompositionValueSource.UserConfirmed);

                    return new PreparedSourceCompositionPlan(
                        source,
                        ManualCompositionPlanFactory
                            .CreateUserConfirmedSingleInterval(
                                source.Source.FullPath,
                                source.Media.Duration,
                                [region],
                                new DateTimeOffset(
                                    2026,
                                    7,
                                    29,
                                    12,
                                    0,
                                    0,
                                    TimeSpan.Zero)));
                }));
    }

    private static ViewModelContext CreateContext(
        IVideoFilePicker? videoFilePicker = null,
        bool seedSource = true,
        IMediaRightsConfirmation? mediaRightsConfirmation = null,
        IRecentGenerationProjectCatalog? recentProjectCatalog = null,
        IStudioProjectSwitchService? studioProjectSwitch = null,
        IRecentProjectsClearConfirmation? recentProjectsClearConfirmation = null)
    {
        string path =
            TestMediaFactory.CreateExistingSourcePath(
                "generate-workflow.mkv");

        var coordinator =
            new WorkflowPreparationCoordinator();

        var dialog =
            new RecordingGenerationSetupDialog();

        var runner =
            new RecordingGenerationRunner();

        var compositionDialog =
            new RecordingCompositionReviewDialog();

        var evidenceCoordinator =
            new WorkflowEvidenceCoordinator();

        var sourceSelection =
            new GenerationSourceSelectionState(
                new VideoSourceValidator());

        var workflowSession =
            new GenerationWorkflowSessionState(
                coordinator,
                evidenceCoordinator);

        var viewModel =
            new GenerateViewModel(
                videoFilePicker ??
                new EmptyVideoFilePicker(),
                mediaRightsConfirmation ?? new TestMediaRightsConfirmation(),
                dialog,
                compositionDialog,
                coordinator,
                evidenceCoordinator,
                runner,
                sourceSelection,
                workflowSession,
                new GenerationOperationController(),
                recentProjectCatalog: recentProjectCatalog,
                studioProjectSwitch: studioProjectSwitch,
                recentProjectsClearConfirmation: recentProjectsClearConfirmation);

        if (seedSource)
        {
            viewModel.AddDroppedFiles(
                [path]);
        }

        return new ViewModelContext(
            path,
            viewModel,
            coordinator,
            dialog,
            compositionDialog,
            evidenceCoordinator,
            runner,
            sourceSelection,
            workflowSession);
    }

    private sealed record ViewModelContext(
        string PrimaryPath,
        GenerateViewModel ViewModel,
        WorkflowPreparationCoordinator Coordinator,
        RecordingGenerationSetupDialog Dialog,
        RecordingCompositionReviewDialog CompositionDialog,
        WorkflowEvidenceCoordinator EvidenceCoordinator,
        RecordingGenerationRunner Runner,
        GenerationSourceSelectionState SourceSelection,
        GenerationWorkflowSessionState WorkflowSession);

    private sealed class EmptyVideoFilePicker :
        IVideoFilePicker
    {
        public IReadOnlyList<string> PickSingleVideo() =>
            [];

        public IReadOnlyList<string> PickMultipleVideos() =>
            [];
    }

    private sealed class RecordingVideoFilePicker :
        IVideoFilePicker
    {
        private readonly IReadOnlyList<string> _single;
        private readonly IReadOnlyList<string> _multiple;

        public RecordingVideoFilePicker(
            IReadOnlyList<string> single,
            IReadOnlyList<string> multiple)
        {
            _single = single;
            _multiple = multiple;
        }

        public IReadOnlyList<string> PickSingleVideo() =>
            _single;

        public IReadOnlyList<string> PickMultipleVideos() =>
            _multiple;
    }

    private sealed class WorkflowPreparationCoordinator :
        IGenerationSourcePreparationCoordinator
    {
        private TaskCompletionSource? _gate;

        public GenerationSourcePreparationResult? Current
        {
            get;
            private set;
        }

        public int PreparationRunCount { get; private set; }

        public bool CancellationObserved { get; private set; }

        public Exception? PreparationFailure { get; set; }

        public GenerationSourcePreparationException?
            FreshnessFailure
        {
            get;
            set;
        }

        public void BlockPreparation()
        {
            _gate =
                new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleasePreparation()
        {
            _gate?.TrySetResult();
        }

        public async Task<GenerationSourcePreparationResult>
            GetOrPrepareAsync(
                GenerationSourcePreparationRequest request,
                IProgress<GenerationSourcePreparationProgress>? progress,
                CancellationToken cancellationToken)
        {
            if (Current is not null)
            {
                return Current;
            }

            PreparationRunCount++;

            progress?.Report(
                new GenerationSourcePreparationProgress(
                    "Inspecting source",
                    "Synthetic preparation progress.",
                    45,
                    request.Sources[0].FileName,
                    1,
                    request.SourceCount));

            if (_gate is not null)
            {
                try
                {
                    await _gate.Task.WaitAsync(
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (PreparationFailure is not null)
            {
                throw PreparationFailure;
            }

            Current =
                new GenerationSourcePreparationResult(
                    request,
                    request.Sources.Select(
                        source =>
                            new PreparedGenerationSource(
                                source,
                                TestMediaFactory.Create(
                                    source.FullPath,
                                    hasAudio: true),
                                TestMediaFactory.CreateSnapshot(
                                    source.FullPath))));

            return Current;
        }

        public void EnsureFresh(
            GenerationSourcePreparationResult preparation)
        {
            if (!ReferenceEquals(
                    Current,
                    preparation))
            {
                throw new InvalidOperationException(
                    "The preparation result is not current.");
            }

            if (FreshnessFailure is not null)
            {
                Current = null;
                throw FreshnessFailure;
            }
        }

        public void Invalidate()
        {
            Current = null;
        }
    }

    private sealed class RecordingGenerationSetupDialog :
        IGenerationSetupDialogService
    {
        public List<GenerationSetupRequest> Requests { get; } = [];

        public GenerationSetupOptions? Result { get; set; }

        public Func<
            GenerationSetupRequest,
            GenerationSetupOptions?>? OnShow
        {
            get;
            set;
        }

        public GenerationSetupOptions? Show(
            GenerationSetupRequest request,
            GenerationSetupOptions? initialOptions)
        {
            Requests.Add(request);

            return OnShow?.Invoke(request) ??
                   Result;
        }
    }

    private sealed class WorkflowEvidenceCoordinator :
        IGenerationEvidenceAnalysisCoordinator
    {
        private static readonly ReplayFoundry.Desktop.Media.Analysis
            .MediaEvidenceAnalyzerIdentity AnalyzerIdentity =
            new(
                "workflow-test-analyzer",
                "3.0.0");

        private TaskCompletionSource? _gate;
        private GenerationEvidenceAnalysisFingerprint?
            _fingerprint;

        public GenerationEvidenceAnalysisSettings Settings
        {
            get;
        } =
            GenerationEvidenceAnalysisSettings
                .CreateDefault();

        public GenerationEvidenceAnalysisResult? Current
        {
            get;
            private set;
        }

        public List<GenerationEvidenceAnalysisRequest>
            Requests
        {
            get;
        } = [];

        public int AnalysisRunCount { get; private set; }

        public int CacheHitCount { get; private set; }

        public int InvalidationCount { get; private set; }

        public bool CancellationObserved { get; private set; }

        public Exception? Failure { get; set; }

        public void BlockAnalysis()
        {
            _gate =
                new TaskCompletionSource(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);
        }

        public void ReleaseAnalysis()
        {
            _gate?.TrySetResult();
        }

        public async Task<GenerationEvidenceAnalysisResult>
            GetOrAnalyzeAsync(
                GenerationEvidenceAnalysisRequest request,
                IProgress<GenerationEvidenceAnalysisProgress>? progress,
                CancellationToken cancellationToken)
        {
            Requests.Add(request);

            GenerationEvidenceAnalysisFingerprint fingerprint =
                GenerationEvidenceAnalysisFingerprint
                    .Create(
                        request,
                        AnalyzerIdentity);

            if (Current is not null &&
                fingerprint.Equals(
                    _fingerprint))
            {
                CacheHitCount++;

                GenerationEvidenceAnalysisResult cached =
                    Current;

                Current =
                    new GenerationEvidenceAnalysisResult(
                        request,
                        request.PreparedSources.Select(
                            (preparedSource, index) =>
                                new AnalyzedGenerationSource(
                                    preparedSource,
                                    request.SourcePlans[index],
                                    cached.Sources[index]
                                        .Evidence,
                                    cached.Sources[index]
                                        .Summary,
                                    request.Settings)));

                progress?.Report(
                    new GenerationEvidenceAnalysisProgress(
                        GenerationEvidenceAnalysisPhase
                            .UsingSavedEvidence,
                        "Using saved evidence",
                        "Synthetic saved evidence.",
                        sourceFileName: null,
                        sourceNumber: null,
                        sourceCount: null,
                        audioStreamIndex: null,
                        isIndeterminate: false,
                        overallPercentage: 100));

                return Current;
            }

            Current = null;
            _fingerprint = null;
            AnalysisRunCount++;

            progress?.Report(
                new GenerationEvidenceAnalysisProgress(
                    GenerationEvidenceAnalysisPhase
                        .StudyingSceneChanges,
                    "Studying scene changes",
                    "Synthetic evidence progress.",
                    request.PreparedSources[0]
                        .Source.FileName,
                    1,
                    request.SourceCount,
                    audioStreamIndex: null,
                    isIndeterminate: true,
                    overallPercentage: null));

            if (_gate is not null)
            {
                try
                {
                    await _gate.Task.WaitAsync(
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (Failure is not null)
            {
                throw Failure;
            }

            Current =
                TestMediaFactory
                    .CreateEvidenceAnalysisResult(
                        request,
                        AnalyzerIdentity.Name,
                        AnalyzerIdentity.Version);

            _fingerprint = fingerprint;

            return Current;
        }

        public void Invalidate()
        {
            InvalidationCount++;
            Current = null;
            _fingerprint = null;
        }
    }

    private sealed class RecordingGenerationRunner :
        IGenerationRunner
    {
        public List<GenerationRequest> Requests { get; } = [];

        public Task<GenerationResult> RunAsync(
            GenerationRequest request,
            IProgress<GenerationProgressUpdate> progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromException<GenerationResult>(
                new GenerationEngineUnavailableException(
                    "Synthetic intentional preflight stop."));
        }
    }

    private sealed class RecordingCompositionReviewDialog :
        IGenerationCompositionReviewDialogService
    {
        public List<GenerationCompositionReviewRequest>
            Requests
        {
            get;
        } = [];

        public List<GenerationCompositionReviewResult?>
            InitialResults
        {
            get;
        } = [];

        public bool Cancel { get; set; }

        public Func<
            GenerationCompositionReviewRequest,
            GenerationCompositionReviewResult?,
            GenerationCompositionReviewResult?>?
            OnShow
        {
            get;
            set;
        }

        public GenerationCompositionReviewResult? Show(
            GenerationCompositionReviewRequest request,
            GenerationCompositionReviewResult? initialResult)
        {
            Requests.Add(request);
            InitialResults.Add(initialResult);

            if (OnShow is not null)
            {
                return OnShow(
                    request,
                    initialResult);
            }

            if (Cancel)
            {
                return null;
            }

            return PreparedGenerationWorkflowTests
                .CreateCompositionReview(
                    request.Preparation);
        }
    }
}
