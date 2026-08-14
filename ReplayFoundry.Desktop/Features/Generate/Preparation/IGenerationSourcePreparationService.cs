namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public interface IGenerationSourcePreparationService
{
    Task<GenerationSourcePreparationResult> PrepareAsync(
        GenerationSourcePreparationRequest request,
        IProgress<GenerationSourcePreparationProgress>? progress,
        CancellationToken cancellationToken);
}
