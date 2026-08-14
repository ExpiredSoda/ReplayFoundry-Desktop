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
                "The selected AI detection method is not connected in this " +
                "build yet. Choose Heuristics to continue with deterministic evidence analysis.");
        }

        if (setupOptions.AudioSelectionMode !=
            AudioSelectionMode.Auto)
        {
            throw new GenerationEngineUnavailableException(
                "Manual audio selection is not connected in this build yet. " +
                "Choose Automatic audio selection to continue.");
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
        if (setupOptions.AnalysisDepth is not GenerationAnalysisDepth.Fast &&
            runtimeCapabilities is not null &&
            !runtimeCapabilities.IsSpeechActivityAvailable)
        {
            throw new GenerationEngineUnavailableException(
                "Balanced and Thorough require the approved local speech-activity model. Configure REPLAYFOUNDRY_SILERO_VAD_MODEL or choose Fast.");
        }
        if (setupOptions.AnalysisDepth == GenerationAnalysisDepth.Thorough &&
            runtimeCapabilities is not null &&
            !runtimeCapabilities.IsVisualSemanticReviewAvailable)
        {
            throw new GenerationEngineUnavailableException(
                "Thorough requires the qualified bounded visual-review provider. Choose Balanced or Fast until it is configured.");
        }
        if (setupOptions.CaptionSettings.IsEnabled &&
            runtimeCapabilities is not null &&
            !runtimeCapabilities.IsCaptionTranscriptionAvailable)
        {
            throw new GenerationEngineUnavailableException(
                "Captions require explicit local whisper.cpp executable and model " +
                "paths. Configure REPLAYFOUNDRY_WHISPER_EXE and " +
                "REPLAYFOUNDRY_WHISPER_MODEL, or disable captions before analysis.");
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
                    "A caption selection does not match an inspected absolute audio stream.");
            }
        }

        if (preparation.Sources
            .Where(static source => source.Media.AudioStreams.Count > 0)
            .Any(
                source =>
                    captions.FindForSource(source.Media.FullPath) is null))
        {
            throw new GenerationSourceException(
                "Every source with audio requires an explicit transcription stream when captions are enabled.");
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
