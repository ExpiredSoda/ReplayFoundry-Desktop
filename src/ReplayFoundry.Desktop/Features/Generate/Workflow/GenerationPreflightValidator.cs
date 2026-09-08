using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Preparation;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal static class GenerationPreflightValidator
{
    public static void ValidateSupportedInputs(
        GenerationSourcePreparationResult preparation,
        GenerationSetupOptions setupOptions,
        GenerationCompositionReviewResult compositionReview,
        GenerationRuntimeCapabilities? runtimeCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(setupOptions);
        ArgumentNullException.ThrowIfNull(compositionReview);

        if (setupOptions.DetectionMethod !=
            DetectionMethod.Heuristics)
        {
            throw new GenerationEngineUnavailableException(
                "The selected AI clip-finding option is not available yet. " +
                "Choose Fast to continue.");
        }

        if (setupOptions.AudioSelectionMode !=
            AudioSelectionMode.Auto)
        {
            throw new GenerationEngineUnavailableException(
                "Manual audio selection is not available yet. Choose " +
                "Automatic audio selection to continue.");
        }

        if (!ReferenceEquals(
                preparation,
                compositionReview.Preparation))
        {
            throw new GenerationSourceException(
                "The confirmed video layouts no longer match the prepared sources.");
        }

        if (compositionReview.SourcePlans.Count !=
            preparation.Sources.Count)
        {
            throw new GenerationSourceException(
                "Every prepared source requires one confirmed video layout.");
        }

        for (int index = 0;
             index < preparation.Sources.Count;
             index++)
        {
            if (!ReferenceEquals(
                    preparation.Sources[index],
                    compositionReview
                        .SourcePlans[index]
                        .PreparedSource))
            {
                throw new GenerationSourceException(
                    "A confirmed video layout does not match its prepared source.");
            }
        }

        ValidateCaptions(preparation, setupOptions.CaptionSettings);
        if (setupOptions.DiscoveryIntent.UsesSemanticRetrieval &&
            (setupOptions.AnalysisDepth != GenerationAnalysisDepth.Thorough ||
             runtimeCapabilities?.IsCaptionTranscriptionAvailable == false))
            throw new GenerationEngineUnavailableException(
                "Semantic search needs a Thorough scan and a recognized speech-to-text model from Advanced AI.");
        if (setupOptions.CaptionSettings.IsEnabled && runtimeCapabilities?.CaptionLanguageCapabilities is { } languages)
        {
            foreach (GenerationCaptionSourceSelection selection in setupOptions.CaptionSettings.SourceSelections)
            {
                if (GenerationCaptionLanguageCatalog.GetUnavailableReason(selection.LanguagePolicy, languages) is string reason)
                    throw new GenerationEngineUnavailableException(reason);
            }
        }
        if (setupOptions.MetadataAuthoringMode == GenerationMetadataAuthoringMode.AiRequired &&
            runtimeCapabilities?.IsEditorialAiAvailable == true &&
            runtimeCapabilities.EditorialGpuAdmissionCheck?.Invoke() is string gpuReason)
            throw new GenerationEngineUnavailableException(gpuReason);
        if (setupOptions.MetadataAuthoringMode ==
                GenerationMetadataAuthoringMode.AiRequired &&
            runtimeCapabilities is not null &&
            !runtimeCapabilities.IsEditorialAiAvailable)
        {
            throw new GenerationEngineUnavailableException(
                runtimeCapabilities.EditorialAiBlockingReason);
        }
        if (setupOptions.AnalysisDepth is not GenerationAnalysisDepth.Fast &&
            runtimeCapabilities is not null &&
            !runtimeCapabilities.IsSpeechActivityAvailable)
        {
            throw new GenerationEngineUnavailableException(
                "Balanced and Thorough need the speech tool from Advanced AI. Install or repair Advanced AI, or choose Fast.");
        }
        if (setupOptions.RequiresVisualSelectionReview &&
            runtimeCapabilities is not null &&
            !runtimeCapabilities.IsVisualSemanticReviewAvailable)
        {
            throw new GenerationEngineUnavailableException(
                "AI moment selection needs the visual tool from Advanced AI. Install or repair Advanced AI, or choose Fast or simple writing.");
        }
        if (setupOptions.CaptionSettings.IsEnabled &&
            runtimeCapabilities is not null &&
            !runtimeCapabilities.IsCaptionTranscriptionAvailable)
        {
            throw new GenerationEngineUnavailableException(
                "Spoken captions need the speech-to-text tool from Advanced AI. Install or repair Advanced AI, or turn off spoken captions.");
        }
    }

    private static void ValidateCaptions(
        GenerationSourcePreparationResult preparation,
        GenerationCaptionSettings captions)
    {
        if (!captions.IsEnabled)
        {
            return;
        }

        foreach (GenerationCaptionSourceSelection selection in
                 captions.SourceSelections)
        {
            PreparedGenerationSource? prepared =
                preparation.Sources.SingleOrDefault(
                    source =>
                        source.Media.FullPath.Equals(
                            selection.SourceFullPath,
                            StringComparison.OrdinalIgnoreCase));
            if (prepared is null ||
                !prepared.Media.AudioStreams.Any(
                    stream =>
                        stream.Index ==
                        selection.AbsoluteAudioStreamIndex))
            {
                throw new GenerationSourceException(
                    "The selected caption audio track is no longer available. Choose the track again.");
            }
        }

        if (preparation.Sources
            .Where(static source => source.Media.AudioStreams.Count > 0)
            .Any(
                source =>
                    captions.FindForSource(source.Media.FullPath) is null))
        {
            throw new GenerationSourceException(
                "Choose a speech track for every video with audio, or turn off spoken captions.");
        }
    }

    public static void ValidateEvidence(
        GenerationSourcePreparationResult preparation,
        GenerationCompositionReviewResult compositionReview,
        GenerationEvidenceAnalysisResult evidenceAnalysis)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(compositionReview);
        ArgumentNullException.ThrowIfNull(evidenceAnalysis);

        if (!ReferenceEquals(
                preparation,
                evidenceAnalysis.Request.Preparation))
        {
            throw new ArgumentException(
                "Evidence analysis must belong to the retained source preparation.",
                nameof(evidenceAnalysis));
        }

        if (!ReferenceEquals(
                compositionReview,
                evidenceAnalysis.Request.CompositionReview))
        {
            throw new ArgumentException(
                "Evidence analysis must be rebound to the current composition review.",
                nameof(evidenceAnalysis));
        }

        if (evidenceAnalysis.Sources.Count !=
            preparation.Sources.Count)
        {
            throw new ArgumentException(
                "Every prepared source requires one completed evidence result.",
                nameof(evidenceAnalysis));
        }

        for (int index = 0;
             index < preparation.Sources.Count;
             index++)
        {
            AnalyzedGenerationSource analyzed =
                evidenceAnalysis.Sources[index];

            if (!ReferenceEquals(
                    preparation.Sources[index],
                    analyzed.PreparedSource) ||
                !ReferenceEquals(
                    compositionReview.SourcePlans[index],
                    analyzed.CompositionPlan))
            {
                throw new ArgumentException(
                    "Evidence source order, preparation identity, and composition-plan identity must match.",
                    nameof(evidenceAnalysis));
            }

            if (!string.Equals(
                    analyzed.Evidence.FullPath,
                    analyzed.PreparedSource.Media.FullPath,
                    StringComparison.OrdinalIgnoreCase) ||
                analyzed.Evidence.SourceDuration !=
                    analyzed.PreparedSource.Media.Duration)
            {
                throw new ArgumentException(
                    "Evidence path and duration must match the prepared source.",
                    nameof(evidenceAnalysis));
            }
        }

        if (!ReferenceEquals(
                evidenceAnalysis.ReferenceSource.PreparedSource,
                preparation.ReferenceSource))
        {
            throw new ArgumentException(
                "Evidence analysis must preserve the explicit reference source.",
                nameof(evidenceAnalysis));
        }
    }
}
