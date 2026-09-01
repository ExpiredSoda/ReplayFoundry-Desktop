namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public sealed class GenerationSourcePreparationCoordinator :
    IGenerationSourcePreparationCoordinator
{
    private readonly IGenerationSourcePreparationService
        _preparationService;

    private readonly GenerationSourceFreshnessValidator
        _freshnessValidator;

    public GenerationSourcePreparationCoordinator(
        IGenerationSourcePreparationService preparationService,
        GenerationSourceFreshnessValidator freshnessValidator)
    {
        ArgumentNullException.ThrowIfNull(preparationService);
        ArgumentNullException.ThrowIfNull(freshnessValidator);

        _preparationService = preparationService;
        _freshnessValidator = freshnessValidator;
    }

    public GenerationSourcePreparationResult? Current
    {
        get;
        private set;
    }

    public async Task<GenerationSourcePreparationResult>
        GetOrPrepareAsync(
            GenerationSourcePreparationRequest request,
            IProgress<GenerationSourcePreparationProgress>? progress,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        if (Current is not null &&
            RequestsMatch(
                Current.Request,
                request))
        {
            try
            {
                _freshnessValidator.EnsureFresh(Current);

                return Current;
            }
            catch (GenerationSourcePreparationException)
            {
                Current = null;
            }
        }
        else
        {
            Current = null;
        }

        GenerationSourcePreparationResult result =
            await _preparationService.PrepareAsync(
                request,
                progress,
                cancellationToken);

        Current = result;

        return result;
    }

    public void EnsureFresh(
        GenerationSourcePreparationResult preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        if (!ReferenceEquals(
                Current,
                preparation))
        {
            throw new InvalidOperationException(
                "The preparation result is no longer current.");
        }

        try
        {
            _freshnessValidator.EnsureFresh(preparation);
        }
        catch (GenerationSourcePreparationException)
        {
            Current = null;
            throw;
        }
    }

    public void Invalidate()
    {
        Current = null;
    }

    private static bool RequestsMatch(
        GenerationSourcePreparationRequest existing,
        GenerationSourcePreparationRequest requested)
    {
        if (existing.SourceCount != requested.SourceCount)
        {
            return false;
        }

        for (int index = 0;
             index < existing.SourceCount;
             index++)
        {
            if (!string.Equals(
                    existing.Sources[index].FullPath,
                    requested.Sources[index].FullPath,
                    StringComparison.OrdinalIgnoreCase) ||
                existing.Sources[index].IsReference !=
                requested.Sources[index].IsReference)
            {
                return false;
            }
        }

        return string.Equals(
            existing.ReferenceSource.FullPath,
            requested.ReferenceSource.FullPath,
            StringComparison.OrdinalIgnoreCase);
    }
}
