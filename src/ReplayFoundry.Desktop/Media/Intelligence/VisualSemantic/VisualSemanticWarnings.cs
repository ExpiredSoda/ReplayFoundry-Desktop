using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed record VisualSemanticWarning
{
    public VisualSemanticWarning(
        VisualSemanticWarningCode code,
        string message,
        string? caseId = null)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        Code = code;
        Message = VisualSemanticContractText.Required(
            message,
            nameof(message),
            500);
        CaseId = VisualSemanticContractText.Optional(
            caseId,
            nameof(caseId),
            128);
    }

    public VisualSemanticWarningCode Code { get; }

    public string Message { get; }

    public string? CaseId { get; }
}

public sealed record VisualSemanticEvidenceInterval
{
    public VisualSemanticEvidenceInterval(
        TimeSpan start,
        TimeSpan end,
        string description)
    {
        if (start < TimeSpan.Zero ||
            end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                "Visual-semantic evidence intervals must be ordered and may represent a point timestamp.");
        }

        Start = start;
        End = end;
        Description = VisualSemanticContractText.Required(
            description,
            nameof(description),
            240);
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration => End - Start;

    public string Description { get; }
}

public sealed record VisualSemanticUncertainty
{
    public VisualSemanticUncertainty(
        VisualSemanticUncertaintyCode code,
        string description)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        Code = code;
        Description = VisualSemanticContractText.Required(
            description,
            nameof(description),
            240);
    }

    public VisualSemanticUncertaintyCode Code { get; }

    public string Description { get; }
}
