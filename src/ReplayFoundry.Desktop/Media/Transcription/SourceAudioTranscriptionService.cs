using System.Globalization;
using System.IO;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Transcription;

public interface ISourceAudioTranscriptionService
{
    Task<AudioTranscriptionResult> TranscribeWindowAsync(string neighborhoodId,
        MediaProbeResult source, int audioStreamIndex, TimeSpan start, TimeSpan end,
        AudioTranscriptionOptions options, CancellationToken cancellationToken);

    Task<AudioTranscriptionResult> TranscribeSourceChunkAsync(string neighborhoodId,
        MediaProbeResult source, int audioStreamIndex, TimeSpan start, TimeSpan end,
        AudioTranscriptionOptions options, CancellationToken cancellationToken) =>
        TranscribeWindowAsync(neighborhoodId, source, audioStreamIndex, start, end, options, cancellationToken);
}

public sealed record SourceTranscriptDiagnostic(string Stage, TimeSpan Start, TimeSpan End,
    bool CacheHit, int SegmentCount, int WordCount, string? Language,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> SegmentTimings);

public sealed class SourceAudioTranscriptionService : ISourceAudioTranscriptionService
{
    public static readonly TimeSpan ChunkDuration = TimeSpan.FromMinutes(2);
    private const int MaximumCachedChunks = 128;
    private readonly IAudioSegmentExtractor _extractor;
    private readonly IAudioTranscriptionProvider _provider;
    private readonly AudioTranscriptionModelSettings _model;
    private readonly Action<SourceTranscriptDiagnostic>? _diagnostics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, AudioTranscriptionResult> _chunks = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recency = new();

