using ReplayFoundry.Desktop.Features.Generate.Editorial.VisualText;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public interface IGenerationCaptureContextScreeningService
{
    Task<GenerationCandidateIntelligenceResult> ScreenAsync(GenerationCandidateIntelligenceResult intelligence,
        IProgress<string>? progress, CancellationToken cancellationToken);
}

public sealed class GenerationCaptureContextScreeningService(IGenerationVisualTextAnalysisService visualText,
    Action<GenerationCaptureContextDiagnostic>? diagnostics = null)
    : IGenerationCaptureContextScreeningService
{
    public async Task<GenerationCandidateIntelligenceResult> ScreenAsync(GenerationCandidateIntelligenceResult intelligence,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        GenerationSetupOptions setup = intelligence.BaseMoments.Request.Setup;
        if (!visualText.IsAvailable || !GenerationCaptureContextPolicy.MayScreen(setup.DiscoveryIntent, setup.ContentEmphasis))
            return intelligence;
        var refinements = new Dictionary<MomentCandidate, GenerationCandidateRefinement>(ReferenceEqualityComparer.Instance);
        foreach (var refinement in intelligence.Refinements) refinements.Add(refinement.Candidate, refinement);
        var review = intelligence.Refinements.Where(value => GenerationAutomaticCandidateEligibility.IsEligible(value.Candidate, value))
            .OrderByDescending(static value => value.RankingScore).Take(Math.Clamp(setup.DesiredResultCount * 2, 4, 12)).ToArray();
        for (int index = 0; index < review.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = review[index];
            MomentCandidate candidate = existing.Candidate;
            GenerationSourceMomentResult source = intelligence.BaseMoments.Sources.Single(value => value.Moments.Proposals.Any(proposal => ReferenceEquals(proposal, candidate)));
            var media = source.AnalyzedSource.PreparedSource.Media;
            TimeSpan start = candidate.Window.Start;
            TimeSpan end = candidate.Window.End;
            TimeSpan midpoint = start + TimeSpan.FromTicks((end - start).Ticks / 2);
            var layout = source.AnalyzedSource.CompositionPlan.Plan.GetLayoutAt(midpoint);
            var gameplay = CompositionRegionSelector.FindPrimary(layout, CompositionRegionRole.Gameplay);
            if (gameplay?.RoleSource != CompositionValueSource.UserConfirmed) continue;
            progress?.Report($"Checking visible application and loading labels in moment {index + 1} of {review.Length}.");
            var context = new ClipEditorialContext(candidate.Id, media.FullPath, System.IO.Path.GetFileName(media.FullPath),
                start, end, media.Duration, candidate.HeuristicScore, "A candidate awaiting capture-context screening.", gameplayRegion: gameplay.Geometry);
            TimeSpan inset = TimeSpan.FromTicks((end - start).Ticks / 10);
            ClipVisualTextContext? retainedText = await ReadOwnedAsync([start + inset, midpoint, end - inset]);
            VisualTextFrameObservation[] frames = retainedText?.Frames.ToArray() ?? [];
            TimeSpan[] additional = GenerationCaptureContextPolicy.AdditionalSampleTimestamps(candidate.Window,
                frames.Select(static frame => frame.Request.Frame.RequestedTimestamp).ToArray(), Lines(frames)).ToArray();
            if (additional.Length > 0)
            {
                ClipVisualTextContext? followup = await ReadOwnedAsync(additional);
                frames = frames.Concat(followup?.Frames ?? []).DistinctBy(static frame => frame.Request.Frame.RequestedTimestamp)
                    .OrderBy(static frame => frame.Request.Frame.RequestedTimestamp).Take(5).ToArray();
                retainedText = new(candidate.Id, media.FullPath, gameplay.Geometry, frames, [],
                    (retainedText?.Warnings ?? []).Concat(followup?.Warnings ?? []));
            }
            GenerationCaptureContextAssessment? assessment = GenerationCaptureContextPolicy.Assess(Lines(frames));
            GenerationSourceSpeechActivity sourceSpeech = intelligence.SpeechActivity.FindSource(media.FullPath);
            GenerationApplicationStartupLeadIn? leadIn = assessment is null ? null :
                GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(assessment,
                    frames.Select(static frame => frame.Request.Frame.RequestedTimestamp).ToArray(), sourceSpeech, candidate, setup);
            if (retainedText is not null)
            {
                try
                {
                    diagnostics?.Invoke(GenerationCaptureContextDiagnostic.Create(candidate.Id, start, end, retainedText) with
                    {
                        AdditionalRequestedSampleSeconds = additional.Select(static value => value.TotalSeconds).ToArray(),
                        FirstSpeechStartSeconds = leadIn?.FirstSpeechStart.TotalSeconds,
                        ApplicationStartupLeadIn = leadIn is not null,
                    });
                }
                catch (Exception) { /* Optional developer diagnostics cannot change ranking. */ }
            }
            if (assessment is null) continue;
            double penalty = assessment.Kind == GenerationCaptureContextKind.Loading ? 12 :
                setup.ContentEmphasis == ContentEmphasis.GameplayFocused ? 30 : 20;
            if (existing.Components.Any(static component => component.Code == GenerationCandidateRefinementComponentCode.UserConfirmedCreatorSpeech && component.RawValue > 0))
                penalty /= 2;
            var references = assessment.FrameIndexes.Select(frame => $"ocr:screen:{candidate.Id}:{frames[frame].Request.Frame.RequestedTimestamp:c}").ToArray();
            bool excludeAutomatically = GenerationCaptureContextPolicy.ShouldExcludeAutomatically(assessment,
                sourceSpeech, candidate, setup);
            string[] leadInReferences = leadIn is null ? [] : leadIn.FrameIndexes.Select(frame =>
                    $"ocr:startup-lead-in:{candidate.Id}:{frames[frame].Request.Frame.RequestedTimestamp:c}")
                .Append($"vad:all-streams:leading-silence:{start:c}-{leadIn.FirstSpeechStart:c}").ToArray();
            refinements[candidate] = new(candidate,
                [.. existing.Components.Where(static component => component.Code is not (
                    GenerationCandidateRefinementComponentCode.CaptureContextPenalty or
                    GenerationCandidateRefinementComponentCode.NonGameplayCapture or
                    GenerationCandidateRefinementComponentCode.ApplicationStartupLeadIn)),
                new(GenerationCandidateRefinementComponentCode.CaptureContextPenalty,
                    1, -penalty, $"Repeated OCR labels identify a {assessment.Kind.ToString().ToLowerInvariant()} " +
                        "surface in the sampled gameplay region. This bounded ranking penalty preserves the clip for review.",
                    references),
                new(GenerationCandidateRefinementComponentCode.NonGameplayCapture, excludeAutomatically ? 1 : 0, 0,
                    excludeAutomatically
                        ? "Repeated software-interface labels and successful speech analysis with no detected speech " +
                            "keep this application capture out of automatic clips. It remains available for manual review."
                        : "Capture screening preserves clips with speech, incomplete speech evidence, loading alone, or a matching user marker or range.",
                    references),
                new(GenerationCandidateRefinementComponentCode.ApplicationStartupLeadIn, leadIn is not null ? 1 : 0, 0,
                    leadIn is not null
                        ? "Repeated application-interface labels occur in a long silent opening before speech begins. " +
                            "Inspect and trim this opening in Studio before including the clip; it may also contain valid gameplay later."
                        : "Application startup review requires repeated labels inside a verified long silent opening, with creator intent and guidance preserved.",
                    leadInReferences)], "1.11");

            async Task<ClipVisualTextContext?> ReadOwnedAsync(IReadOnlyList<TimeSpan> timestamps)
            {
                var observed = await visualText.EnrichAsync(new(context, media, timestamps, maximumSampleCount: timestamps.Count), cancellationToken);
                ClipVisualTextContext? text = observed.VisualText;
                if (text is null || !text.CandidateId.Equals(candidate.Id, StringComparison.Ordinal) ||
                    !text.SourceFullPath.Equals(media.FullPath, StringComparison.OrdinalIgnoreCase) ||
                    text.ContentRegion.X != gameplay.Geometry.X || text.ContentRegion.Y != gameplay.Geometry.Y ||
                    text.ContentRegion.Width != gameplay.Geometry.Width || text.ContentRegion.Height != gameplay.Geometry.Height) return null;
                VisualTextFrameObservation[] owned = text.Frames.Where(frame =>
                        frame.Request.Frame.SourceDuration == media.Duration && frame.Request.Frame.VideoStreamIndex == media.PrimaryVideoStream.Index &&
                        timestamps.Contains(frame.Request.Frame.RequestedTimestamp))
                    .DistinctBy(static frame => frame.Request.Frame.RequestedTimestamp).Take(timestamps.Count).ToArray();
                return new(candidate.Id, media.FullPath, gameplay.Geometry, owned, [], text.Warnings);
            }
        }
        var selected = new GenerationMomentPortfolioSelector().Select(intelligence.BaseMoments.Request,
            intelligence.BaseMoments.Sources, refinements, cancellationToken);
        return new(intelligence.BaseMoments, intelligence.SpeechActivity, refinements.Values,
            new GenerationMomentFindingResult(intelligence.BaseMoments.Request, intelligence.BaseMoments.Sources, selected, refinements),
            intelligence.VisualSemantic, intelligence.Transcripts);
    }

    private static IReadOnlyList<IReadOnlyList<string>> Lines(IEnumerable<VisualTextFrameObservation> frames) =>
        frames.Select(frame => (IReadOnlyList<string>)frame.Lines.Select(static line => line.Text).ToArray()).ToArray();
}
