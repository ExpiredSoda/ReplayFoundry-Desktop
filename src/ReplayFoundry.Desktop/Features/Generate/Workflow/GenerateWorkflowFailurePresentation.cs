using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.Progress;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal sealed class GenerateWorkflowFailureHandler
{
    private readonly IGenerateWorkflowHost _host;
    private readonly GenerationSourceSelectionState _sourceSelection;
    private readonly GenerationWorkflowSessionState _session;
    private readonly GenerationSourceValidityCoordinator _sourceValidity;

    public GenerateWorkflowFailureHandler(
        IGenerateWorkflowHost host,
        GenerationSourceSelectionState sourceSelection,
        GenerationWorkflowSessionState session,
        GenerationSourceValidityCoordinator sourceValidity)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _sourceSelection = sourceSelection ??
            throw new ArgumentNullException(nameof(sourceSelection));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _sourceValidity = sourceValidity ??
            throw new ArgumentNullException(nameof(sourceValidity));
    }

    public bool ValidateAfterDialog(
        GenerationSourcePreparationResult preparation,
        string friendlyPrefix,
        bool useEvidencePresentation)
    {
        GenerationSourceValidityFailure? failure =
            _sourceValidity.ValidateAfterDialog(friendlyPrefix);
        if (failure is null)
        {
            return true;
        }

        Show(
            useEvidencePresentation
                ? GenerationFailurePresentation.Evidence
                : GenerationFailurePresentation.Preparation,
            failure.FriendlyMessage,
            failure.Exception,
            preparation,
            initializeProgress: true);
        return false;
    }

    public bool EnsureFreshOrFail(
        GenerationSourcePreparationResult preparation,
        string? genericMessage,
        bool useEvidencePresentation)
    {
        GenerationSourceValidityFailure? failure =
            _sourceValidity.EnsureFresh(preparation, genericMessage);
        if (failure is null)
        {
            return true;
        }

        Show(
            useEvidencePresentation
                ? GenerationFailurePresentation.Evidence
                : GenerationFailurePresentation.Preparation,
            failure.FriendlyMessage,
            failure.Exception,
            preparation,
            initializeProgress: true);
        return false;
    }

    public void PresentEvidenceFailure(
        GenerationSourcePreparationResult preparation,
        string friendlyMessage,
        Exception exception)
    {
        if (_host.IsDisposed)
        {
            return;
        }

        Exception? freshnessFailure = _sourceValidity.CheckFreshness(preparation);
        if (freshnessFailure is not null)
        {
            _session.InvalidateAfterStaleSource();
            _host.Progress.FailEvidenceAnalysis(
                freshnessFailure.Message,
                freshnessFailure);
        }
        else
        {
            _host.Progress.FailEvidenceAnalysis(friendlyMessage, exception);
        }

        _host.WorkflowState = GenerateWorkflowState.Failed;
    }

    public void Show(
        GenerationFailurePresentation presentation,
        string friendlyMessage,
        Exception exception,
        GenerationSourcePreparationResult? preparation = null,
        bool initializeProgress = false)
    {
        if (_host.IsDisposed)
        {
            return;
        }

        int sourceCount = preparation?.Sources.Count ?? _sourceSelection.Count;
        GenerationFailurePresenter.Present(
            _host.Progress,
            presentation,
            friendlyMessage,
            exception,
            _host.SelectedGenerationMode,
            sourceCount,
            initializeProgress);
        _host.WorkflowState = GenerateWorkflowState.Failed;
    }
}
