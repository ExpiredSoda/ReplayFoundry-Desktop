namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public interface IGenerationSourcePreparationCoordinator
{
    GenerationSourcePreparationResult? Current { get; }

    Task<GenerationSourcePreparationResult> GetOrPrepareAsync(
        GenerationSourcePreparationRequest request,
        IProgress<GenerationSourcePreparationProgress>? progress,
        CancellationToken cancellationToken);

    void EnsureFresh(
        GenerationSourcePreparationResult preparation);

    void Invalidate();
}
