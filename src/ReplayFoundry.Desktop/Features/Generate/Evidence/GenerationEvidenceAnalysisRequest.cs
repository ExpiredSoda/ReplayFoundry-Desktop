using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

public sealed class GenerationEvidenceAnalysisRequest
{
    public GenerationEvidenceAnalysisRequest(
        GenerationSourcePreparationResult preparation,
        GenerationCompositionReviewResult compositionReview,
        GenerationEvidenceAnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(compositionReview);
        ArgumentNullException.ThrowIfNull(settings);

        if (!ReferenceEquals(
                preparation,
                compositionReview.Preparation))
        {
            throw new ArgumentException(
                "The composition review must belong to the retained source preparation.",
                nameof(compositionReview));
        }

        if (compositionReview.SourcePlans.Count !=
            preparation.Sources.Count)
        {
            throw new ArgumentException(
                "Evidence analysis requires one composition plan for every prepared source.",
                nameof(compositionReview));
        }

        for (int index = 0;
             index < preparation.Sources.Count;
             index++)
        {
            PreparedGenerationSource preparedSource =
                preparation.Sources[index];

            PreparedSourceCompositionPlan sourcePlan =
                compositionReview.SourcePlans[index];

            if (!ReferenceEquals(
                    preparedSource,
                    sourcePlan.PreparedSource))
            {
                throw new ArgumentException(
                    "Composition source order and prepared-source identity must be preserved.",
                    nameof(compositionReview));
            }

            ValidatePlan(
                preparedSource,
                sourcePlan.Plan,
                nameof(compositionReview));
        }

        if (!ReferenceEquals(
                preparation.ReferenceSource,
                compositionReview.ReferencePlan.PreparedSource))
        {
            throw new ArgumentException(
                "The composition review must preserve the explicit reference source.",
                nameof(compositionReview));
        }

        Preparation = preparation;
        CompositionReview = compositionReview;
        Settings = settings;
    }

    public GenerationSourcePreparationResult Preparation { get; }

    public GenerationCompositionReviewResult CompositionReview { get; }

    public GenerationEvidenceAnalysisSettings Settings { get; }

    public IReadOnlyList<PreparedGenerationSource>
        PreparedSources =>
        Preparation.Sources;

    public IReadOnlyList<PreparedSourceCompositionPlan>
        SourcePlans =>
        CompositionReview.SourcePlans;

    public PreparedGenerationSource ReferenceSource =>
        Preparation.ReferenceSource;

    public PreparedSourceCompositionPlan ReferencePlan =>
        CompositionReview.ReferencePlan;

    public int SourceCount =>
        PreparedSources.Count;

    public PreparedSourceCompositionPlan GetPlan(
        PreparedGenerationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return SourcePlans.SingleOrDefault(
                plan =>
                    ReferenceEquals(
                        plan.PreparedSource,
                        source)) ??
            throw new ArgumentException(
                "The prepared source does not belong to this evidence-analysis request.",
                nameof(source));
    }

    private static void ValidatePlan(
        PreparedGenerationSource source,
        CompositionPlan plan,
        string parameterName)
    {
        if (!string.Equals(
                source.Media.FullPath,
                plan.SourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A composition plan path does not match its prepared media.",
                parameterName);
        }

        if (source.Media.Duration !=
            plan.SourceDuration)
        {
            throw new ArgumentException(
                "A composition plan duration does not match its prepared media.",
                parameterName);
        }

        if (plan.CoordinateSpace !=
            CompositionCoordinateSpace
                .EffectiveDisplayNormalizedBeforeCrop)
        {
            throw new ArgumentException(
                "Evidence analysis requires effective-display normalized composition plans.",
                parameterName);
        }

        if (!plan.HasGameplay)
        {
            throw new ArgumentException(
                "Every evidence-analysis composition plan requires Gameplay.",
                parameterName);
        }
    }
}
