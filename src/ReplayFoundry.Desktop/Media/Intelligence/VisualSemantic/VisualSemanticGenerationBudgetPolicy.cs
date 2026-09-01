namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public static class VisualSemanticGenerationBudgetPolicy
{
    public const string Version =
        "visual-semantic-generation-budget-1.0";

    public const string Sha256 =
        "42813A9B29FF774343CF9A2FA149D53CEF780E1AD7A7FD0AD3E3312858EE9BBD";

    public const int LegacyDiagnosticMaximumNewTokens = 768;

    public const int ActiveMaximumNewTokens = 2048;

    public const int NumberOfBeams = 1;

    public const bool DoSample = false;

    public const bool UseCache = true;

    public const bool ForcedEndOfSequencePermitted = false;

    public const bool AutomaticRetryPermitted = false;

    public const bool PerCaseEscalationPermitted = false;
}
