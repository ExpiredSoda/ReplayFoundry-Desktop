using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public sealed class PreparedGenerationSource
{
    public PreparedGenerationSource(
        SelectedVideoSource source,
        MediaProbeResult media,
        GenerationSourceFileSnapshot fileSnapshot)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(fileSnapshot);

        if (!string.Equals(
                source.FullPath,
                media.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Prepared media must describe the selected source path.",
                nameof(media));
        }

        if (!string.Equals(
                source.FullPath,
                fileSnapshot.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The file snapshot must describe the selected source path.",
                nameof(fileSnapshot));
        }

        Source = source;
        Media = media;
        FileSnapshot = fileSnapshot;
    }

    public SelectedVideoSource Source { get; }

    public MediaProbeResult Media { get; }

    public GenerationSourceFileSnapshot FileSnapshot { get; }
}
