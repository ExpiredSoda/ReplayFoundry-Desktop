using System.Diagnostics;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Captions;

public sealed class GenerationCaptionPreparationService :
    IGenerationCaptionPreparationService
{
    private readonly IAudioSegmentExtractor _audioExtractor;
    private readonly IAudioTranscriptionProvider _transcriptionProvider;
    private readonly AudioTranscriptionOptions _options;
    private readonly AudioTranscriptionModelSettings _model;

    public GenerationCaptionPreparationService(
        IAudioSegmentExtractor audioExtractor,
        IAudioTranscriptionProvider transcriptionProvider,
        AudioTranscriptionOptions options,
        AudioTranscriptionModelSettings model)
    {
        _audioExtractor = audioExtractor ??
            throw new ArgumentNullException(nameof(audioExtractor));
        _transcriptionProvider = transcriptionProvider ??
            throw new ArgumentNullException(nameof(transcriptionProvider));
        _options = options ??
            throw new ArgumentNullException(nameof(options));
        _model = model ??
            throw new ArgumentNullException(nameof(model));
        if (!_options.RequestWordTimestamps)
        {
            throw new ArgumentException(
                "Caption preparation must request word timestamps for timed effects.",
                nameof(options));
        }
    }

    public async Task<GenerationCaptionPreparationResult> PrepareAsync(
        Moments.GenerationMomentFindingResult moments,
        IProgress<GenerationCaptionPreparationProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moments);
        ArgumentNullException.ThrowIfNull(progress);
        if (!moments.Request.Setup.CaptionSettings.IsEnabled)
        {
            throw new ArgumentException(
                "Caption preparation requires enabled caption settings.",
                nameof(moments));
        }

        var stopwatch = Stopwatch.StartNew();
        var tracks = new List<GenerationCandidateCaptionTrack>();
        int total = moments.SelectedCandidates.Count;
        for (int index = 0; index < total; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Moments.GenerationMomentCandidate candidate =
                moments.SelectedCandidates[index];
            GenerationSetup.GenerationCaptionSourceSelection selection =
                moments.Request.Setup.CaptionSettings.FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath) ??
                throw new InvalidOperationException(
                    "The selected moment has no explicit transcription stream.");
            progress.Report(
                new GenerationCaptionPreparationProgress(
                    "Building your captions",
                    $"Transcribing the selected voice track for clip {index + 1} of {total}.",
                    index,
                    total));
            GenerationCandidateCaptionTrack track =
                await PrepareCandidateAsync(
                candidate,
                selection,
                moments.Request.Setup.CaptionSettings.Style,
                cancellationToken);
            tracks.Add(track);
            progress.Report(
                new GenerationCaptionPreparationProgress(
                    "Building your captions",
                    track.IsSuppressed
                        ? $"Clip {index + 1} of {total} returned repetitive " +
                          "low-information text, so Replay Foundry kept the " +
                          "diagnostic transcript but omitted it from captions " +
                          "and metadata."
                        : $"Caption timing is ready for clip {index + 1} of {total}.",
                    index + 1,
                    total));
        }

        stopwatch.Stop();
        return new GenerationCaptionPreparationResult(
            moments,
            tracks,
            stopwatch.Elapsed);
    }

    public async Task<GenerationCandidateCaptionTrack> PrepareCandidateAsync(
        Moments.GenerationMomentCandidate candidate,
        GenerationSetup.GenerationCaptionSourceSelection selection,
        GenerationSetup.GenerationCaptionStylePreset style,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        AudioTranscriptionOptions options = ResolveOptions(selection);
        string neighborhoodId = "caption-" + candidate.Id;
        using ExtractedAudioSegment audio =
            await _audioExtractor.ExtractAsync(
                new AudioSegmentExtractionRequest(
                    neighborhoodId,
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath,
                    candidate.AnalyzedSource.PreparedSource.Media.Duration,
                    selection.AbsoluteAudioStreamIndex,
                    candidate.Candidate.Window.Start,
                    candidate.Candidate.Window.End,
                    options.MaximumProcessDuration),
                cancellationToken);
        AudioTranscriptionResult transcription =
            await _transcriptionProvider.TranscribeAsync(
                new AudioTranscriptionRequest(
                    neighborhoodId,
                    audio.Path,
                    candidate.Candidate.Window.Duration,
                    candidate.Candidate.Window.Start,
                    candidate.AnalyzedSource.PreparedSource.Media.Duration,
                    selection.AbsoluteAudioStreamIndex,
                    options,
                    _model),
                cancellationToken);
        GenerationCaptionSuppressionReason suppressionReason =
            GenerationCaptionTranscriptQuality.Assess(transcription);
        AudioTranscriptionSegment[] renderableSegments =
            GenerationCaptionTranscriptQuality.SelectRenderableSegments(
                transcription);
        return new GenerationCandidateCaptionTrack(
            candidate,
            selection,
            style,
            transcription,
            segments: suppressionReason ==
                GenerationCaptionSuppressionReason.None
                    ? renderableSegments
                    : [],
            suppressionReason: suppressionReason);
    }

    private AudioTranscriptionOptions ResolveOptions(
        GenerationSetup.GenerationCaptionSourceSelection selection) =>
        selection.LanguagePolicy switch
        {
            GenerationSetup.GenerationCaptionLanguagePolicy.Auto =>
                _options.WithLanguage(
                    AudioTranscriptionLanguageMode.Auto,
                    requestedLanguage: null),
            GenerationSetup.GenerationCaptionLanguagePolicy.English =>
                _options.WithLanguage(
                    AudioTranscriptionLanguageMode.Explicit,
                    new AudioTranscriptionLanguage("en", "English")),
            GenerationSetup.GenerationCaptionLanguagePolicy.Spanish =>
                _options.WithLanguage(
                    AudioTranscriptionLanguageMode.Explicit,
                    new AudioTranscriptionLanguage("es", "Spanish")),
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
}
