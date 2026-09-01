using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegEvidencePassRunner
{
    public const int VisualIntervalOutputLimit = 32 * 1024 * 1024;

    private readonly IProcessRunner _processRunner;

    public FfmpegEvidencePassRunner(IProcessRunner processRunner)
    {
        _processRunner = processRunner ??
            throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<(ProcessRunResult Scene, ProcessRunResult Visual)>
        RunVisualPassesAsync(
            string ffmpegPath,
            MediaEvidenceAnalysisRequest request,
            VisualEvidenceTargetPlan targetPlan,
            int sceneOutputLimit,
            CancellationToken cancellationToken)
    {
        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<ProcessRunResult> sceneTask = RunPassAsync(
            ffmpegPath,
            request.Media.FullPath,
            "Scene detection",
            FfmpegEvidenceCommandBuilder.BuildSceneDetectionArguments(
                request,
                targetPlan.Targets),
            request.Options.ProcessTimeout,
            sceneOutputLimit,
            linkedCancellation.Token);

        Task<ProcessRunResult> visualTask = RunPassAsync(
            ffmpegPath,
            request.Media.FullPath,
            "Black and freeze detection",
            FfmpegEvidenceCommandBuilder.BuildVisualIntervalArguments(
                request,
                targetPlan.Targets),
            request.Options.ProcessTimeout,
            VisualIntervalOutputLimit,
            linkedCancellation.Token);

        try
        {
            await Task.WhenAll(sceneTask, visualTask);
        }
        catch
        {
            linkedCancellation.Cancel();
            try
            {
                await Task.WhenAll(sceneTask, visualTask);
            }
            catch
            {
                // Preserve the first failing pass after its sibling observes
                // cancellation and cleans its owned process tree.
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (sceneTask.Exception?.InnerException is Exception sceneFailure)
            {
                throw sceneFailure;
            }

            if (visualTask.Exception?.InnerException is Exception visualFailure)
            {
                throw visualFailure;
            }

            throw;
        }

        return (sceneTask.Result, visualTask.Result);
    }

    public async Task<ProcessRunResult> RunPassAsync(
        string ffmpegPath,
        string fullPath,
        string passName,
        IReadOnlyList<string> argumentTemplate,
        TimeSpan timeout,
        int outputLimit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> arguments =
            FfmpegEvidenceCommandBuilder.BindInputPath(
                argumentTemplate,
                fullPath);
        var processRequest = new ProcessRunRequest(
            ffmpegPath,
            arguments,
            timeout,
            maxStandardOutputCharacters: outputLimit,
            maxStandardErrorCharacters: 4 * 1024 * 1024);

        ProcessRunResult result;
        try
        {
            result = await _processRunner.RunAsync(
                processRequest,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProcessExecutionException exception)
        {
            throw new MediaEvidenceAnalysisException(
                $"Replay Foundry could not run {passName.ToLowerInvariant()}.",
                innerException: exception);
        }

        if (!result.Succeeded)
        {
            throw new MediaEvidenceAnalysisException(
                $"{passName} did not complete successfully.",
                FfmpegProcessResultDiagnostics.Describe(result));
        }

        return result;
    }
}
