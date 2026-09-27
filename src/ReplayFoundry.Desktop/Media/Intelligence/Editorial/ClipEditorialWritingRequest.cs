namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

public enum ClipEditorialWritingAction { NewAngle, Rewrite }

/// <summary>Editing intent and prior copy are preferences, never event evidence.</summary>
public sealed record ClipEditorialWritingRequest(
    ClipEditorialWritingAction Action,
    string CurrentTitle,
    string CurrentDescription,
    bool KeepTitle = false,
    bool KeepDescription = false,
    IReadOnlyList<ClipEditorialCopyVersion>? History = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Action) || KeepTitle && KeepDescription ||
            string.IsNullOrWhiteSpace(CurrentTitle) || string.IsNullOrWhiteSpace(CurrentDescription) ||
            CurrentTitle.Length > ClipEditorialMetadataDraft.MaximumTitleLength ||
            CurrentDescription.Length > ClipEditorialMetadataDraft.MaximumDescriptionLength ||
            History?.Count > 8 || History?.Any(static version => version is null) == true)
            throw new ArgumentException("Writing requires valid current copy and at least one unlocked field.");
    }
}
