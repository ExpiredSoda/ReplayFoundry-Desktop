namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentFindingWarning
{
    public MomentFindingWarning(
        MomentFindingWarningCode code,
        string message,
        string? candidateId = null)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "The moment-finding warning code is not defined.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A moment-finding warning requires a message.",
                nameof(message));
        }

        Code = code;
        Message = message.Trim();
        CandidateId =
            string.IsNullOrWhiteSpace(candidateId)
                ? null
                : candidateId.Trim();
    }

    public MomentFindingWarningCode Code { get; }

    public string Message { get; }

    public string? CandidateId { get; }
}
