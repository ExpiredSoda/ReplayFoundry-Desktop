using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Guidance;

namespace ReplayFoundry.Desktop.Features.Generate.Moments;

public sealed class GenerationMomentFindingService :
    IGenerationMomentFindingService
{
    private readonly IMediaMomentFinder _finder;
    private readonly GenerationMomentPortfolioSelector
        _portfolioSelector;

    public GenerationMomentFindingService(
        IMediaMomentFinder finder,
        GenerationMomentPortfolioSelector? portfolioSelector = null)
    {
        ArgumentNullException.ThrowIfNull(finder);

        _finder = finder;
        _portfolioSelector =
            portfolioSelector ??
            new GenerationMomentPortfolioSelector();
    }

    public GenerationMomentFindingResult Find(
        GenerationMomentFindingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sourceResults =
            new List<GenerationSourceMomentResult>(
                request.SourceCount);

        foreach (var source in request.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lowLevelRequest =
                new MediaMomentFindingRequest(
                    source.PreparedSource.Media,
                    source.CompositionPlan.Plan,
                    source.Evidence,
                    source.Summary,
                    request.Settings.Options,
                    CreateGuidance(request, source));

            MediaMomentFindingResult moments =
                _finder.Find(
                    lowLevelRequest,
                    cancellationToken);

            sourceResults.Add(
                new GenerationSourceMomentResult(
                    source,
                    moments));
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<GenerationMomentCandidate> selected =
            _portfolioSelector.Select(
                request,
                sourceResults,
                cancellationToken);

        return new GenerationMomentFindingResult(
            request,
            sourceResults,
            selected);
    }

    private static MediaMomentGuidance CreateGuidance(
        GenerationMomentFindingRequest request,
        AnalyzedGenerationSource source)
    {
        UserMomentGuidance[] items = request.Setup.MomentGuidance
            .ForSource(source.PreparedSource.Media.FullPath)
            .ToArray();
        return items.Length == 0
            ? MediaMomentGuidance.Empty
            : new MediaMomentGuidance(
                items.Select(
                    static item => new MediaMomentGuidanceItem(
                        item.Id,
                        item.Kind == UserMomentGuidanceKind.PriorityPoint
                            ? MediaMomentGuidanceKind.PriorityPoint
                            : MediaMomentGuidanceKind.PriorityRange,
                        item.Start,
                        item.End,
                        item.ReservesCandidateSearch)));
    }
}
