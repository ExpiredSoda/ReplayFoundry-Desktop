using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal sealed record StudioCaptionCutProjectionResult(GenerationCandidateCaptionTrack Track,
    IReadOnlyList<string> Warnings);

/// <summary>Presentation-only projection. The retained editable transcript is never changed.</summary>
internal static class StudioCaptionCutProjection
{
    internal const string ApproximateBoundaryWarning = "This cut crosses a phrase without complete measured word timing. " +
        "Its whole text remains visible; review the boundary or regenerate captions for this cut.";

    public static StudioCaptionCutProjectionResult Project(GenerationCandidateCaptionTrack track,
        TimeSpan start, TimeSpan end)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (start < TimeSpan.Zero || end <= start || end > track.SourceDuration)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (track.Segments.All(segment => segment.AbsoluteSourceStart >= start && segment.AbsoluteSourceEnd <= end))
            return new(track, []);
        var segments = new List<AudioTranscriptionSegment>();
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in track.Segments.Where(segment => segment.AbsoluteSourceEnd > start && segment.AbsoluteSourceStart < end))
        {
            if (segment.AbsoluteSourceStart >= start && segment.AbsoluteSourceEnd <= end)
            {
                segments.Add(segment);
                continue;
            }
            bool aligned = StudioCaptionPresentationPolicy.HasCompleteTimedWordCoverage(segment);
            var words = aligned ? segment.Words.Where(word => word.AbsoluteSourceStart >= start &&
                word.AbsoluteSourceEnd <= end).ToArray() : [];
            if (aligned && words.Length == 0) continue;
            TimeSpan projectedStart = aligned ? words[0].AbsoluteSourceStart : Max(start, segment.AbsoluteSourceStart);
            TimeSpan projectedEnd = aligned ? words[^1].AbsoluteSourceEnd : Min(end, segment.AbsoluteSourceEnd);
            if (!aligned) warnings.Add(ApproximateBoundaryWarning);
            if (aligned && segment.SecondaryText is not null)
                warnings.Add("A translated phrase crosses this cut and has no independent word timing. Review or regenerate its translation for the current cut.");
            string text = segment.Text;
            if (aligned)
            {
                var cue = new StudioCaptionCue(segment.Text, segment.RelativeStart, segment.RelativeEnd,
                    segment.AbsoluteSourceStart, segment.AbsoluteSourceEnd,
                    StudioCaptionPresentationPolicy.CreateRequiredWordSpans(segment.Text, segment.Words));
                int firstWord = segment.Words.TakeWhile(word => !ReferenceEquals(word, words[0])).Count();
                text = StudioCaptionDisplayText.SliceWordRangeText(cue, firstWord, words.Length);
            }
            segments.Add(new AudioTranscriptionSegment(segment.Id, segment.NeighborhoodId,
                text,
                projectedStart - track.SourceWindowStart, projectedEnd - track.SourceWindowStart,
                projectedStart, projectedEnd, words, segment.ProviderReportedConfidence, segment.Language,
                segment.Warnings, segment.Speaker, aligned ? null : segment.SecondaryText));
        }
        return new(GenerationCandidateCaptionTrack.RestoreStudioHandoff(track.CandidateId, track.NeighborhoodId,
            track.SourceSelection, track.RequestedStyle, track.SourceWindowStart, track.SourceWindowDuration,
            track.SourceDuration, segments, track.IsUserEdited, track.SuppressionReason), warnings.ToArray());
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}
