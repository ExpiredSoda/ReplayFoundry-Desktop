using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public sealed class GenerationCompositionReviewRequest
{
    public GenerationCompositionReviewRequest(
        GenerationSourcePreparationResult preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        Preparation = preparation;
    }

    public GenerationSourcePreparationResult Preparation { get; }

    public IReadOnlyList<PreparedGenerationSource> Sources =>
        Preparation.Sources;

    public PreparedGenerationSource ReferenceSource =>
        Preparation.ReferenceSource;

    public int SourceCount =>
        Sources.Count;
}

public sealed class PreparedSourceCompositionPlan
{
    public PreparedSourceCompositionPlan(
        PreparedGenerationSource preparedSource,
        CompositionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(preparedSource);
        ArgumentNullException.ThrowIfNull(plan);

        if (!string.Equals(
                preparedSource.Source.FullPath,
                plan.SourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The composition plan path must match the prepared source path.",
                nameof(plan));
        }

        if (preparedSource.Media.Duration !=
            plan.SourceDuration)
        {
            throw new ArgumentException(
                "The composition plan duration must exactly match the prepared media duration.",
                nameof(plan));
        }

        if (plan.CoordinateSpace !=
            CompositionCoordinateSpace
                .EffectiveDisplayNormalizedBeforeCrop)
        {
            throw new ArgumentException(
                "The composition plan must use effective-display normalized coordinates.",
                nameof(plan));
        }

        if (!plan.HasGameplay)
        {
            throw new ArgumentException(
                "The composition plan must contain at least one Gameplay region.",
                nameof(plan));
        }

        PreparedSource = preparedSource;
        Plan = plan;
    }

    public PreparedGenerationSource PreparedSource { get; }

    public CompositionPlan Plan { get; }
}

public sealed class GenerationCompositionReviewResult
{
    private readonly ReadOnlyCollection<PreparedSourceCompositionPlan>
        _sourcePlans;

    public GenerationCompositionReviewResult(
        GenerationCompositionReviewRequest request,
        IEnumerable<PreparedSourceCompositionPlan> sourcePlans)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourcePlans);

        PreparedSourceCompositionPlan[] suppliedPlans =
            sourcePlans.ToArray();

        if (suppliedPlans.Any(static plan => plan is null))
        {
            throw new ArgumentException(
                "Composition source plans cannot contain null entries.",
                nameof(sourcePlans));
        }

        if (suppliedPlans.Length != request.SourceCount)
        {
            throw new ArgumentException(
                "Composition review requires exactly one plan for every prepared source.",
                nameof(sourcePlans));
        }

        var uniqueSources =
            new HashSet<PreparedGenerationSource>(
                ReferenceEqualityComparer.Instance);

        foreach (PreparedSourceCompositionPlan sourcePlan in
                 suppliedPlans)
        {
            if (!uniqueSources.Add(
                    sourcePlan.PreparedSource))
            {
                throw new ArgumentException(
                    "Composition review cannot contain duplicate prepared-source plans.",
                    nameof(sourcePlans));
            }
        }

        var orderedPlans =
            new PreparedSourceCompositionPlan[
                request.SourceCount];

        for (int index = 0;
             index < request.SourceCount;
             index++)
        {
            PreparedGenerationSource preparedSource =
                request.Sources[index];

            PreparedSourceCompositionPlan[] matches =
                suppliedPlans
                    .Where(
                        plan =>
                            ReferenceEquals(
                                plan.PreparedSource,
                                preparedSource))
                    .ToArray();

            if (matches.Length != 1)
            {
                throw new ArgumentException(
                    "Composition review contains a missing or foreign prepared-source plan.",
                    nameof(sourcePlans));
            }

            orderedPlans[index] =
                matches[0];
        }

        Request = request;
        Preparation = request.Preparation;
        _sourcePlans =
            Array.AsReadOnly(
                orderedPlans);

        ReferencePlan =
            _sourcePlans.Single(
                plan =>
                    ReferenceEquals(
                        plan.PreparedSource,
                        request.ReferenceSource));
    }

    public GenerationCompositionReviewRequest Request { get; }

    public GenerationSourcePreparationResult Preparation { get; }

    public IReadOnlyList<PreparedSourceCompositionPlan> SourcePlans =>
        _sourcePlans;

    public PreparedSourceCompositionPlan ReferencePlan { get; }
}
