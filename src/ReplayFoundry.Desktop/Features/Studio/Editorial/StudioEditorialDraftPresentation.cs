using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

internal static class StudioEditorialDraftPresentation
{
    internal static bool IsUnwritten(GenerationOutputAsset? asset) => asset?.EditorialMetadata is
        { Origin: ClipEditorialMetadataOrigin.Heuristic, Generator.Name: "studio-manual", Attempt: 0 };

    internal static string State(bool writing, bool unsaved, GenerationOutputAsset? asset, string savedState) =>
        writing ? "Writing…" : unsaved ? "Unsaved" : IsUnwritten(asset) ? "Needs a title" : savedState;
}
