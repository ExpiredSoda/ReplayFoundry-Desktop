using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public sealed class CreatorVoiceSettings
{
    private readonly ReadOnlyCollection<string> _defaultTags;

    public CreatorVoiceSettings(
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature,
        IEnumerable<string> defaultTags,
        ClipEditorialCopyObjective copyObjective =
            ClipEditorialCopyObjective.BalancedActionAndCommentary)
    {
        ArgumentNullException.ThrowIfNull(defaultTags);
        if (!Enum.IsDefined(copyObjective))
        {
            throw new ArgumentOutOfRangeException(nameof(copyObjective));
        }
        AudienceAddress = audienceAddress ?? string.Empty;
        NamingGuidance = namingGuidance ?? string.Empty;
        DescriptionSignature = descriptionSignature ?? string.Empty;
        _defaultTags = Array.AsReadOnly(defaultTags.ToArray());
        CopyObjective = copyObjective;
    }

    public string AudienceAddress { get; }

    public string NamingGuidance { get; }

    public string DescriptionSignature { get; }

    public IReadOnlyList<string> DefaultTags => _defaultTags;

    public ClipEditorialCopyObjective CopyObjective { get; }

    internal static CreatorVoiceSettings FromProfile(
        ClipEditorialProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new CreatorVoiceSettings(
            profile.AudienceAddress,
            profile.NamingGuidance ?? string.Empty,
            profile.ReusableDescriptionSignature ?? string.Empty,
            profile.DefaultTags,
            profile.CopyObjective);
    }
}

public interface ICreatorVoiceSettingsEditor
{
    CreatorVoiceSettings CurrentCreatorVoice { get; }

    CreatorVoiceSettings UpdateCreatorVoice(
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature,
        IEnumerable<string> defaultTags);
}
