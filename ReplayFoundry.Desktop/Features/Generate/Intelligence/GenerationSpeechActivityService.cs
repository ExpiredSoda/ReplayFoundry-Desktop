using System.Diagnostics;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public sealed class GenerationSpeechActivityService :
    IGenerationSpeechActivityService,
    IDisposable
{
    private readonly IAudioSegmentExtractor _audioExtractor;
    private readonly ISpeechActivityProvider _provider;
    private readonly GenerationSpeechActivitySettings _settings;
    private bool _disposed;

    public GenerationSpeechActivityService(
        IAudioSegmentExtractor audioExtractor,
        ISpeechActivityProvider provider,
        GenerationSpeechActivitySettings settings)
    {
        _audioExtractor = audioExtractor ??
            throw new ArgumentNullException(nameof(audioExtractor));
        _provider = provider ??
            throw new ArgumentNullException(nameof(provider));
        _settings = settings ??
            throw new ArgumentNullException(nameof(settings));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        (_provider as IDisposable)?.Dispose();
        (_audioExtractor as IDisposable)?.Dispose();
    }

    public async Task<GenerationSpeechActivityResult> AnalyzeAsync(
        GenerationRequest request,
        IProgress<GenerationSpeechActivityProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        if (request.SetupOptions.AnalysisDepth == GenerationAnalysisDepth.Fast)
        {
            throw new ArgumentException(
                "Fast generation must not invoke speech activity analysis.",
                nameof(request));
        }

        var stopwatch = Stopwatch.StartNew();
        var sourceResults = new List<GenerationSourceSpeechActivity>(
            request.SourceCount);
        int totalStreams = request.AnalyzedSources.Sum(
            static source => source.PreparedSource.Media.AudioStreams.Count);
        int completedStreams = 0;

        for (int sourceIndex = 0; sourceIndex < request.AnalyzedSources.Count; sourceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnalyzedGenerationSource source = request.AnalyzedSources[sourceIndex];
            string sourceName = Path.GetFileName(source.PreparedSource.Media.FullPath);
            var streams = new List<GenerationSpeechStreamResult>();
            foreach (var audioStream in source.PreparedSource.Media.AudioStreams.OrderBy(static stream => stream.Index))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AudioContentRoleAssignment role = ResolveRole(
                    request.SetupOptions.CaptionSettings.FindForSource(
                        source.PreparedSource.Media.FullPath),
                    audioStream.Index);
                try
                {
                    var detected = new List<SpeechActivityInterval>();
                    var manifests = new List<SpeechActivityExecutionManifest>();
                    int chunkIndex = 0;
                    for (TimeSpan chunkStart = TimeSpan.Zero;
                         chunkStart < source.PreparedSource.Media.Duration;
                         chunkStart += _settings.MaximumAudioChunkDuration)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TimeSpan chunkEnd = chunkStart + _settings.MaximumAudioChunkDuration;
                        if (chunkEnd > source.PreparedSource.Media.Duration)
                        {
                            chunkEnd = source.PreparedSource.Media.Duration;
                        }
                        string chunkId = $"vad-{sourceIndex:D4}-{audioStream.Index:D4}-{chunkIndex:D4}";
                        progress.Report(new GenerationSpeechActivityProgress(
                            GenerationSpeechActivityPhase.PreparingAudio,
                            "Mapping the soundscape",
                            $"Reading a short section from the selected audio track so quiet and spoken moments can be separated.",
                            sourceName,
                            sourceIndex + 1,
                            request.SourceCount,
                            audioStream.Index,
                            isIndeterminate: true,
                            overallPercentage: null));
                        using ExtractedAudioSegment audio = await _audioExtractor.ExtractAsync(
                            new AudioSegmentExtractionRequest(
                                chunkId,
                                source.PreparedSource.Media.FullPath,
                                source.PreparedSource.Media.Duration,
                                audioStream.Index,
                                chunkStart,
                                chunkEnd,
                                _settings.Options.ProcessTimeout),
                            cancellationToken);
                        progress.Report(new GenerationSpeechActivityProgress(
                            GenerationSpeechActivityPhase.DetectingSpeech,
                            "Finding spoken moments",
                            "Tracing where voices begin and end. Words are not being transcribed at this stage.",
                            sourceName,
                            sourceIndex + 1,
                            request.SourceCount,
                            audioStream.Index,
                            isIndeterminate: true,
                            overallPercentage: null));

                        TimeSpan inputDuration = audio.Duration > chunkEnd - chunkStart
                            ? chunkEnd - chunkStart
                            : audio.Duration;
                        SpeechActivityResult result = await _provider.AnalyzeAsync(
                            new SpeechActivityRequest(
                                chunkId,
                                audio.Path,
                                inputDuration,
                                chunkStart,
                                source.PreparedSource.Media.Duration,
                                audioStream.Index,
                                role,
                                _settings.Options,
                                _settings.Model),
                            cancellationToken);
                        detected.AddRange(result.Intervals);
                        manifests.Add(result.Manifest);
                        chunkIndex++;
                    }
                    streams.Add(new GenerationSpeechStreamResult(
                        source,
                        audioStream.Index,
                        role,
                        ConsolidateChunkBoundaries(detected),
                        manifests));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                    when (exception is
                          AudioSegmentExtractionException or
                          SpeechActivityProviderException)
                {
                    string diagnostic = exception switch
                    {
                        AudioSegmentExtractionException extraction => extraction.DiagnosticDetails ?? extraction.Message,
                        SpeechActivityProviderException provider => provider.DiagnosticDetails ?? provider.Message,
                        _ => exception.Message,
                    };
                    throw new GenerationSpeechActivityException(
                        $"Replay Foundry could not analyze speech in source {sourceIndex + 1} of {request.SourceCount} ({sourceName}), audio stream {audioStream.Index}.",
                        source.PreparedSource.Media.FullPath,
                        audioStream.Index,
                        diagnostic,
                        exception);
                }

                completedStreams++;
                progress.Report(new GenerationSpeechActivityProgress(
                    GenerationSpeechActivityPhase.SourceComplete,
                    "Speech timing ready",
                    $"Finished stream {audioStream.Index} for {sourceName}.",
                    sourceName,
                    sourceIndex + 1,
                    request.SourceCount,
                    audioStream.Index,
                    isIndeterminate: false,
                    overallPercentage: totalStreams == 0 ? 100 : completedStreams * 100d / totalStreams));
            }

            sourceResults.Add(new GenerationSourceSpeechActivity(source, streams));
        }

        stopwatch.Stop();
        progress.Report(new GenerationSpeechActivityProgress(
            GenerationSpeechActivityPhase.BatchComplete,
            "Speech analysis complete",
            "Speech timing is ready for deterministic candidate refinement.",
            sourceName: null,
            sourceNumber: null,
            sourceCount: null,
            absoluteAudioStreamIndex: null,
            isIndeterminate: false,
            overallPercentage: 100));
        return new GenerationSpeechActivityResult(
            request,
            _settings,
            _provider.Identity,
            sourceResults,
            stopwatch.Elapsed);
    }

    private static AudioContentRoleAssignment ResolveRole(
        GenerationCaptionSourceSelection? selection,
        int absoluteAudioStreamIndex)
    {
        if (selection is null ||
            selection.AbsoluteAudioStreamIndex != absoluteAudioStreamIndex)
        {
            return AudioContentRoleAssignment.Unknown;
        }

        AudioContentRole role = selection.ContentRole switch
        {
            CaptionAudioContentRole.CreatorCommentary => AudioContentRole.CreatorSpeech,
            CaptionAudioContentRole.GameDialogue => AudioContentRole.GameDialogue,
            CaptionAudioContentRole.MixedSpeech => AudioContentRole.MixedSpeech,
            CaptionAudioContentRole.OtherKnownSpeech => AudioContentRole.MixedSpeech,
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
        return new AudioContentRoleAssignment(
            role,
            AudioContentRoleSource.UserConfirmed);
    }

    private static IEnumerable<SpeechActivityInterval>
        ConsolidateChunkBoundaries(
            IEnumerable<SpeechActivityInterval> intervals)
    {
        SpeechActivityInterval[] ordered = intervals
            .OrderBy(static item => item.AbsoluteStart)
            .ThenBy(static item => item.AbsoluteEnd)
            .ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var consolidated = new List<SpeechActivityInterval>();
        SpeechActivityInterval current = ordered[0];
        for (int index = 1; index < ordered.Length; index++)
        {
            SpeechActivityInterval next = ordered[index];
            if (next.AbsoluteStart <= current.AbsoluteEnd)
            {
                TimeSpan end = next.AbsoluteEnd > current.AbsoluteEnd
                    ? next.AbsoluteEnd
                    : current.AbsoluteEnd;
                TimeSpan start = current.AbsoluteStart;
                current = new SpeechActivityInterval(
                    start,
                    end,
                    start,
                    end,
                    Math.Max(current.PeakProbability, next.PeakProbability),
                    Math.Max(current.MeanProbability, next.MeanProbability));
                continue;
            }

            consolidated.Add(current);
            current = next;
        }
        consolidated.Add(current);
        return consolidated;
    }
}
