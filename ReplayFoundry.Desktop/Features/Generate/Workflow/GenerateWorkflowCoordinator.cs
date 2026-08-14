using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.Progress;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal interface IGenerateWorkflowHost
{
    GenerationMode SelectedGenerationMode { get; }

    GenerateWorkflowState WorkflowState { get; set; }

    GenerationProgressViewModel Progress { get; }

    bool IsDisposed { get; }

    void RefreshCommandState();
}

internal sealed class GenerateWorkflowCoordinator
{
    private readonly IGenerationSetupDialogService _setupDialog;
    private readonly IGenerationCompositionReviewDialogService _compositionDialog;
    private readonly GenerationSourceSelectionState _sourceSelection;
    private readonly GenerationWorkflowSessionState _session;
    private readonly GenerationRuntimeCapabilities _runtimeCapabilities;
    private readonly GenerationSourceValidityCoordinator _sourceValidity;
    private readonly GenerateWorkflowPreparationStage _preparationStage;
    private readonly GenerateWorkflowEvidenceStage _evidenceStage;
    private readonly GenerateWorkflowExecutionStage _executionStage;
    private readonly GenerateWorkflowFailureHandler _failureHandler;
    private readonly IGenerateWorkflowHost _host;

    public GenerateWorkflowCoordinator(
        IGenerationSetupDialogService setupDialog,
        IGenerationCompositionReviewDialogService compositionDialog,
        IGenerationSourcePreparationCoordinator preparationCoordinator,
        IGenerationEvidenceAnalysisCoordinator evidenceCoordinator,
        IGenerationRunner generationRunner,
        GenerationSourceSelectionState sourceSelection,
        GenerationWorkflowSessionState session,
        GenerationOperationController operations,
        GenerationRuntimeCapabilities runtimeCapabilities,
        IGenerateWorkflowHost host)
    {
        _setupDialog = setupDialog ?? throw new ArgumentNullException(nameof(setupDialog));
        _compositionDialog = compositionDialog ??
            throw new ArgumentNullException(nameof(compositionDialog));
        ArgumentNullException.ThrowIfNull(preparationCoordinator);
        ArgumentNullException.ThrowIfNull(evidenceCoordinator);
        ArgumentNullException.ThrowIfNull(generationRunner);
        _sourceSelection = sourceSelection ??
            throw new ArgumentNullException(nameof(sourceSelection));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(operations);
        _runtimeCapabilities = runtimeCapabilities ??
            throw new ArgumentNullException(nameof(runtimeCapabilities));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _sourceValidity = new GenerationSourceValidityCoordinator(
            sourceSelection,
            preparationCoordinator,
            session);
        _failureHandler = new GenerateWorkflowFailureHandler(
            host,
            sourceSelection,
            session,
            _sourceValidity);
        _preparationStage = new GenerateWorkflowPreparationStage(
            preparationCoordinator,
            session,
            operations,
            host,
            _failureHandler);
        _evidenceStage = new GenerateWorkflowEvidenceStage(
            evidenceCoordinator,
            session,
            operations,
            host,
            _failureHandler);
        _executionStage = new GenerateWorkflowExecutionStage(
            generationRunner,
            operations,
            host,
            _failureHandler);
    }

