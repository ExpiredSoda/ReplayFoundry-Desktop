namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public static class VisualSemanticEditorialTruthTableValidator
{
    private static readonly string[] MenuTerms =
    [
        "menu",
        "map",
        "inventory",
        "settings",
        "loading",
        "static overlay",
        "static-overlay",
    ];

    public static void Validate(
        VisualSemanticEditorialObservation observation,
        TimeSpan candidateStart,
        TimeSpan candidateEnd)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (candidateStart < TimeSpan.Zero ||
            candidateEnd <= candidateStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateEnd),
                "The Prompt 2.0 candidate interval must be positive and ordered.");
        }

        VisualSemanticEditorialRejectReason? established =
            HighestPriorityRejectReason(observation);

        switch (observation.EditorialDisposition)
        {
            case VisualSemanticEditorialDisposition.Keep:
                ValidateKeep(
                    observation,
                    candidateStart,
                    candidateEnd,
                    established);
                return;

            case VisualSemanticEditorialDisposition.Reject:
                if (!established.HasValue ||
                    observation.RejectReason != established.Value ||
                    observation.RejectReason is
                        VisualSemanticEditorialRejectReason.None or
                        VisualSemanticEditorialRejectReason
                            .InsufficientEvidence)
                {
                    throw Invalid(
                        "Reject must use the highest-priority established negative reason.");
                }

                return;

            case VisualSemanticEditorialDisposition.Unsure:
                ValidateUnsure(
                    observation,
                    candidateStart,
                    candidateEnd,
                    established);
                return;

            default:
                throw Invalid(
                    "The editorial disposition is undefined.");
        }
    }

    public static VisualSemanticEditorialRejectReason?
        HighestPriorityRejectReason(
            VisualSemanticEditorialObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (MenuReasonEstablished(observation))
        {
            return VisualSemanticEditorialRejectReason
                .MenuOrInventoryOnly;
        }

        if (observation.RoutineTraversalOrMenuOnly ==
            VisualSemanticTernary.Yes)
        {
            return VisualSemanticEditorialRejectReason.RoutineTraversal;
        }

        if (observation.CandidateContainsOnlyAmbientChange ==
            VisualSemanticTernary.Yes)
        {
            return VisualSemanticEditorialRejectReason.AmbientChangeOnly;
        }

        if (observation.HasDistinctEvent ==
            VisualSemanticTernary.No)
        {
            return VisualSemanticEditorialRejectReason.NoDistinctEvent;
        }

        if (observation.HasObservablePayoff ==
            VisualSemanticTernary.No)
        {
            return VisualSemanticEditorialRejectReason.NoObservablePayoff;
        }

        if (observation.CandidateRequiresMissingContext ==
            VisualSemanticTernary.Yes)
        {
            return VisualSemanticEditorialRejectReason
                .MissingRequiredContext;
        }

        return null;
    }

    private static void ValidateKeep(
        VisualSemanticEditorialObservation observation,
        TimeSpan candidateStart,
        TimeSpan candidateEnd,
        VisualSemanticEditorialRejectReason? established)
    {
        bool qualifyingGroundedChange =
            HasQualifyingGroundedChange(
                observation,
                candidateStart,
                candidateEnd);

        if (observation.HasDistinctEvent !=
                VisualSemanticTernary.Yes ||
            observation.HasObservablePayoff !=
                VisualSemanticTernary.Yes ||
            observation.RoutineTraversalOrMenuOnly !=
                VisualSemanticTernary.No ||
            observation.CandidateContainsOnlyAmbientChange !=
                VisualSemanticTernary.No ||
            observation.CandidateRequiresMissingContext !=
                VisualSemanticTernary.No ||
            established.HasValue ||
            observation.RejectReason !=
                VisualSemanticEditorialRejectReason.None ||
            !qualifyingGroundedChange)
        {
            throw Invalid(
                "Keep requires a distinct in-context payoff and visually grounded candidate-overlapping evidence.");
        }
    }

    private static void ValidateUnsure(
        VisualSemanticEditorialObservation observation,
        TimeSpan candidateStart,
        TimeSpan candidateEnd,
        VisualSemanticEditorialRejectReason? established)
    {
        bool ambiguous =
            observation.HasDistinctEvent ==
                VisualSemanticTernary.Unsure ||
            observation.HasObservablePayoff ==
                VisualSemanticTernary.Unsure ||
            observation.RoutineTraversalOrMenuOnly ==
                VisualSemanticTernary.Unsure ||
            observation.CandidateRequiresMissingContext ==
                VisualSemanticTernary.Unsure ||
            observation.CandidateContainsOnlyAmbientChange ==
                VisualSemanticTernary.Unsure ||
            observation.TranscriptContextSupport ==
                VisualSemanticTranscriptContextSupport
                    .UnreliableOrAmbiguous;
        bool assertsAllKeepRequirements =
            observation.HasDistinctEvent ==
                VisualSemanticTernary.Yes &&
            observation.HasObservablePayoff ==
                VisualSemanticTernary.Yes &&
            observation.RoutineTraversalOrMenuOnly ==
                VisualSemanticTernary.No &&
            observation.CandidateContainsOnlyAmbientChange ==
                VisualSemanticTernary.No &&
            observation.CandidateRequiresMissingContext ==
                VisualSemanticTernary.No &&
            HasQualifyingGroundedChange(
                observation,
                candidateStart,
                candidateEnd);

        if (!ambiguous ||
            assertsAllKeepRequirements ||
            established.HasValue ||
            observation.RejectReason !=
                VisualSemanticEditorialRejectReason
                    .InsufficientEvidence ||
            observation.UncertaintyReasons.Count == 0)
        {
            throw Invalid(
                "Unsure requires genuine typed ambiguity and no established Keep or Reject outcome.");
        }
    }

    private static bool MenuReasonEstablished(
        VisualSemanticEditorialObservation observation) =>
        observation.RoutineTraversalOrMenuOnly ==
            VisualSemanticTernary.Yes &&
        observation.ObservableContentType ==
            VisualSemanticObservableContentType.MenuOrTraversal &&
        observation.ObservedChanges.Any(
            change =>
                (change.EvidenceBasis is
                    VisualSemanticEvidenceBasis.Visual or
                    VisualSemanticEvidenceBasis.Both) &&
                MenuTerms.Any(
                    term =>
                        change.Description.Contains(
                            term,
                            StringComparison.OrdinalIgnoreCase)));

    private static bool HasQualifyingGroundedChange(
        VisualSemanticEditorialObservation observation,
        TimeSpan candidateStart,
        TimeSpan candidateEnd) =>
        observation.ObservedChanges.Any(
            change =>
                (change.EvidenceBasis is
                    VisualSemanticEvidenceBasis.Visual or
                    VisualSemanticEvidenceBasis.Both) &&
                change.EvidenceIntervalIds.Any(
                    id =>
                        Overlaps(
                            observation.EvidenceIntervals.Single(
                                interval =>
                                    string.Equals(
                                        interval.Id,
                                        id,
                                        StringComparison.Ordinal)),
                            candidateStart,
                            candidateEnd)));

    private static bool Overlaps(
        VisualSemanticEditorialEvidenceInterval interval,
        TimeSpan candidateStart,
        TimeSpan candidateEnd) =>
        interval.Start <= candidateEnd &&
        interval.End >= candidateStart;

    private static ArgumentException Invalid(
        string message) =>
        new(
            $"Prompt 2.0 disposition truth-table violation: {message}");
}
