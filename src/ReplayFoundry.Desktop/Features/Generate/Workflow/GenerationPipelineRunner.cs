using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal sealed class GenerationPipelineRunner : IGenerationRunner
{
    private readonly GenerationPreflightRunner _preflight;
    private readonly IGenerationMomentFindingService _momentFinder;
    private readonly GenerationTasteRanking? _tasteRanking;
    private readonly IGenerationOutputPathProvider _outputPathProvider;
    private readonly IGenerationCaptionPreparationService?
        _captionPreparation;
    private readonly IGenerationOutputSink? _outputSink;
    private readonly IGenerationEditorialMetadataService
        _editorialMetadata;
    private readonly IGenerationSpeechActivityService? _speechActivity;
    private readonly IGenerationCandidateRefinementService? _candidateRefinement;
    private readonly IGenerationVisualSemanticAnalysisService? _visualSemantic;
    private readonly IGenerationTranscriptAnalysisService? _transcriptAnalysis;
    private readonly IGenerationCaptureContextScreeningService? _captureScreening;

    public GenerationPipelineRunner(
        GenerationPreflightRunner preflight,
        IGenerationMomentFindingService momentFinder,
        IGenerationOutputPathProvider outputPathProvider,
        IGenerationEditorialMetadataService editorialMetadata,
        IGenerationOutputSink? outputSink = null,
        IGenerationCaptionPreparationService? captionPreparation = null,
        IGenerationSpeechActivityService? speechActivity = null,
        IGenerationCandidateRefinementService? candidateRefinement = null,
        IGenerationVisualSemanticAnalysisService? visualSemantic = null,
        IGenerationTranscriptAnalysisService? transcriptAnalysis = null,
        IGenerationCaptureContextScreeningService? captureScreening = null,
        GenerationTasteRanking? tasteRanking = null)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(momentFinder);
        ArgumentNullException.ThrowIfNull(outputPathProvider);
        ArgumentNullException.ThrowIfNull(editorialMetadata);
        _preflight = preflight;
        _momentFinder = momentFinder;
        _outputPathProvider = outputPathProvider;
        _outputSink = outputSink;
        _captionPreparation = captionPreparation;
        _editorialMetadata = editorialMetadata;
        _speechActivity = speechActivity;
        _candidateRefinement = candidateRefinement;
        _visualSemantic = visualSemantic;
        _transcriptAnalysis = transcriptAnalysis;
        _captureScreening = captureScreening;
        _tasteRanking = tasteRanking;
    }

    public async Task<GenerationResult> RunAsync(
        GenerationRequest request,
        IProgress<GenerationProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        _preflight.Validate(request, progress, cancellationToken);

        GenerationVisualSemanticAnalysisResult? retainedReviewMedia = null;
        try
        {

            progress.Report(
                new GenerationProgressUpdate(
                    "Finding the moments worth keeping",
                    "Bringing visual rhythm, motion, and sound together to shape the shortlist.",
                    isIndeterminate: true));
            var momentRequest = new GenerationMomentFindingRequest(
                request.EvidenceAnalysis,
                request.SetupOptions);
            GenerationMomentFindingResult moments =
                await Task.Run(
                    () => _momentFinder.Find(
                        momentRequest,
                        cancellationToken),
                    cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            GenerationCandidateIntelligenceResult? candidateIntelligence = null;
            if (request.SetupOptions.DiscoveryIntent.UsesSemanticRetrieval &&
                (request.SetupOptions.AnalysisDepth != GenerationAnalysisDepth.Thorough || _transcriptAnalysis is null))
                throw new GenerationEngineUnavailableException("Semantic creator search requires Thorough analysis and the local transcript service.");
            if (request.SetupOptions.AnalysisDepth == GenerationAnalysisDepth.Thorough)
            {
                moments = GenerationSemanticExplorationPlanner.Expand(moments, cancellationToken);
            }
            if (request.SetupOptions.AnalysisDepth is not GenerationAnalysisDepth.Fast)
            {
                if (_speechActivity is null || _candidateRefinement is null)
                {
                    throw new GenerationEngineUnavailableException(
                        "Balanced and Thorough analysis require the verified local speech-activity model. Choose Fast or configure the approved VAD model explicitly.");
                }

                var speechProgress =
                    new SynchronousProgress<GenerationSpeechActivityProgress>(
                        update => progress.Report(new GenerationProgressUpdate(
                            update.Title,
                            update.Detail,
                            update.IsIndeterminate,
                            update.OverallPercentage is null
                                ? null
                                : 30 + update.OverallPercentage.Value * 0.15)));
                GenerationSpeechActivityResult speech =
                    await _speechActivity.AnalyzeAsync(
                        request,
                        speechProgress,
                        cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                GenerationTranscriptAnalysisResult? transcripts = null;
                if (request.SetupOptions.AnalysisDepth == GenerationAnalysisDepth.Thorough &&
                    _transcriptAnalysis is not null)
                {
                    transcripts = await _transcriptAnalysis.AnalyzeAsync(moments, speech,
                        new SynchronousProgress<string>(detail => progress.Report(new GenerationProgressUpdate(
                            "Understanding spoken moments", detail, isIndeterminate: true))), cancellationToken);
                    moments = transcripts.ExpandedMoments;
                }
                progress.Report(new GenerationProgressUpdate(
                    "Finding the strongest moments",
                    "Balancing spoken moments with the kind of clips you asked Replay Foundry to create.",
                    isIndeterminate: true));
                candidateIntelligence = await Task.Run(
                    () => _candidateRefinement.Refine(
                        moments,
                        speech,
                        transcripts,
                        cancellationToken),
                    cancellationToken);
                if (_visualSemantic is not null && request.SetupOptions.RequiresVisualSelectionReview)
                    candidateIntelligence = await _visualSemantic.IndexRecordingAsync(candidateIntelligence,
                        new SynchronousProgress<string>(detail => progress.Report(new GenerationProgressUpdate(
                            "Mapping your recording", detail, isIndeterminate: true))), cancellationToken);
                if (_captureScreening is not null && request.SetupOptions.AnalysisDepth == GenerationAnalysisDepth.Thorough)
                {
                    candidateIntelligence = await _captureScreening.ScreenAsync(candidateIntelligence,
                        new SynchronousProgress<string>(detail => progress.Report(new GenerationProgressUpdate(
                            "Checking recording context", detail, isIndeterminate: true))), cancellationToken);
                }
                if (_tasteRanking is not null)
                {
                    var personalized = await _tasteRanking.ApplyAsync(candidateIntelligence.RefinedMoments, candidateIntelligence, cancellationToken);
                    candidateIntelligence = new(candidateIntelligence.BaseMoments, candidateIntelligence.SpeechActivity,
                        personalized.Refinements.Count > 0 ? personalized.Refinements.Values : candidateIntelligence.Refinements,
                        personalized, candidateIntelligence.VisualSemantic, candidateIntelligence.Transcripts);
                }
                moments = candidateIntelligence.RefinedMoments;

                if (request.SetupOptions.RequiresVisualSelectionReview)
                {
                    if (_visualSemantic is null)
                    {
                        throw new GenerationEngineUnavailableException(
                            "AI moment selection needs the visual tool from Advanced AI. Install or repair Advanced AI, or choose Fast or simple writing.");
                    }

                    var visualProgress =
                        new SynchronousProgress<GenerationVisualSemanticProgress>(
                            update => progress.Report(new GenerationProgressUpdate(
                                update.Title,
                                update.Detail,
                                update.IsIndeterminate,
                                update.OverallPercentage is null
                                    ? null
                                    : 45 + update.OverallPercentage.Value * 0.05)));
                    GenerationCandidateIntelligenceResult preVisualIntelligence = candidateIntelligence;
                    GenerationVisualSemanticAnalysisResult visual =
                        await _visualSemantic.AnalyzeAsync(
                            candidateIntelligence,
                            visualProgress,
                            cancellationToken);
                    retainedReviewMedia = visual;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (visual.Outcome != GenerationVisualSemanticOutcome.Completed &&
                        request.SetupOptions.MetadataAuthoringMode == GenerationMetadataAuthoringMode.AiRequired)
                        throw new GenerationEngineUnavailableException(
                            "AI could not verify any shortlisted moments. Retry the picture check or repair Advanced AI. " + visual.FallbackReason);
                    progress.Report(new GenerationProgressUpdate(
                        "Applying picture details",
                        "Combining what Replay Foundry saw with timing, audio, and story clues.",
                        isIndeterminate: true));
                    candidateIntelligence = await Task.Run(
                        () => _candidateRefinement.ApplyVisualSemantic(
                            candidateIntelligence,
                            visual,
                            cancellationToken),
                        cancellationToken);
                    if (visual.Outcome == GenerationVisualSemanticOutcome.Completed)
                    {
                        visual = await _visualSemantic.ReviewPromotedAsync(preVisualIntelligence,
                            candidateIntelligence.RefinedMoments.SelectedCandidates, visual, visualProgress, cancellationToken);
                        retainedReviewMedia = visual;
                        cancellationToken.ThrowIfCancellationRequested();
                        candidateIntelligence = await Task.Run(() => _candidateRefinement.ApplyVisualSemantic(
                            preVisualIntelligence, visual, cancellationToken), cancellationToken);
                        candidateIntelligence = GenerationReviewedSelectionPolicy.Apply(candidateIntelligence, cancellationToken);
                    }
                    moments = candidateIntelligence.RefinedMoments;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (_tasteRanking is not null)
            {
                moments = await _tasteRanking.ApplyAsync(moments, candidateIntelligence, cancellationToken);
                if (candidateIntelligence is not null)
                    candidateIntelligence = new(candidateIntelligence.BaseMoments, candidateIntelligence.SpeechActivity,
                        moments.Refinements.Count > 0 ? moments.Refinements.Values : candidateIntelligence.Refinements,
                        moments, candidateIntelligence.VisualSemantic, candidateIntelligence.Transcripts);
            }
            if (_captureScreening is not null && candidateIntelligence is not null &&
                request.SetupOptions.AnalysisDepth == GenerationAnalysisDepth.Balanced)
            {
                candidateIntelligence = await _captureScreening.ScreenAsync(candidateIntelligence,
                    new SynchronousProgress<string>(detail => progress.Report(new GenerationProgressUpdate(
                        "Checking recording context", detail, isIndeterminate: true))), cancellationToken);
                moments = candidateIntelligence.RefinedMoments;
            }
            if (moments.SelectedCandidates.Count == 0)
            {
                if (candidateIntelligence?.VisualSemantic?.Outcome == GenerationVisualSemanticOutcome.Completed)
                    throw new GenerationSourceException(
                        "The bounded picture check found no eligible moments for automatic selection. " +
                        "Try a different source or mark a moment you want to include during setup.");
                throw new GenerationSourceException(
                    request.SetupOptions.ClipFulfillmentPreference ==
                        ClipFulfillmentPreference.QualityFirst
                        ? "Replay Foundry found no moments at the selected quality target. Lower the target, choose Fill requested count, or try a different content emphasis."
                        : "Replay Foundry found no safe renderable moments in the selected sources.");
            }

            GenerationCaptionPreparationResult? captions;
            GenerationHiddenMomentDeck hiddenMoments;
            GenerationEditorialMetadataResult editorialMetadata;
            for (int editorialReplacements = 0; ; editorialReplacements++)
            {
                progress.Report(
                    new GenerationProgressUpdate(
                        "Moments selected",
                        moments.FulfillmentMessage,
                        isIndeterminate: false,
                        progressPercent: 50));
                captions = null;
                if (request.SetupOptions.CaptionSettings.IsEnabled)
                {
                    if (_captionPreparation is null)
                    {
                        throw new GenerationEngineUnavailableException(
                            "Spoken captions need the speech-to-text tool from Advanced AI. Install or repair Advanced AI, or turn off spoken captions.");
                    }

                    var captionProgress =
                        new SynchronousProgress<GenerationCaptionPreparationProgress>(
                            update =>
                                progress.Report(
                                    new GenerationProgressUpdate(
                                        update.Title,
                                        update.Detail,
                                        isIndeterminate: false,
                                        progressPercent:
                                            50 + update.Percentage * 0.20)));
                    captions = await _captionPreparation.PrepareAsync(
                        moments,
                        captionProgress,
                        cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                hiddenMoments =
                    GenerationHiddenMomentPlanner.Create(
                        moments,
                        candidateIntelligence,
                        cancellationToken);
                progress.Report(
                    new GenerationProgressUpdate(
                        "Shaping each clip's story",
                        "Writing titles and descriptions from what happens in each moment.",
                        isIndeterminate: true));
                try
                {
                    editorialMetadata = await _editorialMetadata.GenerateAsync(
                        moments,
                        captions,
                        cancellationToken,
                        candidateIntelligence);
                    break;
                }
                catch (ClipEditorialAiGenerationException exception) when (
                    exception.FailureKind == ClipEditorialAiFailureKind.CaseRejected && editorialReplacements < 2)
                {
                    var replacement = GenerationEditorialReplacementPolicy.RejectAutomaticCut(
                        candidateIntelligence, exception.CandidateId, cancellationToken);
                    if (replacement is null) throw;
                    candidateIntelligence = replacement;
                    moments = replacement.RefinedMoments;
                    progress.Report(new GenerationProgressUpdate("Choosing another reviewed moment",
                        "AI could not write reliable copy for one automatic pick. Checking another reviewed cut; the original stays in Find More.",
                        isIndeterminate: true));
                }
            }
            hiddenMoments = await _editorialMetadata.GenerateHiddenAsync(
                hiddenMoments,
                candidateIntelligence,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(
                new GenerationProgressUpdate(
                    "Opening your Studio project",
                    "The selected source windows and caption timing are ready. Studio will render files only after you finish editing.",
                    isIndeterminate: false,
                    progressPercent: 100));
            var result = new GenerationResult(
                request,
                moments,
                editorialMetadata,
                captions,
                candidateIntelligence,
                hiddenMoments);
            string outputDirectory =
                _outputPathProvider.CreateOutputDirectoryPath(moments);
            _outputSink?.Publish(
                GenerationOutputProject.FromResult(
                    result,
                    outputDirectory));
            return result;
        }
        finally
        {
            retainedReviewMedia?.Dispose();
        }
    }
}
