namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGenerationWatchdogPolicy
{
    internal const string Version =
        "visual-semantic-generation-watchdog-1.0";
    internal const string Sha256 =
        "a8f797b610de464de2c81cfa2beeb0b5bc732d65766be53c2e2a0b009143917e";
    internal const double MaximumGenerationWallClockSeconds = 240.0;
    internal const double MaximumGroundedCaseWallClockSeconds = 900.0;
    internal const string TimeoutBehavior = "FailClosed";
    internal const string GenerationTimeoutReason =
        "GenerationInvocationWallClockBudgetExceeded";
    internal const string CaseTimeoutReason =
        "GroundedCaseWallClockBudgetExceeded";
}
