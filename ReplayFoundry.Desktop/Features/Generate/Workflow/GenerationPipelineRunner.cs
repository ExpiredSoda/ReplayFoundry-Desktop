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
    private readonly IGenerationOutputPathProvider _outputPathProvider;
    private readonly IGenerationCaptionPreparationService?
        _captionPreparation;
    private readonly IGenerationOutputSink? _outputSink;
    private readonly IGenerationEditorialMetadataService?
        _editorialMetadata;
    private readonly IGenerationSpeechActivityService? _speechActivity;
    private readonly IGenerationCandidateRefinementService? _candidateRefinement;
    private readonly IGenerationVisualSemanticAnalysisService? _visualSemantic;

    public GenerationPipelineRunner(
        GenerationPreflightRunner preflight,
        IGenerationMomentFindingService momentFinder,
        IGenerationOutputPathProvider outputPathProvider,
        IGenerationOutputSink? outputSink = null,
        IGenerationCaptionPreparationService? captionPreparation = null,
        IGenerationEditorialMetadataService? editorialMetadata = null,
        IGenerationSpeechActivityService? speechActivity = null,
        IGenerationCandidateRefinementService? candidateRefinement = null,
        IGenerationVisualSemanticAnalysisService? visualSemantic = null)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(momentFinder);
        ArgumentNullException.ThrowIfNull(outputPathProvider);
        _preflight = preflight;
        _momentFinder = momentFinder;
        _outputPathProvider = outputPathProvider;
        _outputSink = outputSink;
        _captionPreparation = captionPreparation;
        _editorialMetadata = editorialMetadata;
        _speechActivity = speechActivity;
        _candidateRefinement = candidateRefinement;
        _visualSemantic = visualSemantic;
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
                progress.Report(new GenerationProgressUpdate(
                    "Refining candidate order",
                    "Applying transparent speech-timing support according to your Gameplay & Story, Mixed, or Presenter Commentary focus.",
                    isIndeterminate: true));
                candidateIntelligence = await Task.Run(
                    () => _candidateRefinement.Refine(
                        moments,
                        speech,
                        cancellationToken),
                    cancellationToken);
                moments = candidateIntelligence.RefinedMoments;

                if (request.SetupOptions.AnalysisDepth ==
                    GenerationAnalysisDepth.Thorough)
                {
                    if (_visualSemantic is null)
                    {
                        throw new GenerationEngineUnavailableException(
                            "Thorough analysis requires the qualified local visual model. Choose Balanced or configure the approved Qwen runtime explicitly.");
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
                    GenerationVisualSemanticAnalysisResult visual =
                        await _visualSemantic.AnalyzeAsync(
                            candidateIntelligence,
                            visualProgress,
                            cancellationToken);
                    retainedReviewMedia = visual;
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.Report(new GenerationProgressUpdate(
                        "Applying visual observations",
                        "Combining qualified bounded observations with the deterministic score components.",
                        isIndeterminate: true));
                    candidateIntelligence = await Task.Run(
                        () => _candidateRefinement.ApplyVisualSemantic(
                            candidateIntelligence,
                            visual,
                            cancellationToken),
                        cancellationToken);
                    moments = candidateIntelligence.RefinedMoments;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (moments.SelectedCandidates.Count == 0)
            {
                throw new GenerationSourceException(
                    request.SetupOptions.ClipFulfillmentPreference ==
                        ClipFulfillmentPreference.QualityFirst
                        ? "Replay Foundry found no moments at the selected quality target. Lower the target, choose Fill requested count, or try a different content emphasis."
                        : "Replay Foundry found no safe renderable moments in the selected sources.");
            }

            progress.Report(
                new GenerationProgressUpdate(
                    "Moments selected",
                    moments.FulfillmentMessage,
                    isIndeterminate: false,
                    progressPercent: 50));
            GenerationCaptionPreparationResult? captions = null;
            if (request.SetupOptions.CaptionSettings.IsEnabled)
            {
                if (_captionPreparation is null)
                {
                    throw new GenerationEngineUnavailableException(
                        "Captions were requested, but an explicit local whisper.cpp " +
                        "executable and model are not configured. Disable captions " +
                        "or configure the local transcription paths.");
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
            GenerationHiddenMomentDeck hiddenMoments =
                GenerationHiddenMomentPlanner.Create(
                    moments,
                    candidateIntelligence,
                    cancellationToken);
            GenerationEditorialMetadataResult? editorialMetadata = null;
            if (_editorialMetadata is not null)
            {
                progress.Report(
                    new GenerationProgressUpdate(
                        "Shaping each clip's story",
                        "Writing grounded titles and descriptions from the moments Replay Foundry verified.",
                        isIndeterminate: true));
                editorialMetadata =
                    await _editorialMetadata.GenerateAsync(
                        moments,
                        captions,
                        cancellationToken,
                        candidateIntelligence);
                hiddenMoments = await _editorialMetadata.GenerateHiddenAsync(
                    hiddenMoments,
                    candidateIntelligence,
                    cancellationToken);
            }
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
                captions,
                editorialMetadata,
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
