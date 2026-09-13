using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Media.Intelligence;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public interface IGenerationTranscriptAnalysisService
{
    Task<IReadOnlyList<GenerationSourceTranscript>> ReadWindowAsync(ReplayFoundry.Desktop.Media.Inspection.MediaProbeResult source,
        TimeSpan start, TimeSpan end, CancellationToken cancellationToken,
        IReadOnlyList<int>? preferredStreams = null) => Task.FromResult<IReadOnlyList<GenerationSourceTranscript>>([]);
    Task<GenerationTranscriptAnalysisResult> AnalyzeAsync(GenerationMomentFindingResult moments,
        GenerationSpeechActivityResult speech, IProgress<string>? progress,
        CancellationToken cancellationToken);
}

public sealed record GenerationSourceTranscript(string SourceFullPath, int AudioStreamIndex,
    IReadOnlyList<AudioTranscriptionSegment> Segments,
    IReadOnlyList<AudioTranscriptionManifest> Manifests,
    IReadOnlyList<GenerationTranscriptLanguageSpan>? LanguageSpans = null,
    IReadOnlyList<GenerationSourceTranscript>? AdditionalTracks = null)
{
    public IEnumerable<GenerationSourceTranscript> Tracks => new[] { this }.Concat(AdditionalTracks ?? []);
    public IEnumerable<AudioTranscriptionSegment> AllSegments => Tracks.SelectMany(track => track.Segments)
        .OrderBy(segment => segment.AbsoluteSourceStart).ThenBy(segment => segment.AbsoluteSourceEnd);
}

public sealed record GenerationTranscriptAnalysisResult(GenerationMomentFindingResult ExpandedMoments,
    IReadOnlyList<GenerationSourceTranscript> Sources,
    GenerationSemanticRetrievalResult? SemanticRetrieval = null);

public sealed class GenerationTranscriptAnalysisService : IGenerationTranscriptAnalysisService
{
    private readonly ISourceAudioTranscriptionService _transcription;
    private readonly AudioTranscriptionOptions _defaultOptions;
    private readonly Func<GenerationCaptionSourceSelection, AudioTranscriptionOptions> _options;
    private readonly ISemanticTextEmbeddingService? _semanticEmbeddings;

    public GenerationTranscriptAnalysisService(ISourceAudioTranscriptionService transcription,
        AudioTranscriptionOptions defaultOptions,
        Func<GenerationCaptionSourceSelection, AudioTranscriptionOptions> options,
        ISemanticTextEmbeddingService? semanticEmbeddings = null)
    {
        _transcription = transcription ?? throw new ArgumentNullException(nameof(transcription));
        _defaultOptions = defaultOptions ?? throw new ArgumentNullException(nameof(defaultOptions));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _semanticEmbeddings = semanticEmbeddings;
    }

