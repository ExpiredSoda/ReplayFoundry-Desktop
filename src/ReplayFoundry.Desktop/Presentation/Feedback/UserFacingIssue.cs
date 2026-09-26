using System;
using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Presentation.Feedback;

public enum UserIssueSeverity
{
    Info,
    Warning,
    Error,
}

public static class IssueReference
{
    private static readonly Regex Pattern = new(
        "^RF-[A-Z]{2,8}-[0-9]{3}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsValid(string? reference) => reference is not null && Pattern.IsMatch(reference);
}

public sealed record UserFacingIssue
{
    public UserFacingIssue(
        string reference,
        string summary,
        string suggestion,
        string details,
        UserIssueSeverity severity = UserIssueSeverity.Error)
    {
        if (!IssueReference.IsValid(reference))
        {
            throw new ArgumentException("Issue references must use RF-AREA-000 format.", nameof(reference));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("A user-facing issue needs a short summary.", nameof(summary));
        }

        Reference = reference;
        Summary = summary;
        Suggestion = suggestion;
        Details = details;
        Severity = severity;
    }

    public string Reference { get; }
    public string Summary { get; }
    public string Suggestion { get; }
    public string Details { get; }
    public UserIssueSeverity Severity { get; }
}
