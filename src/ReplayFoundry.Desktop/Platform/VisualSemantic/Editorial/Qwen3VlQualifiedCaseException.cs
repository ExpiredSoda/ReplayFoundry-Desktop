namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed class Qwen3VlQualifiedCaseException(string errorCode, string stage, int exitCode)
    : Qwen3VlInferenceException("The local picture check could not finish.")
{
    public string ErrorCode { get; } = errorCode;
    public string Stage { get; } = stage;
    public int ExitCode { get; } = exitCode;
}