    public async Task<GenerationTranscriptAnalysisResult> AnalyzeAsync(GenerationMomentFindingResult moments,
        GenerationSpeechActivityResult speech, IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using IDisposable priority = MediaWorkBudget.WithPriority(MediaWorkPriority.Background);
        var sources = new List<GenerationSourceTranscript>();
        foreach (GenerationSourceMomentResult source in moments.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var media = source.AnalyzedSource.PreparedSource.Media;
            GenerationCaptionSourceSelection? selection = moments.Request.Setup.CaptionSettings.FindForSource(media.FullPath);
            var streams = speech.Sources.Single(value =>
                    ReferenceEquals(value.Source, source.AnalyzedSource)).Streams
                .Where(value => value.Intervals.Count > 0)
                .OrderByDescending(value => value.AbsoluteAudioStreamIndex == selection?.AbsoluteAudioStreamIndex)
                .ThenByDescending(static value => value.Intervals.Sum(interval =>
                    (interval.AbsoluteEnd - interval.AbsoluteStart).TotalSeconds))
                .ThenBy(static value => value.AbsoluteAudioStreamIndex)
                .ToArray();
            if (streams.Length == 0)
            {
                continue;
            }
            if (streams.Length > 4)
                progress?.Report("Reading up to four speech tracks; additional tracks retain unknown spoken context.");
            var tracks = new List<GenerationSourceTranscript>();
            foreach (var stream in streams.Take(4))
            {
                // A caption language choice belongs only to its selected stream.
                AudioTranscriptionOptions options = selection is null || selection.AbsoluteAudioStreamIndex != stream.AbsoluteAudioStreamIndex
                    ? _defaultOptions : _options(selection);
                var segments = new List<AudioTranscriptionSegment>();
                var manifests = new List<AudioTranscriptionManifest>();
                var languages = new List<GenerationTranscriptLanguageSpan>();
                long chunkTicks = SourceAudioTranscriptionService.ChunkDuration.Ticks;
                long chunkCount = (media.Duration.Ticks + chunkTicks - 1) / chunkTicks;
                for (long chunk = 0; chunk < chunkCount; chunk++)
                {
                    TimeSpan start = TimeSpan.FromTicks(chunk * chunkTicks);
                    TimeSpan end = TimeSpan.FromTicks(Math.Min(media.Duration.Ticks, start.Ticks + chunkTicks));
                    if (!stream.Intervals.Any(interval => interval.AbsoluteStart < end && interval.AbsoluteEnd > start))
                    {
                        continue;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report($"Reading speech track {stream.AbsoluteAudioStreamIndex} in {System.IO.Path.GetFileName(media.FullPath)}, " +
                        $"section {chunk + 1} of {chunkCount}.");
                    AudioTranscriptionResult transcript = await _transcription.TranscribeSourceChunkAsync(
                        MomentStableId.Create("discovery", media.FullPath, stream.AbsoluteAudioStreamIndex, chunk),
                        media, stream.AbsoluteAudioStreamIndex, start, end, options, cancellationToken);
                    if (GenerationCaptionTranscriptQuality.Assess(transcript) == GenerationCaptionSuppressionReason.None)
                    {
                        segments.AddRange(GenerationCaptionTranscriptQuality.SelectRenderableSegments(transcript));
                        manifests.AddRange(transcript.Manifest.SourceManifests.Count > 0
                            ? transcript.Manifest.SourceManifests : [transcript.Manifest]);
                        languages.Add(new(start, end, transcript.DetectedLanguage?.Code,
                            options.TranslateToEnglish, options.RequestedLanguage?.Code));
                    }
                }
                tracks.Add(new(media.FullPath, stream.AbsoluteAudioStreamIndex,
                    Array.AsReadOnly(segments.ToArray()), Array.AsReadOnly(manifests.ToArray()),
                    Array.AsReadOnly(languages.ToArray())));
            }
            sources.Add(tracks[0] with { AdditionalTracks = Array.AsReadOnly(tracks.Skip(1).ToArray()) });
        }
        GenerationSemanticRetrievalResult? retrieval = null;
        if (moments.Request.Setup.DiscoveryIntent.UsesSemanticRetrieval)
        {
            if (_semanticEmbeddings is null)
                throw new InvalidOperationException("Semantic search is unavailable in this generation service.");
            retrieval = await GenerationSemanticRetrieval.SearchAsync(moments, sources, _semanticEmbeddings, progress, cancellationToken);
            progress?.Report("Spoken matches are ready for video review. Similar wording can also appear in jokes, denials, or hypothetical examples.");
        }
        GenerationMomentFindingResult expanded = GenerationTranscriptCandidatePlanner.Expand(
            moments, sources, cancellationToken, retrieval);
        return new(expanded, Array.AsReadOnly(sources.ToArray()), retrieval);
    }

    public async Task<IReadOnlyList<GenerationSourceTranscript>> ReadWindowAsync(ReplayFoundry.Desktop.Media.Inspection.MediaProbeResult source,
        TimeSpan start, TimeSpan end, CancellationToken cancellationToken, IReadOnlyList<int>? preferredStreams = null)
    {
        var tracks = new List<GenerationSourceTranscript>();
        foreach (var stream in source.AudioStreams.OrderByDescending(stream => preferredStreams?.Contains(stream.Index) == true).Take(4))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _transcription.TranscribeWindowAsync(MomentStableId.Create("scene-speech", source.FullPath,
                stream.Index, start, end), source, stream.Index, start, end, _defaultOptions, cancellationToken);
            tracks.Add(new(source.FullPath, stream.Index,
                GenerationCaptionTranscriptQuality.Assess(result) == GenerationCaptionSuppressionReason.None
                    ? GenerationCaptionTranscriptQuality.SelectRenderableSegments(result).ToArray() : [], [result.Manifest]));
        }
        return tracks.AsReadOnly();
    }
}
