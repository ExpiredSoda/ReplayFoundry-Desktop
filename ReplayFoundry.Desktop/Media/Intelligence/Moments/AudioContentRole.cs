namespace ReplayFoundry.Desktop.Media.Intelligence.Moments;

public enum AudioContentRole
{
    CreatorSpeech,
    GameDialogue,
    MixedSpeech,
    Unknown,
}

public enum AudioContentRoleSource
{
    UserConfirmed,
    ImportedHumanReview,
    NotAvailable,
}

public sealed record AudioContentRoleAssignment
{
    public AudioContentRoleAssignment(
        AudioContentRole role = AudioContentRole.Unknown,
        AudioContentRoleSource source = AudioContentRoleSource.NotAvailable)
    {
        if (!Enum.IsDefined(role) ||
            !Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                "Audio role values must be defined.");
        }

        if ((role == AudioContentRole.Unknown) !=
            (source == AudioContentRoleSource.NotAvailable))
        {
            throw new ArgumentException(
                "Unknown/NotAvailable is the only unconfirmed audio-role assignment. Known roles require explicit user or human-review provenance.");
        }

        Role = role;
        Source = source;
    }

    public AudioContentRole Role { get; }

    public AudioContentRoleSource Source { get; }

    public static AudioContentRoleAssignment Unknown { get; } =
        new();
}
