using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed record VisualSemanticEditorialCollectionAudit(
    int RawCount,
    int CanonicalCount,
    int ExactDuplicateCount,
    bool OrderChanged);

public sealed record VisualSemanticEditorialCanonicalizationAudit(
    string PolicyVersion,
    VisualSemanticEditorialCollectionAudit ObservedChanges,
    VisualSemanticEditorialCollectionAudit EvidenceIntervals,
    VisualSemanticEditorialCollectionAudit UncertaintyReasons,
    bool OuterWhitespaceTrimmed,
    int SyntacticCanonicalizationCount,
    int SchemaShapeCanonicalizationCount,
    int SemanticRepairCount,
    string? WireRepresentationVersion = null);

public sealed record VisualSemanticEditorialCanonicalizationResult(
    IReadOnlyList<VisualSemanticEditorialObservedChange> ObservedChanges,
    IReadOnlyList<VisualSemanticEditorialEvidenceInterval> EvidenceIntervals,
    IReadOnlyList<VisualSemanticEditorialUncertainty> UncertaintyReasons,
    VisualSemanticEditorialCanonicalizationAudit Audit);

public static class VisualSemanticEditorialCanonicalizer
{
    public const string PolicyVersion =
        "visual-semantic-editorial-canonicalization-1.3";

    public static VisualSemanticEditorialCanonicalizationResult
        Canonicalize(
            IEnumerable<VisualSemanticEditorialObservedChange> changes,
            IEnumerable<VisualSemanticEditorialEvidenceInterval> intervals,
            IEnumerable<VisualSemanticEditorialUncertainty> uncertainties,
            int nestedReferenceCanonicalizationCount = 0)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(uncertainties);

        if (nestedReferenceCanonicalizationCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nestedReferenceCanonicalizationCount));
        }

        VisualSemanticEditorialObservedChange[] rawChanges =
            changes.ToArray();
        VisualSemanticEditorialEvidenceInterval[] rawIntervals =
            intervals.ToArray();
        VisualSemanticEditorialUncertainty[] rawUncertainties =
            uncertainties.ToArray();
        VisualSemanticEditorialObservedChange[] canonicalChanges =
            Order(
                    rawChanges.DistinctBy(
                        static value =>
                            (
                                value.Description,
                                value.EvidenceBasis,
                                References:
                                    string.Join(
                                        "\u001f",
                                        value.EvidenceIntervalIds)
                            )))
                .ToArray();
        VisualSemanticEditorialEvidenceInterval[] canonicalIntervals =
            Order(rawIntervals.Distinct()).ToArray();
        VisualSemanticEditorialUncertainty[] canonicalUncertainties =
            Order(rawUncertainties.Distinct()).ToArray();
        VisualSemanticEditorialCollectionAudit changeAudit =
            CreateAudit(rawChanges, canonicalChanges);
        VisualSemanticEditorialCollectionAudit intervalAudit =
            CreateAudit(rawIntervals, canonicalIntervals);
        VisualSemanticEditorialCollectionAudit uncertaintyAudit =
            CreateAudit(rawUncertainties, canonicalUncertainties);

        return new VisualSemanticEditorialCanonicalizationResult(
            Array.AsReadOnly(canonicalChanges),
            Array.AsReadOnly(canonicalIntervals),
            Array.AsReadOnly(canonicalUncertainties),
            new VisualSemanticEditorialCanonicalizationAudit(
                PolicyVersion,
                changeAudit,
                intervalAudit,
                uncertaintyAudit,
                OuterWhitespaceTrimmed: false,
                SyntacticCanonicalizationCount:
                    Changed(changeAudit) +
                    Changed(intervalAudit) +
                    Changed(uncertaintyAudit) +
                    nestedReferenceCanonicalizationCount,
                SchemaShapeCanonicalizationCount: 0,
                SemanticRepairCount: 0,
                WireRepresentationVersion: null));
    }

    internal static IEnumerable<VisualSemanticEditorialObservedChange>
        Order(
            IEnumerable<VisualSemanticEditorialObservedChange> values) =>
        values
            .OrderBy(
                static value => value.Description,
                StringComparer.Ordinal)
            .ThenBy(static value => value.EvidenceBasis)
            .ThenBy(
                static value =>
                    string.Join(
                        "\u001f",
                        value.EvidenceIntervalIds),
                StringComparer.Ordinal);

    internal static IEnumerable<VisualSemanticEditorialEvidenceInterval>
        Order(
            IEnumerable<VisualSemanticEditorialEvidenceInterval> values) =>
        values
            .OrderBy(static value => value.Start)
            .ThenBy(static value => value.End)
            .ThenBy(
                static value => value.Description,
                StringComparer.Ordinal)
            .ThenBy(static value => value.EvidenceBasis)
            .ThenBy(
                static value => value.Id,
                StringComparer.Ordinal);

    internal static IEnumerable<VisualSemanticEditorialUncertainty>
        Order(
            IEnumerable<VisualSemanticEditorialUncertainty> values) =>
        values
            .OrderBy(static value => value.Code)
            .ThenBy(
                static value => value.Description,
                StringComparer.Ordinal);

    private static VisualSemanticEditorialCollectionAudit CreateAudit<T>(
        IReadOnlyList<T> raw,
        IReadOnlyList<T> canonical)
    {
        int duplicates =
            typeof(T) ==
                typeof(VisualSemanticEditorialObservedChange)
                ? raw.Count -
                  raw
                      .Cast<VisualSemanticEditorialObservedChange>()
                      .DistinctBy(
                          static value =>
                              (
                                  value.Description,
                                  value.EvidenceBasis,
                                  References:
                                      string.Join(
                                          "\u001f",
                                          value.EvidenceIntervalIds)
                              ))
                      .Count()
                : raw.Count - raw.Distinct().Count();
        IEnumerable<T> distinct =
            typeof(T) ==
                typeof(VisualSemanticEditorialObservedChange)
                ? raw
                    .Cast<VisualSemanticEditorialObservedChange>()
                    .DistinctBy(
                        static value =>
                            (
                                value.Description,
                                value.EvidenceBasis,
                                References:
                                    string.Join(
                                        "\u001f",
                                        value.EvidenceIntervalIds)
                            ))
                    .Cast<T>()
                : raw.Distinct();
        bool orderChanged =
            !distinct.SequenceEqual(canonical);

        return new VisualSemanticEditorialCollectionAudit(
            raw.Count,
            canonical.Count,
            duplicates,
            orderChanged);
    }

    private static int Changed(
        VisualSemanticEditorialCollectionAudit audit) =>
        audit.ExactDuplicateCount > 0 ||
        audit.OrderChanged
            ? 1
            : 0;
}
