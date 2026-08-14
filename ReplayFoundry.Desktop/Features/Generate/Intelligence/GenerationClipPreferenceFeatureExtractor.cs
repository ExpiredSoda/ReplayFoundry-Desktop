using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public static class GenerationClipPreferenceFeatureExtractor
{
    private const double DurationNormalizationSeconds = 180;

    public static ClipPreferenceFeatureVector Create(
        GenerationMomentCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Create(candidate.Candidate, candidate.Refinement);
    }

    public static ClipPreferenceFeatureVector Create(
        MomentCandidate moment,
        GenerationCandidateRefinement? refinement)
    {
        ArgumentNullException.ThrowIfNull(moment);
        var features = new List<ClipPreferenceFeature>
        {
            new(
                ClipPreferenceFeatureCode.Duration,
                Math.Clamp(
                    moment.Window.Duration.TotalSeconds /
                    DurationNormalizationSeconds,
                    0,
                    1)),
            new(
                ClipPreferenceFeatureCode.DeterministicScore,
                Math.Clamp(moment.HeuristicScore / 100d, 0, 1)),
        };

        if (moment.EpisodeFeatures is MomentEpisodeFeatureVector episode)
        {
            features.Add(new(
                ClipPreferenceFeatureCode.EpisodeDistinctiveness,
                episode.Distinctiveness));
            features.Add(new(
                ClipPreferenceFeatureCode.EpisodeOnset,
                episode.OnsetStrength));
            features.Add(new(
                ClipPreferenceFeatureCode.EpisodeRecovery,
                episode.RecoverySupport));
            features.Add(new(
                ClipPreferenceFeatureCode.ContinuousActivity,
                episode.ContinuousActivityRatio));
        }

        AddRefinement(
            features,
            refinement,
            GenerationCandidateRefinementComponentCode.SpeechCoverage,
            ClipPreferenceFeatureCode.SpeechCoverage);
        AddRefinement(
            features,
            refinement,
            GenerationCandidateRefinementComponentCode.UserConfirmedCreatorSpeech,
            ClipPreferenceFeatureCode.CreatorSpeech);
        AddRefinement(
            features,
            refinement,
            GenerationCandidateRefinementComponentCode.UserConfirmedGameDialogue,
            ClipPreferenceFeatureCode.GameDialogue);
        AddRefinement(
            features,
            refinement,
            GenerationCandidateRefinementComponentCode.VisualSemanticSupport,
            ClipPreferenceFeatureCode.VisualSemanticSupport);
        AddRefinement(
            features,
            refinement,
            GenerationCandidateRefinementComponentCode.VisualSemanticEditorialPenalty,
            ClipPreferenceFeatureCode.VisualSemanticRejection);
        return new ClipPreferenceFeatureVector(features);
    }

    private static void AddRefinement(
        ICollection<ClipPreferenceFeature> target,
        GenerationCandidateRefinement? refinement,
        GenerationCandidateRefinementComponentCode componentCode,
        ClipPreferenceFeatureCode featureCode)
    {
        GenerationCandidateRefinementComponent? component = refinement?
            .Components.SingleOrDefault(value => value.Code == componentCode);
        if (component is not null)
        {
            target.Add(new(featureCode, component.RawValue));
        }
    }
}
