using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.Progress;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal sealed class GenerateWorkflowPreparationStage
{
    private readonly IGenerationSourcePreparationCoordinator _coordinator;
    private readonly GenerationWorkflowSessionState _session;
    private readonly GenerationOperationController _operations;
    private readonly IGenerateWorkflowHost _host;
    private readonly GenerateWorkflowFailureHandler _failureHandler;

    public GenerateWorkflowPreparationStage(
        IGenerationSourcePreparationCoordinator coordinator,
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

    public async Task<GenerationSourcePreparationResult?> PrepareAsync(
        GenerationSourcePreparationRequest request)
    {
        using GenerationOperationLease operation =
            _operations.Begin(GenerationOperationKind.SourcePreparation);
        _host.Progress.BeginPreparation(
            _host.SelectedGenerationMode,
            request.SourceCount);
        _host.WorkflowState = GenerateWorkflowState.PreparingSources;
        var progress = new SynchronousProgress<GenerationSourcePreparationProgress>(
            _host.Progress.ReportPreparation);

        try
        {
            GenerationSourcePreparationResult result =
                await _coordinator.GetOrPrepareAsync(
                    request,
                    progress,
                    operation.CancellationToken);
            _session.AcceptPreparation(result);
            return result;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (!_host.IsDisposed)
            {
                _host.Progress.MarkPreparationCancelled();
                _host.WorkflowState = GenerateWorkflowState.Cancelled;
            }

            return null;
        }
        catch (GenerationSourcePreparationException exception)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Preparation,
                exception.Message,
                exception);
            return null;
        }
        catch (MediaToolNotFoundException exception)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Preparation,
                exception.Message,
                exception);
            return null;
        }
        catch (Exception exception)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Preparation,
                "Replay Foundry could not prepare the selected videos.",
                exception);
            return null;
        }
        finally
        {
            _host.RefreshCommandState();
        }
    }
}
