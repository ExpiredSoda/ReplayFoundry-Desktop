using System.Diagnostics;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Captions;

public sealed class GenerationCaptionPreparationService :
    IGenerationCaptionPreparationService
{
    private readonly IAudioSegmentExtractor _audioExtractor;
    private readonly IAudioTranscriptionProvider _transcriptionProvider;
    private readonly AudioTranscriptionOptions _options;
    private readonly AudioTranscriptionModelSettings _model;
    private readonly ISourceAudioTranscriptionService? _sourceTranscription;

    public GenerationCaptionPreparationService(
        IAudioSegmentExtractor audioExtractor,
        IAudioTranscriptionProvider transcriptionProvider,
        AudioTranscriptionOptions options,
        AudioTranscriptionModelSettings model,
        ISourceAudioTranscriptionService? sourceTranscription = null)
    {
        _audioExtractor = audioExtractor ??
            throw new ArgumentNullException(nameof(audioExtractor));
        _transcriptionProvider = transcriptionProvider ??
            throw new ArgumentNullException(nameof(transcriptionProvider));
        _options = options ??
            throw new ArgumentNullException(nameof(options));
        _model = model ??
            throw new ArgumentNullException(nameof(model));
        _sourceTranscription = sourceTranscription;
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
                        ? $"Clip {index + 1} of {total} did not contain " +
                          "enough clear speech, so Replay Foundry left out " +
                          "its spoken captions and did not use those words " +
                          "in the title or description."
                        : track.HasRenderableSegments
                            ? $"Caption timing is ready for clip {index + 1} of {total}."
                            : $"No timed speech was returned for clip {index + 1} of {total}. Review the selected voice track or regenerate captions in Studio.",
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
        PreparedCandidateCaption prepared = await PrepareCandidateCoreAsync(
            candidate.Id,
            candidate.AnalyzedSource.PreparedSource.Media,
            candidate.Candidate.Window.Start,
            candidate.Candidate.Window.End,
            selection,
            cancellationToken);
        return new GenerationCandidateCaptionTrack(
            candidate,
            selection,
            style,
            prepared.Transcription,
            segments: prepared.SuppressionReason ==
                GenerationCaptionSuppressionReason.None
                    ? prepared.RenderableSegments
                    : [],
            suppressionReason: prepared.SuppressionReason);
    }

    public async Task<GenerationCandidateCaptionTrack>
        PrepareRetainedCandidateAsync(
            string candidateId,
            MediaProbeResult sourceMedia,
            TimeSpan sourceStart,
            TimeSpan sourceEnd,
            GenerationSetup.GenerationCaptionSourceSelection selection,
            GenerationSetup.GenerationCaptionStylePreset style,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentNullException.ThrowIfNull(sourceMedia);
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(style) ||
            sourceStart < TimeSpan.Zero ||
            sourceEnd <= sourceStart ||
            sourceEnd > sourceMedia.Duration ||
            !sourceMedia.FullPath.Equals(
                selection.SourceFullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Retained caption preparation requires one bounded source window, stream, and style.");
        }

        PreparedCandidateCaption prepared = await PrepareCandidateCoreAsync(
            candidateId.Trim(),
            sourceMedia,
            sourceStart,
            sourceEnd,
            selection,
            cancellationToken);
        return GenerationCandidateCaptionTrack.RestoreStudioHandoff(
            candidateId.Trim(),
            prepared.Transcription.NeighborhoodId,
            selection,
            style,
            sourceStart,
            sourceEnd - sourceStart,
            sourceMedia.Duration,
            prepared.SuppressionReason ==
                GenerationCaptionSuppressionReason.None
                    ? prepared.RenderableSegments
                    : [],
            isUserEdited: false,
            suppressionReason: prepared.SuppressionReason);
    }

    private async Task<PreparedCandidateCaption> PrepareCandidateCoreAsync(
        string candidateId,
        MediaProbeResult sourceMedia,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        GenerationSetup.GenerationCaptionSourceSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioTranscriptionOptions options = ResolveOptions(selection);
        string neighborhoodId = "caption-" + candidateId;
        if (_sourceTranscription is not null)
        {
            AudioTranscriptionResult cached = await _sourceTranscription.TranscribeWindowAsync(
                neighborhoodId, sourceMedia, selection.AbsoluteAudioStreamIndex,
                sourceStart, sourceEnd, options, cancellationToken);
            return new PreparedCandidateCaption(cached,
                GenerationCaptionTranscriptQuality.SelectRenderableSegments(cached),
                GenerationCaptionTranscriptQuality.Assess(cached));
        }
        using ExtractedAudioSegment audio =
            await _audioExtractor.ExtractAsync(
                new AudioSegmentExtractionRequest(
                    neighborhoodId,
                    sourceMedia.FullPath,
                    sourceMedia.Duration,
                    selection.AbsoluteAudioStreamIndex,
                    sourceStart,
                    sourceEnd,
                    options.MaximumProcessDuration),
                cancellationToken);
        AudioTranscriptionResult transcription =
            await _transcriptionProvider.TranscribeAsync(
                new AudioTranscriptionRequest(
                    neighborhoodId,
                    audio.Path,
                    sourceEnd - sourceStart,
                    sourceStart,
                    sourceMedia.Duration,
                    selection.AbsoluteAudioStreamIndex,
                    options,
                    _model),
                cancellationToken);
        GenerationCaptionSuppressionReason suppressionReason =
            GenerationCaptionTranscriptQuality.Assess(transcription);
        AudioTranscriptionSegment[] renderableSegments =
            GenerationCaptionTranscriptQuality.SelectRenderableSegments(
                transcription);
        return new PreparedCandidateCaption(
            transcription,
            renderableSegments,
            suppressionReason);
    }

    internal AudioTranscriptionOptions ResolveOptions(
        GenerationSetup.GenerationCaptionSourceSelection selection)
    {
        AudioTranscriptionOptions options = GenerationSetup.GenerationCaptionLanguageCatalog.ResolveOptions(
            _options, selection.LanguagePolicy, _model.LanguageCapabilities);
        return options.WithInitialPrompt(new Platform.Storage.JsonCaptionVocabularyStore().ReadPrompt());
    }

    private sealed record PreparedCandidateCaption(
        AudioTranscriptionResult Transcription,
        AudioTranscriptionSegment[] RenderableSegments,
        GenerationCaptionSuppressionReason SuppressionReason);
}
