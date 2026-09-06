namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

/// <summary>An earlier package for comparison. It never supplies grounding to a generator.</summary>
public sealed class ClipEditorialCopyVersion
{
    public ClipEditorialCopyVersion(string title, string description, IReadOnlyList<string> tags,
        string contextFingerprint, DateTimeOffset savedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextFingerprint);
        ArgumentNullException.ThrowIfNull(tags);
        if (title.Length > ClipEditorialMetadataDraft.MaximumTitleLength ||
            description.Length > ClipEditorialMetadataDraft.MaximumDescriptionLength ||
            savedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid earlier title and description.");
        Title = title;
        Description = description;
        Tags = Array.AsReadOnly(tags.ToArray());
        ContextFingerprint = contextFingerprint;
        SavedAtUtc = savedAtUtc;
    }

    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<string> Tags { get; }
    public string ContextFingerprint { get; }
    public DateTimeOffset SavedAtUtc { get; }
    public string Preview => Title + "\n\n" + Description;
}
