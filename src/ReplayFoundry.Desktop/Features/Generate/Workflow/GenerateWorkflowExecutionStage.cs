using ReplayFoundry.Desktop.Features.Generate.Progress;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal sealed class GenerateWorkflowExecutionStage
{
    private readonly IGenerationRunner _runner;
    private readonly GenerationOperationController _operations;
    private readonly IGenerateWorkflowHost _host;
    private readonly GenerateWorkflowFailureHandler _failureHandler;

    public GenerateWorkflowExecutionStage(
        IGenerationRunner runner,
        GenerationOperationController operations,
        IGenerateWorkflowHost host,
        GenerateWorkflowFailureHandler failureHandler)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _failureHandler = failureHandler ??
            throw new ArgumentNullException(nameof(failureHandler));
    }

    public void Start(GenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        GenerationOperationLease operation =
            _operations.Begin(GenerationOperationKind.Generation);

        try
        {
            _host.Progress.Begin(request);
            _host.WorkflowState = GenerateWorkflowState.Generating;
            var progress = new Progress<GenerationProgressUpdate>(
                _host.Progress.Report);
            _ = RunGenerationAsync(request, progress, operation);
        }
        catch
        {
            operation.Dispose();
            throw;
        }
    }

    private async Task RunGenerationAsync(
        GenerationRequest request,
        IProgress<GenerationProgressUpdate> progress,
        GenerationOperationLease operation)
    {
        try
        {
            GenerationResult result = await _runner.RunAsync(
                request,
                progress,
                operation.CancellationToken);
            if (_host.IsDisposed || !operation.IsCurrent)
            {
                return;
            }

            _host.Progress.Complete(result);
            _host.WorkflowState = GenerateWorkflowState.Completed;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (!_host.IsDisposed)
            {
                _host.Progress.MarkCancelled();
                _host.WorkflowState = GenerateWorkflowState.Cancelled;
            }
        }
        catch (GenerationEngineUnavailableException exception)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Generation,
                exception.Message,
                exception);
        }
        catch (GenerationSourceException exception)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Generation,
                exception.Message,
                exception);
        }
        catch (Exception exception)
        {
            _failureHandler.Show(
                GenerationFailurePresentation.Generation,
                "Replay Foundry could not finish generating your clips.",
                exception);
        }
        finally
        {
            operation.Dispose();
            _host.RefreshCommandState();
        }
    }
}
