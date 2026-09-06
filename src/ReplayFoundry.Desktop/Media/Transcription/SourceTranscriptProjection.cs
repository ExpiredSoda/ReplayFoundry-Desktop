namespace ReplayFoundry.Desktop.Media.Transcription;

internal static class SourceTranscriptProjection
{
    public static AudioTranscriptionResult Project(string neighborhoodId, TimeSpan sourceDuration,
        int audioStreamIndex, TimeSpan start, TimeSpan end, AudioTranscriptionOptions options,
        IReadOnlyList<AudioTranscriptionResult> chunks)
    {
        var segments = new List<AudioTranscriptionSegment>();
        foreach (AudioTranscriptionSegment segment in chunks.SelectMany(static chunk => chunk.Segments)
                     .Where(segment => segment.AbsoluteSourceEnd > start && segment.AbsoluteSourceStart < end)
                     .OrderBy(static segment => segment.AbsoluteSourceStart))
        {
            bool contained = segment.AbsoluteSourceStart >= start && segment.AbsoluteSourceEnd <= end;
            AudioTranscriptionWord[] words = segment.Words.Where(word =>
                    word.AbsoluteSourceStart >= start && word.AbsoluteSourceEnd <= end &&
                    word.AbsoluteSourceEnd > word.AbsoluteSourceStart)
                .Select(word => new AudioTranscriptionWord(word.Text,
                    word.AbsoluteSourceStart - start, word.AbsoluteSourceEnd - start,
                    word.AbsoluteSourceStart, word.AbsoluteSourceEnd,
                    word.ProviderReportedProbability, word.IsEmphasized)).ToArray();
            if (!contained && words.Length == 0)
            {
                continue; // Never move the full text of a clipped approximate segment inside the cut.
            }
            TimeSpan segmentStart = contained ? segment.AbsoluteSourceStart : words[0].AbsoluteSourceStart;
            TimeSpan segmentEnd = contained ? segment.AbsoluteSourceEnd : words[^1].AbsoluteSourceEnd;
            if (segments.Count > 0 && segmentStart < segments[^1].AbsoluteSourceEnd)
            {
                continue;
            }
            segments.Add(new AudioTranscriptionSegment(
                TranscriptionStableId.Create("projected", neighborhoodId, segment.NeighborhoodId, segment.Id), neighborhoodId,
                contained ? segment.Text : string.Join(" ", words.Select(static word => word.Text)),
                segmentStart - start, segmentEnd - start, segmentStart, segmentEnd,
                words, segment.ProviderReportedConfidence, segment.Language, segment.Warnings, segment.Speaker,
                contained ? segment.SecondaryText : null));
        }
        var manifest = new AudioTranscriptionManifest(neighborhoodId, end - start, start,
            sourceDuration, audioStreamIndex, options, chunks[0].Manifest.Execution,
            chunks.Select(static chunk => chunk.Manifest));
        return new AudioTranscriptionResult(neighborhoodId, audioStreamIndex, segments, manifest,
            chunks.Select(static chunk => chunk.DetectedLanguage).FirstOrDefault(static language => language is not null),
            chunks.SelectMany(static chunk => chunk.Warnings).Distinct());
    }
}
