using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Platform.Transcription;

internal static class WhisperCppSegmentSequenceNormalizer
{
    public static AudioTranscriptionSegment[] Normalize(IReadOnlyList<AudioTranscriptionSegment> source,
        ICollection<AudioTranscriptionWarning> warnings)
    {
        AudioTranscriptionSegment[] ordered = source.OrderBy(static segment => segment.RelativeStart)
            .ThenBy(static segment => segment.RelativeEnd).ToArray();
        if (!source.SequenceEqual(ordered)) warnings.Add(new(AudioTranscriptionWarningCode.SegmentTimingCanonicalized,
            "The provider's segments were sorted by their reported source timestamps. No timestamps were shifted."));
        var result = new List<AudioTranscriptionSegment>();
        foreach (AudioTranscriptionSegment segment in ordered)
        {
            if (result.Count == 0 || segment.RelativeStart >= result[^1].RelativeEnd)
            {
                result.Add(segment); continue;
            }
            AudioTranscriptionSegment previous = result[^1];
            if (previous.Text == segment.Text && previous.RelativeStart == segment.RelativeStart && previous.RelativeEnd == segment.RelativeEnd)
            {
                warnings.Add(new(AudioTranscriptionWarningCode.SegmentTimingCanonicalized,
                    "An identical duplicate provider segment at the same reported interval was kept once.", segment.Id));
                continue;
            }
            // Segment envelopes occasionally overlap at a whisper.cpp split boundary
            // even while their word clocks remain valid. Join only that connected
            // overlap group, retaining text and the exact observed time envelope.
            // Conflicting word clocks cannot truthfully drive karaoke; that local
            // group falls back to its reported phrase timing instead.
            AudioTranscriptionWord[] words = previous.Words.Concat(segment.Words).ToArray();
            bool wordsUsable = previous.Words.Count > 0 && segment.Words.Count > 0 &&
                !words.Zip(words.Skip(1), static (left, right) => right.RelativeStart < left.RelativeEnd).Any(static overlap => overlap);
            string id = TranscriptionStableId.Create("joined-provider-overlap", previous.Id, segment.Id);
            var warning = new AudioTranscriptionWarning(AudioTranscriptionWarningCode.SegmentTimingCanonicalized,
                wordsUsable ? "Overlapping provider phrase envelopes were joined; every reported word timestamp was preserved." :
                    "Overlapping provider phrases were joined. Conflicting or incomplete word timing uses phrase timing for this group; review its alignment.", id);
            warnings.Add(warning);
            TimeSpan end = previous.RelativeEnd >= segment.RelativeEnd ? previous.RelativeEnd : segment.RelativeEnd;
            TimeSpan offset = previous.AbsoluteSourceStart - previous.RelativeStart;
            result[^1] = new AudioTranscriptionSegment(id, previous.NeighborhoodId,
                previous.Text.TrimEnd() + " " + segment.Text.TrimStart(), previous.RelativeStart, end,
                previous.AbsoluteSourceStart, offset + end, wordsUsable ? words : [],
                providerReportedConfidence: null, previous.Language == segment.Language ? previous.Language : null,
                previous.Warnings.Concat(segment.Warnings).Append(warning));
        }
        return result.ToArray();
    }
}