    public SourceAudioTranscriptionService(IAudioSegmentExtractor extractor,
        IAudioTranscriptionProvider provider, AudioTranscriptionModelSettings model,
        Action<SourceTranscriptDiagnostic>? diagnostics = null)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _diagnostics = diagnostics;
    }

    public Task<AudioTranscriptionResult> TranscribeWindowAsync(string neighborhoodId,
        MediaProbeResult source, int audioStreamIndex, TimeSpan start, TimeSpan end,
        AudioTranscriptionOptions options, CancellationToken cancellationToken) =>
        TranscribeAsync(neighborhoodId, source, audioStreamIndex, start, end, options, false, cancellationToken);

    public Task<AudioTranscriptionResult> TranscribeSourceChunkAsync(string neighborhoodId,
        MediaProbeResult source, int audioStreamIndex, TimeSpan start, TimeSpan end,
        AudioTranscriptionOptions options, CancellationToken cancellationToken) =>
        TranscribeAsync(neighborhoodId, source, audioStreamIndex, start, end, options, true, cancellationToken);

    private async Task<AudioTranscriptionResult> TranscribeAsync(string neighborhoodId,
        MediaProbeResult source, int audioStreamIndex, TimeSpan start, TimeSpan end,
        AudioTranscriptionOptions options, bool populateSourceChunks, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(neighborhoodId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (start < TimeSpan.Zero || end <= start || end > source.Duration ||
            !source.AudioStreams.Any(stream => stream.Index == audioStreamIndex))
        {
            throw new ArgumentException("Source transcription requires a bounded inspected audio stream.");
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string identity = Identity(source.FullPath);
            string modelIdentity = Identity(_model.ModelPath);
            var results = new List<AudioTranscriptionResult>();
            long firstChunk = start.Ticks / ChunkDuration.Ticks;
            long lastChunk = (end.Ticks - 1) / ChunkDuration.Ticks;
            string ChunkKey(long index) => TranscriptionStableId.Create("source-transcript-v1", identity,
                modelIdentity, source.Duration.Ticks.ToString(CultureInfo.InvariantCulture),
                audioStreamIndex.ToString(CultureInfo.InvariantCulture),
                index.ToString(CultureInfo.InvariantCulture), OptionsIdentity(options));
            string exactKey = TranscriptionStableId.Create("source-transcript-cut-v1", identity, modelIdentity,
                source.Duration.Ticks.ToString(CultureInfo.InvariantCulture),
                audioStreamIndex.ToString(CultureInfo.InvariantCulture), start.Ticks.ToString(CultureInfo.InvariantCulture),
                end.Ticks.ToString(CultureInfo.InvariantCulture), OptionsIdentity(options));
            bool completeCachedCoverage = true;
            for (long index = firstChunk; index <= lastChunk; index++)
                completeCachedCoverage &= _chunks.ContainsKey(ChunkKey(index));
            if (!populateSourceChunks && (_chunks.ContainsKey(exactKey) || !completeCachedCoverage))
            {
                // Caption-only requests pay only for their cut. Source-wide
                // discovery is the only caller that creates the larger chunks.
                return await ReadExactAsync("exact-window");
            }
            for (long index = firstChunk; index <= lastChunk; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan chunkStart = TimeSpan.FromTicks(index * ChunkDuration.Ticks);
                TimeSpan chunkEnd = TimeSpan.FromTicks(Math.Min(source.Duration.Ticks,
                    chunkStart.Ticks + ChunkDuration.Ticks));
                string key = ChunkKey(index);
                bool cacheHit = _chunks.TryGetValue(key, out AudioTranscriptionResult? result);
                if (!cacheHit)
                {
                    using ExtractedAudioSegment audio = await _extractor.ExtractAsync(
                        new AudioSegmentExtractionRequest(key, source.FullPath, source.Duration,
                            audioStreamIndex, chunkStart, chunkEnd, options.MaximumProcessDuration), cancellationToken);
                    result = await _provider.TranscribeAsync(new AudioTranscriptionRequest(
                        key, audio.Path, chunkEnd - chunkStart, chunkStart, source.Duration,
                        audioStreamIndex, options, _model), cancellationToken);
                    if (identity != Identity(source.FullPath) || modelIdentity != Identity(_model.ModelPath))
                    {
                        throw new IOException("The recording or transcription model changed while its transcript was being prepared.");
                    }
                    _chunks.Add(key, result);
                }
                Report("source-chunk", chunkStart, chunkEnd, cacheHit || result!.WasReused, result!);
                _recency.Remove(key);
                _recency.AddLast(key);
                while (_recency.Count > MaximumCachedChunks)
                {
                    string oldest = _recency.First!.Value;
                    _recency.RemoveFirst();
                    _chunks.Remove(oldest);
                }
                results.Add(result!);
            }
            cancellationToken.ThrowIfCancellationRequested();
            AudioTranscriptionResult projected = SourceTranscriptProjection.Project(neighborhoodId, source.Duration,
                audioStreamIndex, start, end, options, results);
            Report("cut-projection", start, end, true, projected);
            bool omittedBoundarySpeech = results.SelectMany(static result => result.Segments).Any(segment =>
                segment.AbsoluteSourceStart < end && segment.AbsoluteSourceEnd > start &&
                (segment.AbsoluteSourceStart < start || segment.AbsoluteSourceEnd > end) &&
                !segment.Words.Any(word => word.AbsoluteSourceStart >= start && word.AbsoluteSourceEnd <= end));
            if (omittedBoundarySpeech)
            {
                // Some providers return approximate sentence timings without
                // words. Re-read this precise cut instead of silently losing
                // its speech or moving the entire sentence inside the cut.
                return await ReadExactAsync("exact-window-recovery");
            }
            return projected;

            async Task<AudioTranscriptionResult> ReadExactAsync(string stage)
            {
                bool cached = _chunks.TryGetValue(exactKey, out AudioTranscriptionResult? exact);
                if (!cached)
                {
                    using ExtractedAudioSegment audio = await _extractor.ExtractAsync(new AudioSegmentExtractionRequest(
                        exactKey, source.FullPath, source.Duration, audioStreamIndex, start, end, options.MaximumProcessDuration), cancellationToken);
                    exact = await _provider.TranscribeAsync(new AudioTranscriptionRequest(exactKey, audio.Path, end - start,
                        start, source.Duration, audioStreamIndex, options, _model), cancellationToken);
                    if (identity != Identity(source.FullPath) || modelIdentity != Identity(_model.ModelPath))
                        throw new IOException("The recording or transcription model changed while its transcript was being prepared.");
                    _chunks.Add(exactKey, exact);
                }
                _recency.Remove(exactKey);
                _recency.AddLast(exactKey);
                if (_recency.Count > MaximumCachedChunks)
                {
                    string oldest = _recency.First!.Value;
                    _recency.RemoveFirst();
                    _chunks.Remove(oldest);
                }
                Report(stage, start, end, cached || exact!.WasReused, exact!);
                return SourceTranscriptProjection.Project(neighborhoodId, source.Duration, audioStreamIndex,
                    start, end, options, [exact!]);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Identity(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The source recording or transcription model is unavailable.", path);
        }
        return string.Join("|", info.FullName.ToUpperInvariant(),
            info.Length.ToString(CultureInfo.InvariantCulture),
            info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    private void Report(string stage, TimeSpan start, TimeSpan end, bool cached, AudioTranscriptionResult result) =>
        _diagnostics?.Invoke(new(stage, start, end, cached, result.Segments.Count,
            result.Segments.Sum(static segment => segment.Words.Count), result.DetectedLanguage?.Code,
            result.Warnings.Select(static warning => warning.Code.ToString()).ToArray(),
            result.Segments.Take(12).Select(static segment =>
                $"{segment.AbsoluteSourceStart:c}-{segment.AbsoluteSourceEnd:c}:words={segment.Words.Count}:characters={segment.Text.Length}").ToArray()));

    private static string OptionsIdentity(AudioTranscriptionOptions options) =>
        string.Join("|", options.PolicyVersion, options.LanguageMode,
            options.RequestedLanguage?.Code, options.TranslateToEnglish,
            options.RequestWordTimestamps, options.RequireSegmentTimestamps,
            options.Temperature?.ToString("R", CultureInfo.InvariantCulture),
            options.ProcessorHint, options.ThreadCount, options.OutputFormatPolicy,
            options.InitialPrompt);
}
