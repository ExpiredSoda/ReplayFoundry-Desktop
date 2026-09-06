using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationVisualTranscriptContextBuilder
{
    public static VisualSemanticTranscriptContext Build(GenerationSourceTranscript? source,
        TimeSpan start, TimeSpan end)
    {
        var spans = new List<VisualSemanticTranscriptSpan>();
        int remainingCharacters = 8000;
        foreach (AudioTranscriptionSegment segment in source?.Segments ?? [])
        {
            if (segment.AbsoluteSourceEnd <= start || segment.AbsoluteSourceStart >= end ||
                spans.Count >= 32 || remainingCharacters <= 0)
            {
                continue;
            }
            bool contained = segment.AbsoluteSourceStart >= start && segment.AbsoluteSourceEnd <= end;
            AudioTranscriptionWord[] words = segment.Words.Where(word =>
                word.AbsoluteSourceStart >= start && word.AbsoluteSourceEnd <= end).ToArray();
            if (!contained && words.Length == 0)
            {
                continue;
            }
            string text = contained ? segment.Text : string.Join(" ", words.Select(static word => word.Text));
            int limit = Math.Min(1000, remainingCharacters);
            if (text.Length > limit)
            {
                continue; // Do not detach truncated text from its actual timing.
            }
            spans.Add(new VisualSemanticTranscriptSpan(segment.Id, text,
                (contained ? segment.AbsoluteSourceStart : words[0].AbsoluteSourceStart) - start,
                (contained ? segment.AbsoluteSourceEnd : words[^1].AbsoluteSourceEnd) - start,
                isNonSpeech: false, TranscriptTimingPrecision.SegmentApproximate));
            remainingCharacters -= text.Length;
        }
        return spans.Count == 0
            ? new(VisualSemanticTranscriptContextPolicy.VisualOnlyV1, null, [],
                "No reliable timed transcript is available inside this review; judge only the sampled pictures.")
            : new(VisualSemanticTranscriptContextPolicy.FullContextV1, TranscriptEvidenceStatus.LexicalText, spans,
                "Local speech recognition may contain mistakes. Timings are approximate; spoken claims are context, not proof of unseen game events or speaker identity.");
    }
}
