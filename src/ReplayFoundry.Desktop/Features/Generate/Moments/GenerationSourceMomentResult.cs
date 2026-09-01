using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Moments;

public sealed class GenerationSourceMomentResult
{
    public GenerationSourceMomentResult(
        AnalyzedGenerationSource analyzedSource,
        MediaMomentFindingResult moments)
    {
        ArgumentNullException.ThrowIfNull(analyzedSource);
        ArgumentNullException.ThrowIfNull(moments);

        if (!ReferenceEquals(
                analyzedSource.PreparedSource.Media,
                moments.Request.Media) ||
            !ReferenceEquals(
                analyzedSource.CompositionPlan.Plan,
                moments.Request.Composition))
        {
            throw new ArgumentException(
                "The moment result must preserve the analyzed media and composition identities.",
                nameof(moments));
        }

        if (!ReferenceEquals(
                analyzedSource.Evidence,
                moments.Request.Evidence) ||
            !ReferenceEquals(
                analyzedSource.Summary,
                moments.Request.Summary))
        {
            throw new ArgumentException(
                "The moment result must use the retained evidence and summary payloads.",
                nameof(moments));
        }

        AnalyzedSource = analyzedSource;
        Moments = moments;
    }

    public AnalyzedGenerationSource AnalyzedSource { get; }
    public MediaMomentFindingResult Moments { get; }
}