    public async Task RunAsync()
    {
        if (!_sourceSelection.HasSources)
        {
            throw new InvalidOperationException(
                "Generation Setup requires at least one selected video.");
        }

        if (!_sourceValidity.RevalidateSelection())
        {
            return;
        }

        var preparationRequest = new GenerationSourcePreparationRequest(
            _sourceSelection.CreateSnapshot());
        GenerationSourcePreparationResult? preparation =
            await _preparationStage.PrepareAsync(preparationRequest);
        if (preparation is null || _host.IsDisposed)
        {
            return;
        }

        _host.Progress.Reset();
        _host.WorkflowState = GenerateWorkflowState.SourceSelection;
        GenerationSetupOptions? setup = ShowGenerationSetup(preparation);
        if (setup is null)
        {
            return;
        }

        if (setup.Mode != _host.SelectedGenerationMode)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Preparation,
                "Generation Setup returned settings for a different mode.",
                new InvalidOperationException(
                    "The returned Generation Setup mode did not match the selected mode."),
                preparation,
                initializeProgress: true);
            return;
        }

        _session.SetSetup(setup);
        if (!_failureHandler.ValidateAfterDialog(
                preparation,
                "A selected source changed while Generation Setup was open. " +
                "Prepare the sources again before continuing.",
                useEvidencePresentation: false) ||
            !_failureHandler.EnsureFreshOrFail(
                preparation,
                "Replay Foundry could not verify that the prepared sources are still current.",
                useEvidencePresentation: false))
        {
            return;
        }

        GenerationCompositionReviewResult? composition =
            ShowCompositionReview(preparation);
        if (_host.IsDisposed)
        {
            return;
        }

        _host.WorkflowState = GenerateWorkflowState.SourceSelection;
        if (composition is null)
        {
            return;
        }

        _session.SetComposition(composition);
        if (!_failureHandler.ValidateAfterDialog(
                preparation,
                "A selected source changed while Video Layout Review was open. " +
                "Prepare and review the sources again before continuing.",
                useEvidencePresentation: false) ||
            !_failureHandler.EnsureFreshOrFail(
                preparation,
                "Replay Foundry could not verify that the reviewed sources are still current.",
                useEvidencePresentation: false))
        {
            return;
        }

        try
        {
            GenerationPreflightValidator.ValidateSupportedInputs(
                preparation,
                setup,
                composition,
                _runtimeCapabilities);
        }
        catch (Exception exception)
            when (exception is GenerationEngineUnavailableException or GenerationSourceException)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Evidence,
                exception.Message,
                exception,
                preparation,
                initializeProgress: true);
            return;
        }

        var evidenceRequest = new GenerationEvidenceAnalysisRequest(
            preparation,
            composition,
            GenerationEvidenceAnalysisSettings.CreateForDepth(setup.AnalysisDepth));
        GenerationEvidenceAnalysisResult? evidence =
            await _evidenceStage.AnalyzeAsync(evidenceRequest);
        if (evidence is null || _host.IsDisposed)
        {
            return;
        }

        if (!_failureHandler.ValidateAfterDialog(
                preparation,
                "A selected source changed after evidence analysis. " +
                "Prepare and review the sources again before continuing.",
                useEvidencePresentation: true) ||
            !_failureHandler.EnsureFreshOrFail(
                preparation,
                genericMessage: null,
                useEvidencePresentation: true))
        {
            return;
        }

        _sourceSelection.ReportValidation(null);
        try
        {
            _executionStage.Start(
                new GenerationRequest(preparation, setup, composition, evidence));
        }
        catch (Exception exception)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Evidence,
                "Replay Foundry could not start final generation preflight.",
                exception,
                preparation,
                initializeProgress: true);
        }
    }

    private GenerationSetupOptions? ShowGenerationSetup(
        GenerationSourcePreparationResult preparation)
    {
        try
        {
            return _setupDialog.Show(
                new GenerationSetupRequest(
                    _host.SelectedGenerationMode,
                    preparation),
                _session.Setup);
        }
        catch (Exception exception)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Preparation,
                "Replay Foundry could not open Generation Setup.",
                exception,
                preparation,
                initializeProgress: true);
            return null;
        }
    }

    private GenerationCompositionReviewResult? ShowCompositionReview(
        GenerationSourcePreparationResult preparation)
    {
        try
        {
            _host.WorkflowState = GenerateWorkflowState.ReviewingComposition;
            return _compositionDialog.Show(
                new GenerationCompositionReviewRequest(preparation),
                _session.Composition);
        }
        catch (Exception exception)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Preparation,
                "Replay Foundry could not open Video Layout Review.",
                exception,
                preparation,
                initializeProgress: true);
            return null;
        }
    }
}
