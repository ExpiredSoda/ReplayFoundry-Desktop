using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public sealed class GenerationSourcePreparationRequest
{
    private readonly ReadOnlyCollection<SelectedVideoSource> _sources;

    public GenerationSourcePreparationRequest(
        IEnumerable<SelectedVideoSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        SelectedVideoSource[] snapshot =
            sources.ToArray();

        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "Source preparation requires at least one selected video.",
                nameof(sources));
        }

        if (snapshot.Any(static source => source is null))
        {
            throw new ArgumentException(
                "Selected sources cannot contain null entries.",
                nameof(sources));
        }

        var uniquePaths =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (SelectedVideoSource source in snapshot)
        {
            if (!uniquePaths.Add(source.FullPath))
            {
                throw new ArgumentException(
                    $"The selected source path is duplicated: '{source.FullPath}'.",
                    nameof(sources));
            }
        }

        SelectedVideoSource[] references =
            snapshot
                .Where(static source => source.IsReference)
                .ToArray();

        if (references.Length != 1)
        {
            throw new ArgumentException(
                "Source preparation requires exactly one explicit reference source.",
                nameof(sources));
        }

        _sources =
            Array.AsReadOnly(snapshot);

        ReferenceSource =
            references[0];
    }

    public IReadOnlyList<SelectedVideoSource> Sources =>
        _sources;

    public SelectedVideoSource ReferenceSource { get; }

    public int SourceCount =>
        _sources.Count;
}
