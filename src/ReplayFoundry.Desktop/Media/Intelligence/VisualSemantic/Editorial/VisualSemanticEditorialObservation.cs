using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed class VisualSemanticEditorialObservation
{
    public const string SchemaVersion =
        "visual-semantic-editorial-observation-2.0";

    public const int MaximumObservedChanges = 6;
    public const int MaximumEvidenceIntervals = 6;
    public const int MaximumUncertaintyReasons = 8;
    public const int MaximumRationaleLength = 400;

    private readonly ReadOnlyCollection<
        VisualSemanticEditorialObservedChange> _observedChanges;
    private readonly ReadOnlyCollection<
        VisualSemanticEditorialEvidenceInterval> _evidenceIntervals;
    private readonly ReadOnlyCollection<
        VisualSemanticEditorialUncertainty> _uncertaintyReasons;

    public VisualSemanticEditorialObservation(
        VisualSemanticObservableContentType observableContentType,
        VisualSemanticTernary hasDistinctEvent,
        VisualSemanticTernary hasObservablePayoff,
        VisualSemanticTernary routineTraversalOrMenuOnly,
        VisualSemanticTernary candidateRequiresMissingContext,
        VisualSemanticTernary candidateContainsOnlyAmbientChange,
        VisualSemanticTranscriptContextSupport transcriptContextSupport,
        IEnumerable<VisualSemanticEditorialObservedChange> observedChanges,
        IEnumerable<VisualSemanticEditorialEvidenceInterval>
            evidenceIntervals,
        IEnumerable<VisualSemanticEditorialUncertainty> uncertaintyReasons,
        VisualSemanticEditorialDisposition editorialDisposition,
        VisualSemanticEditorialRejectReason rejectReason,
        string dispositionRationale)
    {
        ValidateEnum(observableContentType, nameof(observableContentType));
        ValidateEnum(hasDistinctEvent, nameof(hasDistinctEvent));
        ValidateEnum(hasObservablePayoff, nameof(hasObservablePayoff));
        ValidateEnum(
            routineTraversalOrMenuOnly,
            nameof(routineTraversalOrMenuOnly));
        ValidateEnum(
            candidateRequiresMissingContext,
            nameof(candidateRequiresMissingContext));
        ValidateEnum(
            candidateContainsOnlyAmbientChange,
            nameof(candidateContainsOnlyAmbientChange));
        ValidateEnum(
            transcriptContextSupport,
            nameof(transcriptContextSupport));
        ValidateEnum(
            editorialDisposition,
            nameof(editorialDisposition));
        ValidateEnum(rejectReason, nameof(rejectReason));
        ArgumentNullException.ThrowIfNull(observedChanges);
        ArgumentNullException.ThrowIfNull(evidenceIntervals);
        ArgumentNullException.ThrowIfNull(uncertaintyReasons);

        VisualSemanticEditorialObservedChange[] changes =
            observedChanges.ToArray();
        VisualSemanticEditorialEvidenceInterval[] intervals =
            evidenceIntervals.ToArray();
        VisualSemanticEditorialUncertainty[] uncertainties =
            uncertaintyReasons.ToArray();

        if (changes.Length > MaximumObservedChanges ||
            intervals.Length > MaximumEvidenceIntervals ||
            uncertainties.Length > MaximumUncertaintyReasons ||
            changes.Any(static value => value is null) ||
            intervals.Any(static value => value is null) ||
            uncertainties.Any(static value => value is null) ||
            changes
                .GroupBy(
                    static value =>
                        (
                            value.Description,
                            value.EvidenceBasis,
                            References:
                                string.Join(
                                    "\u001f",
                                    value.EvidenceIntervalIds)
                        ))
                .Any(static group => group.Count() != 1) ||
            intervals.Distinct().Count() != intervals.Length ||
            uncertainties.Distinct().Count() != uncertainties.Length ||
            intervals
                .GroupBy(
                    static value => value.Id,
                    StringComparer.Ordinal)
                .Any(static group => group.Count() != 1))
        {
            throw new ArgumentException(
                "Prompt 2.0 semantic collections must be bounded, non-null, and unique.");
        }

        RequireCanonicalOrder(changes, intervals, uncertainties);
        ValidateEvidenceReferences(
            changes,
            intervals,
            transcriptContextSupport);

        ObservableContentType = observableContentType;
        HasDistinctEvent = hasDistinctEvent;
        HasObservablePayoff = hasObservablePayoff;
        RoutineTraversalOrMenuOnly = routineTraversalOrMenuOnly;
        CandidateRequiresMissingContext =
            candidateRequiresMissingContext;
        CandidateContainsOnlyAmbientChange =
            candidateContainsOnlyAmbientChange;
        TranscriptContextSupport = transcriptContextSupport;
        EditorialDisposition = editorialDisposition;
        RejectReason = rejectReason;
        DispositionRationale =
            VisualSemanticEditorialEvidenceInterval.RequireText(
                dispositionRationale,
                nameof(dispositionRationale),
                MaximumRationaleLength);
        _observedChanges = Array.AsReadOnly(changes);
        _evidenceIntervals = Array.AsReadOnly(intervals);
        _uncertaintyReasons = Array.AsReadOnly(uncertainties);
    }

    public VisualSemanticObservableContentType ObservableContentType
    {
        get;
    }

    public VisualSemanticTernary HasDistinctEvent { get; }

    public VisualSemanticTernary HasObservablePayoff { get; }

    public VisualSemanticTernary RoutineTraversalOrMenuOnly { get; }

    public VisualSemanticTernary CandidateRequiresMissingContext { get; }

    public VisualSemanticTernary CandidateContainsOnlyAmbientChange
    {
        get;
    }

    public VisualSemanticTranscriptContextSupport TranscriptContextSupport
    {
        get;
    }

    public IReadOnlyList<VisualSemanticEditorialObservedChange>
        ObservedChanges =>
        _observedChanges;

    public IReadOnlyList<VisualSemanticEditorialEvidenceInterval>
        EvidenceIntervals =>
        _evidenceIntervals;

    public IReadOnlyList<VisualSemanticEditorialUncertainty>
        UncertaintyReasons =>
        _uncertaintyReasons;

    public VisualSemanticEditorialDisposition EditorialDisposition
    {
        get;
    }

    public VisualSemanticEditorialRejectReason RejectReason { get; }

    public string DispositionRationale { get; }

    private static void RequireCanonicalOrder(
        IReadOnlyList<VisualSemanticEditorialObservedChange> changes,
        IReadOnlyList<VisualSemanticEditorialEvidenceInterval> intervals,
        IReadOnlyList<VisualSemanticEditorialUncertainty> uncertainties)
    {
        if (!changes.SequenceEqual(
                VisualSemanticEditorialCanonicalizer.Order(changes)) ||
            !intervals.SequenceEqual(
                VisualSemanticEditorialCanonicalizer.Order(intervals)) ||
            !uncertainties.SequenceEqual(
                VisualSemanticEditorialCanonicalizer.Order(uncertainties)))
        {
            throw new ArgumentException(
                "Prompt 2.0 semantic collections must use canonical ordering.");
        }
    }

    private static void ValidateEvidenceReferences(
        IEnumerable<VisualSemanticEditorialObservedChange> changes,
        IReadOnlyList<VisualSemanticEditorialEvidenceInterval> intervals,
        VisualSemanticTranscriptContextSupport transcriptContextSupport)
    {
        Dictionary<string, VisualSemanticEditorialEvidenceInterval> byId =
            intervals.ToDictionary(
                static value => value.Id,
                StringComparer.Ordinal);

        if (transcriptContextSupport ==
                VisualSemanticTranscriptContextSupport.NotSupplied &&
            intervals.Any(
                static value =>
                    value.EvidenceBasis is
                        VisualSemanticEvidenceBasis.TranscriptContext or
                        VisualSemanticEvidenceBasis.Both))
        {
            throw new ArgumentException(
                "Transcript evidence cannot exist when transcript context was not supplied.");
        }

        foreach (VisualSemanticEditorialObservedChange change in changes)
        {
            VisualSemanticEditorialEvidenceInterval[] referenced =
                change.EvidenceIntervalIds
                    .Select(
                        id =>
                            byId.TryGetValue(
                                id,
                                out VisualSemanticEditorialEvidenceInterval?
                                    value)
                                ? value
                                : throw new ArgumentException(
                                    $"Observed change references unknown evidence interval '{id}'."))
                    .ToArray();
            bool hasVisual =
                referenced.Any(
                    static value =>
                        value.EvidenceBasis is
                            VisualSemanticEvidenceBasis.Visual or
                            VisualSemanticEvidenceBasis.Both);
            bool hasTranscript =
                referenced.Any(
                    static value =>
                        value.EvidenceBasis is
                            VisualSemanticEvidenceBasis.TranscriptContext or
                            VisualSemanticEvidenceBasis.Both);

            if (change.EvidenceBasis ==
                    VisualSemanticEvidenceBasis.Visual &&
                !hasVisual ||
                change.EvidenceBasis ==
                    VisualSemanticEvidenceBasis.TranscriptContext &&
                !hasTranscript ||
                change.EvidenceBasis ==
                    VisualSemanticEvidenceBasis.Both &&
                (!hasVisual || !hasTranscript))
            {
                throw new ArgumentException(
                    "Observed-change evidence basis is not supported by its cited intervals.");
            }
        }
    }

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
                "Prompt 2.0 enum values must be defined.");
        }
    }
}
