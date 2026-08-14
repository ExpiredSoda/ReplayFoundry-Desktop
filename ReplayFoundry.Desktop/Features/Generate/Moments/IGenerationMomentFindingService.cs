namespace ReplayFoundry.Desktop.Features.Generate.Moments;

public interface IGenerationMomentFindingService
{
    GenerationMomentFindingResult Find(
        GenerationMomentFindingRequest request,
        CancellationToken cancellationToken = default);
}
