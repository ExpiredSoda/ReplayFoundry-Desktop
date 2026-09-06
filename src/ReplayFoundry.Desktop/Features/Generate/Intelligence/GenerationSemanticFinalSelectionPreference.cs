using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationSemanticFinalSelectionPreference
{
    internal const string PolicyVersion = "semantic-final-selection-preference-1";
    internal const double MaximumBonus = 1;
    internal const string Explanation =
        "An explicit query can add at most one point to final selection preference among safe, quality-qualified, fully reviewed Keep candidates. " +
        "Footage novelty scales this preference. It changes no event, quality, or ranking score; similarity is not confidence or proof of the requested event.";

    internal static IReadOnlyDictionary<MomentCandidate, double> Create(GenerationCandidateIntelligenceResult intelligence)
    {
        ArgumentNullException.ThrowIfNull(intelligence);
        var preferences = new Dictionary<MomentCandidate, double>(ReferenceEqualityComparer.Instance);
        var setup = intelligence.BaseMoments.Request.Setup;
        var transcripts = intelligence.Transcripts;
        if (string.IsNullOrWhiteSpace(setup.DiscoveryIntent.NaturalLanguageQuery) ||
            transcripts?.SemanticRetrieval is not { } retrieval ||
            !ReferenceEquals(transcripts.ExpandedMoments.Request, intelligence.BaseMoments.Request) ||
            intelligence.VisualSemantic is not { Outcome: GenerationVisualSemanticOutcome.Completed } visual)
            return preferences;

        foreach (GenerationSourceMomentResult source in intelligence.BaseMoments.Sources)
        {
            string path = source.AnalyzedSource.PreparedSource.Media.FullPath;
            var transcript = transcripts.Sources.SingleOrDefault(value =>
                value.SourceFullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (transcript is null || !source.AnalyzedSource.PreparedSource.Media.AudioStreams.Any(stream => stream.Index == transcript.AudioStreamIndex))
                continue;
            foreach (MomentCandidate candidate in source.Moments.Proposals)
            {
                GenerationCandidateRefinement refinement = intelligence.Refinements.Single(value => ReferenceEquals(value.Candidate, candidate));
                if (!GenerationAutomaticCandidateEligibility.IsEligible(candidate, refinement) || refinement.FinalScore < setup.QualityThreshold)
                    continue;
                var reviewed = visual.Observations.SingleOrDefault(value => ReferenceEquals(value.Candidate, candidate));
                if (reviewed is null || !ReferenceEquals(reviewed.Source, source.AnalyzedSource) ||
                    reviewed.ReviewedSourceStart > candidate.Window.Start || reviewed.ReviewedSourceEnd < candidate.Window.End)
                    continue;
                var observation = reviewed.Observation;
                if (observation.EditorialDisposition != VisualSemanticEditorialDisposition.Keep ||
                    observation.RejectReason != VisualSemanticEditorialRejectReason.None ||
                    observation.HasDistinctEvent != VisualSemanticTernary.Yes || observation.HasObservablePayoff != VisualSemanticTernary.Yes ||
                    observation.RoutineTraversalOrMenuOnly != VisualSemanticTernary.No ||
                    observation.CandidateRequiresMissingContext != VisualSemanticTernary.No ||
                    observation.CandidateContainsOnlyAmbientChange != VisualSemanticTernary.No ||
                    observation.TranscriptContextSupport == VisualSemanticTranscriptContextSupport.UnreliableOrAmbiguous ||
                    observation.UncertaintyReasons.Count != 0 || observation.EvidenceIntervals.Count == 0)
                    continue;
                double similarity = retrieval.Matches.Where(match =>
                        string.Equals(match.Window.SourceFullPath, path, StringComparison.OrdinalIgnoreCase) &&
                        match.Window.AudioStreamIndex == transcript.AudioStreamIndex &&
                        match.Window.Start >= candidate.Window.Start && match.Window.End <= candidate.Window.End &&
                        double.IsFinite(match.Similarity))
                    .Select(static match => Math.Max(0, match.Similarity)).DefaultIfEmpty(0).Max();
                if (double.IsFinite(similarity) && similarity > 0)
                    preferences.Add(candidate, Math.Clamp(similarity, 0, 1) * MaximumBonus);
            }
        }
        return preferences;
    }
}
