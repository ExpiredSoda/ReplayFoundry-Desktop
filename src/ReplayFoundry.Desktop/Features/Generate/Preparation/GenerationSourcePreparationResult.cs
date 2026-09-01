using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public sealed class GenerationSourcePreparationResult
{
    private readonly ReadOnlyCollection<PreparedGenerationSource> _sources;

    public GenerationSourcePreparationResult(
        GenerationSourcePreparationRequest request,
        IEnumerable<PreparedGenerationSource> sources)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sources);

        PreparedGenerationSource[] snapshot =
            sources.ToArray();

        if (snapshot.Length != request.SourceCount)
        {
            throw new ArgumentException(
                "Prepared source count must match the preparation request.",
                nameof(sources));
        }

        if (snapshot.Any(static source => source is null))
        {
            throw new ArgumentException(
                "Prepared sources cannot contain null entries.",
                nameof(sources));
        }

        for (int index = 0;
             index < snapshot.Length;
             index++)
        {
            if (!ReferenceEquals(
                    request.Sources[index],
                    snapshot[index].Source))
            {
                throw new ArgumentException(
                    "Prepared sources must preserve request order and selected-source identity.",
                    nameof(sources));
            }
        }

        PreparedGenerationSource[] references =
            snapshot
                .Where(static source => source.Source.IsReference)
                .ToArray();

        if (references.Length != 1 ||
            !ReferenceEquals(
                request.ReferenceSource,
                references[0].Source))
        {
            throw new ArgumentException(
                "Prepared sources must preserve the explicit reference source.",
                nameof(sources));
        }

        Request = request;
        _sources =
            Array.AsReadOnly(snapshot);
        ReferenceSource =
            references[0];
    }

    public GenerationSourcePreparationRequest Request { get; }

    public IReadOnlyList<PreparedGenerationSource> Sources =>
        _sources;

    public PreparedGenerationSource ReferenceSource { get; }
}
