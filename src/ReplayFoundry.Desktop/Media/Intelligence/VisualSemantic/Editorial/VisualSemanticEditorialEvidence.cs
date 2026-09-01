using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed record VisualSemanticEditorialEvidenceInterval
{
    private static readonly Regex IdPattern =
        new(
            "^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$",
            RegexOptions.CultureInvariant);

    public VisualSemanticEditorialEvidenceInterval(
        string id,
        TimeSpan start,
        TimeSpan end,
        string description,
        VisualSemanticEvidenceBasis evidenceBasis)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            !IdPattern.IsMatch(id) ||
            start < TimeSpan.Zero ||
            end < start ||
            !Enum.IsDefined(evidenceBasis))
        {
            throw new ArgumentException(
                "Prompt 2.0 evidence requires a stable ID, ordered timestamps, and a defined basis.");
        }

        Id = id;
        Start = start;
        End = end;
        Description = RequireText(
            description,
            nameof(description),
            240);
        EvidenceBasis = evidenceBasis;
    }

    public string Id { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public string Description { get; }

    public VisualSemanticEvidenceBasis EvidenceBasis { get; }

    internal static string RequireText(
        string value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            !string.Equals(
                value,
                value.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Prompt 2.0 text must be nonblank, already trimmed, and at most {maximumLength} characters.",
                parameterName);
        }

        return value;
    }
}

public sealed record VisualSemanticEditorialObservedChange
{
    private readonly ReadOnlyCollection<string>
        _evidenceIntervalIds;

    public VisualSemanticEditorialObservedChange(
        string description,
        VisualSemanticEvidenceBasis evidenceBasis,
        IEnumerable<string> evidenceIntervalIds)
    {
        ArgumentNullException.ThrowIfNull(evidenceIntervalIds);

        if (!Enum.IsDefined(evidenceBasis))
        {
            throw new ArgumentOutOfRangeException(
                nameof(evidenceBasis));
        }

        string[] identifiers =
            evidenceIntervalIds.ToArray();

        if (identifiers.Length is < 1 or > 6 ||
            identifiers.Any(
                static value =>
                    string.IsNullOrWhiteSpace(value)) ||
            identifiers.Distinct(
                    StringComparer.Ordinal)
                .Count() != identifiers.Length ||
            !identifiers.SequenceEqual(
                identifiers.OrderBy(
                    static value => value,
                    StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Prompt 2.0 observed changes require one to six unique canonical evidence references.",
                nameof(evidenceIntervalIds));
        }

        Description =
            VisualSemanticEditorialEvidenceInterval.RequireText(
                description,
                nameof(description),
                240);
        EvidenceBasis = evidenceBasis;
        _evidenceIntervalIds =
            Array.AsReadOnly(identifiers);
    }

    public string Description { get; }

    public VisualSemanticEvidenceBasis EvidenceBasis { get; }

    public IReadOnlyList<string> EvidenceIntervalIds =>
        _evidenceIntervalIds;
}

public sealed record VisualSemanticEditorialUncertainty
{
    public VisualSemanticEditorialUncertainty(
        VisualSemanticEditorialUncertaintyCode code,
        string description)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        Code = code;
        Description =
            VisualSemanticEditorialEvidenceInterval.RequireText(
                description,
                nameof(description),
                240);
    }

    public VisualSemanticEditorialUncertaintyCode Code { get; }

    public string Description { get; }
}
