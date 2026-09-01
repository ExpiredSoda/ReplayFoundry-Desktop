namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

/// <summary>
/// Reports that a bounded deterministic provider has exhausted every grounded
/// title it can produce for one exact cut. Repeating prior copy would make a
/// reroll misleading, so callers should surface this outcome instead.
/// </summary>
public sealed class ClipEditorialMetadataVariationUnavailableException :
    InvalidOperationException
{
    public ClipEditorialMetadataVariationUnavailableException(string message)
        : base(message)
    {
    }
}
