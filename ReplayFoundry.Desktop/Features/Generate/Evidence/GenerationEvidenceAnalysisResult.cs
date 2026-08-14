using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Features.Generate.Preparation;

namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

public sealed class GenerationEvidenceAnalysisResult
{
    private readonly ReadOnlyCollection<AnalyzedGenerationSource>
        _sources;

    public GenerationEvidenceAnalysisResult(
        GenerationEvidenceAnalysisRequest request,
        IEnumerable<AnalyzedGenerationSource> sources)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sources);

        AnalyzedGenerationSource[] supplied =
            sources.ToArray();

        if (supplied.Any(
                static source =>
                    source is null))
        {
            throw new ArgumentException(
                "Analyzed sources cannot contain null entries.",
                nameof(sources));
        }

        if (supplied.Length !=
            request.SourceCount)
        {
            throw new ArgumentException(
                "Evidence analysis requires one result for every prepared source.",
                nameof(sources));
        }

        var uniqueSources =
            new HashSet<PreparedGenerationSource>(
                ReferenceEqualityComparer.Instance);

        foreach (AnalyzedGenerationSource source in supplied)
        {
            if (!uniqueSources.Add(
                    source.PreparedSource))
            {
                throw new ArgumentException(
                    "Evidence analysis cannot contain duplicate prepared sources.",
                    nameof(sources));
            }
        }

        var ordered =
            new AnalyzedGenerationSource[
                request.SourceCount];

        for (int index = 0;
             index < request.SourceCount;
             index++)
        {
            PreparedGenerationSource preparedSource =
                request.PreparedSources[index];

            AnalyzedGenerationSource[] matches =
                supplied
                    .Where(
                        source =>
                            ReferenceEquals(
                                source.PreparedSource,
                                preparedSource))
                    .ToArray();

            if (matches.Length != 1)
            {
                throw new ArgumentException(
                    "Evidence analysis contains a missing or foreign prepared source.",
                    nameof(sources));
            }

            if (!ReferenceEquals(
                    matches[0].CompositionPlan,
                    request.SourcePlans[index]))
            {
                throw new ArgumentException(
                    "Analyzed sources must bind to the current matching composition plans.",
                    nameof(sources));
            }

            ordered[index] = matches[0];
        }

        Request = request;
        _sources =
            Array.AsReadOnly(ordered);

        ReferenceSource =
            _sources.Single(
                source =>
                    ReferenceEquals(
                        source.PreparedSource,
                        request.ReferenceSource));
    }

    public GenerationEvidenceAnalysisRequest Request { get; }

    public IReadOnlyList<AnalyzedGenerationSource> Sources =>
        _sources;

    public AnalyzedGenerationSource ReferenceSource { get; }
}
