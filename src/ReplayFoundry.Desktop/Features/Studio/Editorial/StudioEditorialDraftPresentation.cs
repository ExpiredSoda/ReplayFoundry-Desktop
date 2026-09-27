using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

internal static class StudioEditorialDraftPresentation
{
    internal static bool IsUnwritten(GenerationOutputAsset? asset) => asset?.EditorialMetadata is
        { Origin: ClipEditorialMetadataOrigin.Heuristic, Generator.Name: "studio-manual", Attempt: 0 };

    internal static string State(bool writing, bool unsaved, GenerationOutputAsset? asset, string savedState) =>
        writing ? "Writing…" : unsaved ? "Unsaved" : IsUnwritten(asset) ? "Needs a title" : savedState;

    internal static string ProviderText(bool unsaved, bool useAi, bool aiAvailable, string? unavailableReason) => unsaved
        ? "Saves your edits before writing another version. Your saved wording remains in History."
        : useAi
            ? aiAvailable
                ? "Local AI will write a title and description for this clip. Your current draft stays in place if it cannot finish."
                : unavailableReason ?? "Local AI is not ready. Check Advanced AI in Settings before trying again."
            : "Replay Foundry will create a quick local rewrite.";
}
