using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record EditorialFeedbackServices(
    IClipPreferenceFeedbackStore? ClipPreferences,
    IStudioCandidateDecisionStore? CandidateDecisions,
    IStudioHiddenMomentDecisionStore? HiddenMomentDecisions,
    ResearchParticipationState ResearchParticipation,
    IResearchFeedbackStore ResearchStore,
    ResearchFeedbackRecorder ResearchRecorder,
    IGenerationCandidateRefinementService? CandidateRefinement);

internal static class EditorialFeedbackComposition
{
    public static EditorialFeedbackServices Create(
        IGenerationSpeechActivityService? speechActivity)
    {
        IClipPreferenceFeedbackStore? clipPreferences =
            CreateClipPreferenceStore();
        IStudioCandidateDecisionStore? candidateDecisions =
            CreateCandidateDecisionStore();
        IStudioHiddenMomentDecisionStore? hiddenMomentDecisions =
            CreateHiddenMomentDecisionStore();
        ResearchParticipationState researchParticipation =
            CreateResearchParticipation();
        IResearchFeedbackStore researchStore = CreateResearchStore();
        var researchRecorder = new ResearchFeedbackRecorder(
            researchParticipation,
            researchStore);
        IGenerationCandidateRefinementService? candidateRefinement =
            speechActivity is null
                ? null
                : new GenerationCandidateRefinementService(
                    preferenceProfiles: clipPreferences);
        return new(
            clipPreferences,
            candidateDecisions,
            hiddenMomentDecisions,
            researchParticipation,
            researchStore,
            researchRecorder,
            candidateRefinement);
    }

    private static ResearchParticipationState CreateResearchParticipation()
    {
        try
        {
            return new ResearchParticipationState(
                new JsonResearchParticipationStore());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            return new ResearchParticipationState(
                new InMemoryResearchParticipationStore());
        }
    }

    private static IResearchFeedbackStore CreateResearchStore()
    {
        try
        {
            return new JsonResearchFeedbackStore();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            return new InMemoryResearchFeedbackStore();
        }
    }

    private static IClipPreferenceFeedbackStore? CreateClipPreferenceStore()
    {
        try
        {
            return JsonClipPreferenceFeedbackStore.CreateDefault();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Clip preference storage is unavailable",
                exception);
            return null;
        }
    }

    private static IStudioCandidateDecisionStore?
        CreateCandidateDecisionStore()
    {
        try
        {
            return new JsonStudioCandidateDecisionStore();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Studio candidate-decision storage is unavailable",
                exception);
            return null;
        }
    }

    private static IStudioHiddenMomentDecisionStore?
        CreateHiddenMomentDecisionStore()
    {
        try
        {
            return new JsonStudioHiddenMomentDecisionStore();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Hidden Moments decision storage is unavailable",
                exception);
            return null;
        }
    }
}
