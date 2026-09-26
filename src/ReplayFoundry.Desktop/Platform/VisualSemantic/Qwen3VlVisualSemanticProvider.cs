using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

public sealed class Qwen3VlVisualSemanticProvider :
    IVisualSemanticProvider
{
    private readonly Qwen3VlBatchProcessExecutor _executor;

    public Qwen3VlVisualSemanticProvider(
        Qwen3VlBatchHostSettings settings)
    {
        _executor =
            new Qwen3VlBatchProcessExecutor(settings);
    }

    internal Qwen3VlVisualSemanticProvider(
        Qwen3VlBatchHostSettings settings,
        IProcessRunner processRunner,
        IQwen3VlBatchWorkspaceFactory workspaceFactory)
    {
        _executor =
            new Qwen3VlBatchProcessExecutor(
                settings,
                processRunner,
                workspaceFactory);
    }

    public InferenceProviderIdentity Identity =>
        _executor.Identity;

    public Task<VisualSemanticBatchResult> ObserveAsync(
        VisualSemanticBatchRequest request,
        CancellationToken cancellationToken) =>
        _executor.ObserveAsync(
            request,
            cancellationToken);

    internal Task<Qwen3VlObservationWithAttemptResult>
        ObserveWithAttemptAsync(
            VisualSemanticBatchRequest request,
            CancellationToken cancellationToken) =>
        _executor.ObserveWithAttemptAsync(
            request,
            cancellationToken);
}
