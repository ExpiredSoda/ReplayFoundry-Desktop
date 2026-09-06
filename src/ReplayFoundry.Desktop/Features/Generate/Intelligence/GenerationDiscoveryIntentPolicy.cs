using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationDiscoveryIntentPolicy
{
    public static GenerationCandidateIntelligenceResult ApplyTranscriptIntent(
        GenerationCandidateIntelligenceResult intelligence, GenerationTranscriptAnalysisResult transcripts)
    {
        if (!ReferenceEquals(intelligence.BaseMoments.Request, transcripts.ExpandedMoments.Request))
        {
            throw new ArgumentException("Discovery transcripts must belong to the retained generation request.", nameof(transcripts));
        }
        GenerationDiscoveryIntent intent = intelligence.BaseMoments.Request.Setup.DiscoveryIntent;
        var refinements = new Dictionary<MomentCandidate, GenerationCandidateRefinement>(ReferenceEqualityComparer.Instance);
        foreach (GenerationSourceMomentResult source in intelligence.BaseMoments.Sources)
        {
            GenerationSourceTranscript? transcript = transcripts.Sources.SingleOrDefault(value =>
                value.SourceFullPath.Equals(source.AnalyzedSource.PreparedSource.Media.FullPath, StringComparison.OrdinalIgnoreCase));
            foreach (MomentCandidate candidate in source.Moments.Proposals)
            {
                GenerationCandidateRefinement existing = intelligence.Refinements.Single(value => ReferenceEquals(value.Candidate, candidate));
                var matches = transcript?.Segments.Where(segment =>
                    segment.AbsoluteSourceStart >= candidate.Window.Start &&
                    segment.AbsoluteSourceEnd <= candidate.Window.End && intent.CountMatches(segment.Text) > 0).ToArray() ?? [];
                GenerationCandidateRefinement updated = matches.Length == 0 ? existing : new(candidate,
                    [.. existing.Components.Where(static component => component.Code != GenerationCandidateRefinementComponentCode.SpokenIntentMatch),
                        new(GenerationCandidateRefinementComponentCode.SpokenIntentMatch, 1, 4,
                            "The timed transcript contains a creator-requested phrase. A spoken match does not establish that the described game event happened.",
                            matches.Select(static segment => "transcript:" + segment.Id))], "1.8");
                double similarity = GenerationSemanticRetrieval.Priority(transcripts.SemanticRetrieval,
                    source.AnalyzedSource.PreparedSource.Media.FullPath, candidate);
                if (similarity > 0)
                    updated = new(candidate,
                        [.. updated.Components.Where(static component => component.Code != GenerationCandidateRefinementComponentCode.SemanticRetrievalRelevance),
                            new(GenerationCandidateRefinementComponentCode.SemanticRetrievalRelevance, similarity, 0,
                                "English transcript similarity guides bounded review. A complete Keep may permit a separate " +
                                    "capped final query preference; this component adds no event score and does not verify " +
                                    "the objective, negation, or hypothetical speech.",
                                transcripts.SemanticRetrieval!.Matches.Where(match =>
                                    string.Equals(match.Window.SourceFullPath, source.AnalyzedSource.PreparedSource.Media.FullPath, StringComparison.OrdinalIgnoreCase) &&
                                    match.Window.Start >= candidate.Window.Start && match.Window.End <= candidate.Window.End)
                                    .SelectMany(static match => match.Window.SegmentIds).Select(static id => "transcript:semantic-retrieval:" + id))], "1.11");
                refinements.Add(candidate, updated);
            }
        }
        var selected = new GenerationMomentPortfolioSelector().Select(intelligence.BaseMoments.Request,
            intelligence.BaseMoments.Sources, refinements, CancellationToken.None);
        var moments = new GenerationMomentFindingResult(intelligence.BaseMoments.Request,
            intelligence.BaseMoments.Sources, selected, refinements);
        return new(intelligence.BaseMoments, intelligence.SpeechActivity, refinements.Values,
            moments, intelligence.VisualSemantic, transcripts);
    }

    public static GenerationCandidateRefinementComponent SemanticMatch(GenerationDiscoveryIntent intent,
        GenerationVisualSemanticCandidateObservation reviewed)
    {
        var observation = reviewed.Observation;
        bool matches = intent.MomentType switch
        {
            GenerationMomentIntent.Action => observation.ObservableContentType == VisualSemanticObservableContentType.Action,
            GenerationMomentIntent.Humor => observation.ObservableContentType == VisualSemanticObservableContentType.Humor,
            GenerationMomentIntent.Story => observation.ObservableContentType == VisualSemanticObservableContentType.Story,
            GenerationMomentIntent.Discovery => observation.ObservableContentType == VisualSemanticObservableContentType.Discovery,
            GenerationMomentIntent.Failure => observation.ObservableContentType == VisualSemanticObservableContentType.Failure,
            GenerationMomentIntent.Dialogue => observation.ObservableContentType == VisualSemanticObservableContentType.Dialogue,
            _ => false,
        };
        bool grounded = observation.EditorialDisposition == VisualSemanticEditorialDisposition.Keep &&
            observation.UncertaintyReasons.Count == 0 && observation.EvidenceIntervals.Count > 0 &&
            reviewed.ReviewedSourceStart <= reviewed.Candidate.Window.Start &&
            reviewed.ReviewedSourceEnd >= reviewed.Candidate.Window.End;
        return new(GenerationCandidateRefinementComponentCode.CreatorIntentMatch,
            matches && grounded ? 1 : 0, 6,
            intent.MomentType is GenerationMomentIntent.Clutch or GenerationMomentIntent.Tutorial or GenerationMomentIntent.Reaction
                ? "The requested objective guides transcript retrieval and review priority only. This review does not label a clutch, tutorial, or reaction as detected."
                : $"A complete grounded review can prioritize the requested {intent.MomentType} content type while preserving capture and editorial eligibility.",
            observation.EvidenceIntervals.Select(value => $"qwen:{reviewed.Candidate.Id}:{value.Id}:{value.Start:c}"));
    }
}
