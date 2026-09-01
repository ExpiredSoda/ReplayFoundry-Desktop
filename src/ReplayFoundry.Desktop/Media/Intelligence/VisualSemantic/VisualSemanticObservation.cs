using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed class VisualSemanticObservation
{
    public const int MaximumEvidenceIntervals = 6;
    public const int MaximumUncertainties = 8;
    public const int MaximumLimitations = 4;
    public const int MaximumRationaleLength = 400;

    private readonly ReadOnlyCollection<VisualSemanticEvidenceInterval>
        _evidenceIntervals;
    private readonly ReadOnlyCollection<VisualSemanticUncertainty>
        _uncertainties;
    private readonly ReadOnlyCollection<string> _limitations;

    public VisualSemanticObservation(
        string caseId,
        string candidateId,
        string schemaVersion,
        VisualSemanticObservableContentType observableContentType,
        string? visibleStateChange,
        VisualSemanticTernary hasClearBeginning,
        VisualSemanticTernary hasClearOutcome,
        VisualSemanticTernary menuOrTraversalPresent,
        VisualSemanticRelevance spokenContentAppearsRelevant,
        VisualSemanticTernary suggestedWorthReviewing,
        VisualSemanticReviewCertainty reviewCertainty,
        IEnumerable<VisualSemanticEvidenceInterval> evidenceIntervals,
        IEnumerable<VisualSemanticUncertainty> uncertainties,
        IEnumerable<string> limitations,
        string conciseRationale)
    {
        ValidateEnum(observableContentType, nameof(observableContentType));
        ValidateEnum(hasClearBeginning, nameof(hasClearBeginning));
        ValidateEnum(hasClearOutcome, nameof(hasClearOutcome));
        ValidateEnum(menuOrTraversalPresent, nameof(menuOrTraversalPresent));
        ValidateEnum(
            spokenContentAppearsRelevant,
            nameof(spokenContentAppearsRelevant));
        ValidateEnum(
            suggestedWorthReviewing,
            nameof(suggestedWorthReviewing));
        ValidateEnum(reviewCertainty, nameof(reviewCertainty));
        ArgumentNullException.ThrowIfNull(evidenceIntervals);
        ArgumentNullException.ThrowIfNull(uncertainties);
        ArgumentNullException.ThrowIfNull(limitations);

        VisualSemanticEvidenceInterval[] intervalSnapshot =
            evidenceIntervals
                .OrderBy(static value => value.Start)
                .ThenBy(static value => value.End)
                .ThenBy(
                    static value => value.Description,
                    StringComparer.Ordinal)
                .ToArray();
        VisualSemanticUncertainty[] uncertaintySnapshot =
            uncertainties
                .OrderBy(static value => value.Code)
                .ThenBy(
                    static value => value.Description,
                    StringComparer.Ordinal)
                .ToArray();
        string[] limitationSnapshot =
            limitations
                .Select(
                    value => VisualSemanticContractText.Required(
                        value,
                        nameof(limitations),
                        240))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();

        if (intervalSnapshot.Length > MaximumEvidenceIntervals ||
            uncertaintySnapshot.Length > MaximumUncertainties ||
            limitationSnapshot.Length > MaximumLimitations ||
            intervalSnapshot.Any(static value => value is null) ||
            uncertaintySnapshot.Any(static value => value is null) ||
            intervalSnapshot
                .GroupBy(
                    static value =>
                        (
                            value.Start,
                            value.End,
                            value.Description
                        ))
                .Any(static group => group.Count() > 1) ||
            uncertaintySnapshot
                .GroupBy(
                    static value =>
                        (
                            value.Code,
                            value.Description
                        ))
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Visual-semantic observation collections must be bounded, unique, and non-null.");
        }

        CaseId = VisualSemanticContractText.Required(
            caseId,
            nameof(caseId),
            128);
        CandidateId = VisualSemanticContractText.Required(
            candidateId,
            nameof(candidateId),
            128);
        SchemaVersion = VisualSemanticContractText.Required(
            schemaVersion,
            nameof(schemaVersion),
            32);
        ObservableContentType = observableContentType;
        VisibleStateChange = VisualSemanticContractText.Optional(
            visibleStateChange,
            nameof(visibleStateChange),
            320);
        HasClearBeginning = hasClearBeginning;
        HasClearOutcome = hasClearOutcome;
        MenuOrTraversalPresent = menuOrTraversalPresent;
        SpokenContentAppearsRelevant =
            spokenContentAppearsRelevant;
        SuggestedWorthReviewing = suggestedWorthReviewing;
        ReviewCertainty = reviewCertainty;
        ConciseRationale = VisualSemanticContractText.Required(
            conciseRationale,
            nameof(conciseRationale),
            MaximumRationaleLength);
        _evidenceIntervals = Array.AsReadOnly(intervalSnapshot);
        _uncertainties = Array.AsReadOnly(uncertaintySnapshot);
        _limitations = Array.AsReadOnly(limitationSnapshot);
    }

    public string CaseId { get; }

    public string CandidateId { get; }

    public string SchemaVersion { get; }

    public VisualSemanticObservableContentType ObservableContentType { get; }

    public string? VisibleStateChange { get; }

    public VisualSemanticTernary HasClearBeginning { get; }

    public VisualSemanticTernary HasClearOutcome { get; }

    public VisualSemanticTernary MenuOrTraversalPresent { get; }

    public VisualSemanticRelevance SpokenContentAppearsRelevant { get; }

    public VisualSemanticTernary SuggestedWorthReviewing { get; }

    public VisualSemanticReviewCertainty ReviewCertainty { get; }

    public IReadOnlyList<VisualSemanticEvidenceInterval>
        EvidenceIntervals =>
        _evidenceIntervals;

    public IReadOnlyList<VisualSemanticUncertainty> Uncertainties =>
        _uncertainties;

    public IReadOnlyList<string> Limitations => _limitations;

    public string ConciseRationale { get; }

    private static void ValidateEnum<T>(
        T value,
        string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Visual-semantic enum values must be defined.");
        }
    }
}
