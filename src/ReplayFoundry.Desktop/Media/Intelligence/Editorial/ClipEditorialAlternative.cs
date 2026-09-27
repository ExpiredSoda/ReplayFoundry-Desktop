using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

/// <summary>A source-checked alternative from the same immutable writing batch.</summary>
public sealed class ClipEditorialAlternative
{
    public ClipEditorialAlternative(string title, string description, IReadOnlyList<string> tags,
        string angle, string profileFingerprint, string factHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(tags);
        if (title.Length > 100 || description.Length > 5000 || tags.Count > 15 ||
            tags.Any(static tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 60) ||
            string.IsNullOrWhiteSpace(angle) || angle.Length > 160 ||
            !IsHash(profileFingerprint) || !IsHash(factHash))
            throw new ArgumentException("Invalid saved writing alternative.");
        Title = title; Description = description; Tags = Array.AsReadOnly(tags.ToArray());
        Angle = angle; ProfileFingerprint = profileFingerprint; FactHash = factHash;
    }

    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<string> Tags { get; }
    public string Angle { get; }
    public string ProfileFingerprint { get; }
    public string FactHash { get; }
    public string Preview => Title + "\n\n" + Description;

    public static string ProfileKey(ClipEditorialProfile profile, string tone) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            profile.AudienceAddress, profile.NamingGuidance, profile.ReusableDescriptionSignature,
            profile.DefaultTags, profile.VoicePerspective, profile.CopyObjective, tone
        }))));

    private static bool IsHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
