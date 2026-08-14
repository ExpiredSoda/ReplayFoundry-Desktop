using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal sealed class GenerateWorkflowEvidenceStage
{
    private readonly IGenerationEvidenceAnalysisCoordinator _coordinator;
    private readonly GenerationWorkflowSessionState _session;
    private readonly GenerationOperationController _operations;
    private readonly IGenerateWorkflowHost _host;
    private readonly GenerateWorkflowFailureHandler _failureHandler;

    public GenerateWorkflowEvidenceStage(
        IGenerationEvidenceAnalysisCoordinator coordinator,
        GenerationWorkflowSessionState session,
        GenerationOperationController operations,
        IGenerateWorkflowHost host,
        GenerateWorkflowFailureHandler failureHandler)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _failureHandler = failureHandler ??
            throw new ArgumentNullException(nameof(failureHandler));
    }

    public async Task<GenerationEvidenceAnalysisResult?> AnalyzeAsync(
        GenerationEvidenceAnalysisRequest request)
    {
        using GenerationOperationLease operation =
            _operations.Begin(GenerationOperationKind.EvidenceAnalysis);
        _host.Progress.BeginEvidenceAnalysis(
            _host.SelectedGenerationMode,
            request.SourceCount);
        _host.WorkflowState = GenerateWorkflowState.AnalyzingEvidence;
        var progress = new SynchronousProgress<GenerationEvidenceAnalysisProgress>(
            _host.Progress.ReportEvidenceAnalysis);

        try
        {
            GenerationEvidenceAnalysisResult result =
                await _coordinator.GetOrAnalyzeAsync(
                    request,
                    progress,
                    operation.CancellationToken);
            _session.AcceptEvidence(result);
            return result;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (!_host.IsDisposed)
            {
                _host.Progress.MarkEvidenceAnalysisCancelled();
                _host.WorkflowState = GenerateWorkflowState.Cancelled;
            }

            return null;
        }
        catch (GenerationSourcePreparationException exception)
        {
            if (!_host.IsDisposed)
            {
                _session.InvalidateAfterStaleSource();
                _host.Progress.FailEvidenceAnalysis(exception.Message, exception);
                _host.WorkflowState = GenerateWorkflowState.Failed;
            }

            return null;
        }
        catch (GenerationEvidenceAnalysisException exception)
        {
            _failureHandler.PresentEvidenceFailure(
                request.Preparation,
                exception.Message,
                exception);
            return null;
        }
        catch (Exception exception)
        {
            _failureHandler.PresentEvidenceFailure(
                request.Preparation,
                "Replay Foundry could not analyze deterministic evidence for the selected videos.",
                exception);
            return null;
        }
        finally
        {
            _host.RefreshCommandState();
        }
    }

}
