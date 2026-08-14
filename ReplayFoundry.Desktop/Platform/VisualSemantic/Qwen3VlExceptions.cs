namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

public sealed class Qwen3VlInitializationException : Exception
{
    public Qwen3VlInitializationException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null,
        Qwen3VlHostFailureEnvelope? hostFailure = null,
        Exception? failureEnvelopeParseException = null,
        Exception? postFailureIntegrityException = null)
        : base(message, innerException)
    {
        DiagnosticDetails =
            string.IsNullOrWhiteSpace(diagnosticDetails)
                ? null
                : diagnosticDetails.Trim();
        HostFailure = hostFailure;
        FailureEnvelopeParseException =
            failureEnvelopeParseException;
        PostFailureIntegrityException =
            postFailureIntegrityException;
    }

    public string? DiagnosticDetails { get; }

    public Qwen3VlHostFailureEnvelope? HostFailure { get; }

    public Exception? FailureEnvelopeParseException { get; }

    public Exception? PostFailureIntegrityException { get; }
}

public class Qwen3VlInferenceException : Exception
{
    public Qwen3VlInferenceException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null,
        Qwen3VlHostFailureEnvelope? hostFailure = null,
        Exception? failureEnvelopeParseException = null,
        Exception? postFailureIntegrityException = null)
        : base(message, innerException)
    {
        DiagnosticDetails =
            string.IsNullOrWhiteSpace(diagnosticDetails)
                ? null
                : diagnosticDetails.Trim();
        HostFailure = hostFailure;
        FailureEnvelopeParseException =
            failureEnvelopeParseException;
        PostFailureIntegrityException =
            postFailureIntegrityException;
    }

    public string? DiagnosticDetails { get; }

    public Qwen3VlHostFailureEnvelope? HostFailure { get; }

    public Exception? FailureEnvelopeParseException { get; }

    public Exception? PostFailureIntegrityException { get; }

    internal Qwen3VlProviderAttemptBatch? AttemptBatch { get; init; }
}

public sealed class Qwen3VlOutputParseException :
    Qwen3VlInferenceException
{
    public Qwen3VlOutputParseException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, diagnosticDetails, innerException)
    {
    }
}
