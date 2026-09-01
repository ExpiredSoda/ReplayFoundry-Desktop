using System.IO;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

public static class ClipEditorialMetadataGenerationPolicy
{
    public static bool IsCompatible(
        ClipEditorialGenerationPreference preference,
        ClipEditorialMetadataDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!Enum.IsDefined(preference))
        {
            throw new ArgumentOutOfRangeException(nameof(preference));
        }

        return preference == ClipEditorialGenerationPreference.HeuristicOnly
            ? draft.Origin == ClipEditorialMetadataOrigin.Heuristic &&
              draft.AiProvenance is null &&
              draft.Readiness ==
                  ClipEditorialMetadataReadiness.WorkingLabel
            : draft.Origin == ClipEditorialMetadataOrigin.AiAssisted &&
              draft.AiProvenance is not null &&
              draft.Readiness ==
                  ClipEditorialMetadataReadiness.GroundedDraft;
    }

    public static void EnsureCompatible(
        ClipEditorialGenerationPreference preference,
        ClipEditorialMetadataDraft draft,
        string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (IsCompatible(preference, draft))
        {
            return;
        }

        throw new InvalidDataException(
            $"{operation} returned metadata that does not match the " +
            "explicit provider preference. Replay Foundry rejected the " +
            "draft instead of accepting heuristic, missing-provenance, or " +
            "provisional copy through an AI-required path.");
    }
}
