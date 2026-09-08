using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public static class GenerationClipPreferenceFeatureExtractor
{
    private const double DurationNormalizationSeconds = 180;

    public static ClipPreferenceFeatureVector Create(
        GenerationMomentCandidate candidate,
        GenerationSetupOptions? setup = null,
        GenerationVisualSemanticCandidateObservation? review = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Create(candidate.Candidate, candidate.Refinement,
            setup is null ? null : CreateContext(setup, candidate.AnalyzedSource.PreparedSource.Media.FullPath), review);
    }

    public static ClipPreferenceFeatureVector Create(
        MomentCandidate moment,
        GenerationCandidateRefinement? refinement,
        ClipPreferenceContext? context = null,
        GenerationVisualSemanticCandidateObservation? review = null)
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
        AddRefinement(features, refinement, GenerationCandidateRefinementComponentCode.NeuralGameplay, ClipPreferenceFeatureCode.ObservedGameplay);
        AddRefinement(features, refinement, GenerationCandidateRefinementComponentCode.NeuralHumor, ClipPreferenceFeatureCode.ObservedHumor);
        AddRefinement(features, refinement, GenerationCandidateRefinementComponentCode.NeuralCommentary, ClipPreferenceFeatureCode.ObservedCommentary);
        AddRefinement(features, refinement, GenerationCandidateRefinementComponentCode.NeuralMenu, ClipPreferenceFeatureCode.ObservedMenu);
        AddRefinement(features, refinement, GenerationCandidateRefinementComponentCode.NeuralLore, ClipPreferenceFeatureCode.ObservedLore);
        AddRefinement(features, refinement, GenerationCandidateRefinementComponentCode.NeuralIndexCoverage, ClipPreferenceFeatureCode.RecordingCoverage);
        return new ClipPreferenceFeatureVector(features, context, GenerationMomentContentClassifier.Classify(moment, features, review, refinement));
    }

    public static ClipPreferenceContext CreateContext(GenerationSetupOptions setup, string sourcePath)
    {
        GenerationSourceGameContext? game = setup.GameContextSettings.Find(sourcePath);
        return new(game?.IsUserGrounded == true ? game.GameName : "Unspecified game",
            setup.Mode.ToString(), setup.ContentEmphasis.ToString(), setup.DiscoveryIntent.MomentType.ToString());
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
