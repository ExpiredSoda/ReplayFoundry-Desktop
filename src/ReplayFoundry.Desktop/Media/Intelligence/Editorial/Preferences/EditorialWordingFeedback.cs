namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;

public sealed record EditorialWordingFeedback(string Reason = "Unspecified", string CorrectedEvent = "")
{
    public static IReadOnlySet<string> Reasons { get; } = new HashSet<string>(StringComparer.Ordinal)
    { "Unspecified", "Style", "TooGeneric", "WrongSpeaker", "WrongEvent", "InventedOutcome", "WrongScope" };

    public void Validate()
    {
        if (!Reasons.Contains(Reason) || CorrectedEvent.Length > 600)
            throw new ArgumentException("Wording feedback is outside its supported bounds.");
    }
}
