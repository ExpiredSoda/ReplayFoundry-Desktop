namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public enum ClipEditorialAiFailureKind
{
    ProviderUnavailable = 0,
    ProviderFailed = 1,
    IncompleteResult = 2,
    UnsafeOutput = 3,
    CaseRejected = 4,
    NoveltyRejected = 5,
}

public sealed class ClipEditorialAiGenerationException : Exception
{
    public ClipEditorialAiGenerationException(
        ClipEditorialAiFailureKind failureKind,
        string message,
        string? candidateId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        FailureKind = failureKind;
        CandidateId = string.IsNullOrWhiteSpace(candidateId)
            ? null
            : candidateId.Trim();
    }

    public ClipEditorialAiFailureKind FailureKind { get; }

    public string? CandidateId { get; }
}
