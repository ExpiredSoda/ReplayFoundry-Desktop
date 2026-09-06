using System.Text;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioCaptionWordEdit(string Text, double StartSeconds, double EndSeconds, bool IsEmphasized = false,
    double? AcousticScore = null);
public sealed record StudioCaptionSegmentEdit(string Id, string Text, double StartSeconds, double EndSeconds,
    IReadOnlyList<StudioCaptionWordEdit>? Words = null, string? Speaker = null, string? SecondaryText = null,
    string? AlignmentProvenance = null);

internal static class StudioCaptionTrackEditing
{
    public static IReadOnlyList<StudioCaptionSegmentEdit> CreateDrafts(GenerationOutputAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.Captions?.Segments.Select(segment =>
        {
            string? provenance = segment.Warnings.LastOrDefault(warning => warning.Code == AudioTranscriptionWarningCode.CorrectedTextAlignment)?.Message;
            var alignments = StudioCaptionAlignmentProvenance.ReadAll(provenance);
            return new StudioCaptionSegmentEdit(
            segment.Id, segment.Text,
            (segment.AbsoluteSourceStart - asset.SourceStart).TotalSeconds,
            (segment.AbsoluteSourceEnd - asset.SourceStart).TotalSeconds,
            segment.Words.Select(word => new StudioCaptionWordEdit(word.Text,
                (word.AbsoluteSourceStart - asset.SourceStart).TotalSeconds,
                (word.AbsoluteSourceEnd - asset.SourceStart).TotalSeconds, word.IsEmphasized,
                alignments.Select(alignment => alignment.ScoreFor(word.Text, word.AbsoluteSourceStart, word.AbsoluteSourceEnd))
                    .FirstOrDefault(static score => score.HasValue))).ToArray(),
            segment.Speaker, segment.SecondaryText, provenance);
        }).ToArray() ?? [];
    }

    public static GenerationOutputAsset Apply(IGenerationOutputEditor outputEditor, GenerationOutputProject project,
        GenerationOutputAsset asset, IReadOnlyList<StudioCaptionSegmentEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != edits.Count)
            throw new ArgumentException("Each caption phrase needs its own ID.");
        GenerationCandidateCaptionTrack? track = asset.Captions;
        TimeSpan windowStart = track is null || asset.SourceStart < track.SourceWindowStart ? asset.SourceStart : track.SourceWindowStart;
        TimeSpan windowEnd = track is null || asset.SourceEnd > track.SourceWindowStart + track.SourceWindowDuration
            ? asset.SourceEnd : track.SourceWindowStart + track.SourceWindowDuration;
        string neighborhood = track?.NeighborhoodId ?? "caption-manual-" + asset.Id;
        var segments = edits.Select((edit, index) => CreateSegment(asset, track, edit, index, windowStart, windowEnd, neighborhood)).ToArray();
        var replacementTrack = GenerationCandidateCaptionTrack.RestoreStudioHandoff(
            track?.CandidateId ?? asset.Id, neighborhood,
            track?.SourceSelection ?? new GenerationCaptionSourceSelection(asset.SourceFullPath,
                asset.SourceMedia.AudioStreams.FirstOrDefault()?.Index ?? 0, CaptionAudioContentRole.CreatorCommentary),
            track?.RequestedStyle ?? asset.Appearance.CaptionStyle, windowStart, windowEnd - windowStart,
            asset.SourceDuration, segments, true, GenerationCaptionSuppressionReason.None);
        GenerationOutputAsset replacement = asset.WithCaptionTrack(replacementTrack);
        outputEditor.ReplaceAsset(project.Id, replacement);
        return replacement;
    }

    private static AudioTranscriptionSegment CreateSegment(GenerationOutputAsset asset, GenerationCandidateCaptionTrack? track,
        StudioCaptionSegmentEdit edit, int index, TimeSpan windowStart, TimeSpan windowEnd, string neighborhood)
    {
        AudioTranscriptionSegment? original = track?.Segments.FirstOrDefault(s => s.Id == edit.Id);
        if (original is not null && IsUnchangedDraft(original, edit, asset.SourceStart))
        {
            // Partial measured words remain valuable for later cuts even when a
            // phrase cannot animate. Saving another row must retain them exactly,
            // including source ticks, provider evidence and historical alignment.
            if (windowStart == track!.SourceWindowStart && neighborhood == original.NeighborhoodId) return original;
            return new AudioTranscriptionSegment(original.Id, neighborhood, original.Text,
                original.AbsoluteSourceStart - windowStart, original.AbsoluteSourceEnd - windowStart,
                original.AbsoluteSourceStart, original.AbsoluteSourceEnd,
                original.Words.Select(word => new AudioTranscriptionWord(word.Text,
                    word.AbsoluteSourceStart - windowStart, word.AbsoluteSourceEnd - windowStart,
                    word.AbsoluteSourceStart, word.AbsoluteSourceEnd, word.ProviderReportedProbability, word.IsEmphasized)).ToArray(),
                original.ProviderReportedConfidence, original.Language, original.Warnings, original.Speaker, original.SecondaryText);
        }
        if (string.IsNullOrWhiteSpace(edit.Text) || !double.IsFinite(edit.StartSeconds) || !double.IsFinite(edit.EndSeconds))
            throw new ArgumentException($"Caption phrase {index + 1} requires text and finite timing.");
        TimeSpan absoluteStart = asset.SourceStart + TimeSpan.FromSeconds(edit.StartSeconds);
        TimeSpan absoluteEnd = asset.SourceStart + TimeSpan.FromSeconds(edit.EndSeconds);
        if (absoluteStart < windowStart || absoluteEnd <= absoluteStart || absoluteEnd > windowEnd)
            throw new ArgumentException($"Caption phrase {index + 1} must remain inside the retained source window.");
        IReadOnlyList<StudioCaptionWordEdit> words = edit.Words ?? original?.Words.Select(w => new StudioCaptionWordEdit(
            w.Text, (w.AbsoluteSourceStart - asset.SourceStart).TotalSeconds,
            (w.AbsoluteSourceEnd - asset.SourceStart).TotalSeconds, w.IsEmphasized)).ToArray() ?? [];
        // Punctuation/case changes preserve lexical alignment. Other corrections need
        // explicit word correction or new provider alignment; never synthesize timing.
        bool aligned = Lexical(edit.Text) == Lexical(string.Concat(words.Select(w => w.Text)));
        var retainedWords = new List<AudioTranscriptionWord>();
        if (aligned)
        {
            foreach (StudioCaptionWordEdit word in words)
            {
                if (!double.IsFinite(word.StartSeconds) || !double.IsFinite(word.EndSeconds))
                    throw new ArgumentException("Word times must be finite.");
                TimeSpan start = asset.SourceStart + TimeSpan.FromSeconds(word.StartSeconds);
                TimeSpan end = asset.SourceStart + TimeSpan.FromSeconds(word.EndSeconds);
                if (start < absoluteStart || end > absoluteEnd || end <= start)
                {
                    retainedWords.Clear();
                    break;
                }
                AudioTranscriptionWord? previous = original?.Words.FirstOrDefault(w => w.Text == word.Text &&
                    w.AbsoluteSourceStart == start && w.AbsoluteSourceEnd == end);
                retainedWords.Add(new AudioTranscriptionWord(word.Text, start - windowStart, end - windowStart,
                    start, end, edit.AlignmentProvenance is null ? previous?.ProviderReportedProbability : null, word.IsEmphasized));
            }
        }
        var warnings = original?.Warnings.Where(warning => warning.Code != AudioTranscriptionWarningCode.CorrectedTextAlignment).ToList() ?? [];
        if (edit.AlignmentProvenance is not null)
            warnings.Add(new AudioTranscriptionWarning(AudioTranscriptionWarningCode.CorrectedTextAlignment, edit.AlignmentProvenance, edit.Id));
        return new AudioTranscriptionSegment(edit.Id, neighborhood, edit.Text,
            absoluteStart - windowStart, absoluteEnd - windowStart, absoluteStart, absoluteEnd,
            retainedWords, original?.Text == edit.Text ? original.ProviderReportedConfidence : null,
            edit.AlignmentProvenance is null ? original?.Language : new AudioTranscriptionLanguage("en", "English"), warnings, edit.Speaker, edit.SecondaryText);
    }

    private static bool IsUnchangedDraft(AudioTranscriptionSegment original, StudioCaptionSegmentEdit edit, TimeSpan cutStart)
    {
        if (original.Text != edit.Text || original.Speaker != edit.Speaker || original.SecondaryText != edit.SecondaryText ||
            (original.AbsoluteSourceStart - cutStart).TotalSeconds != edit.StartSeconds ||
            (original.AbsoluteSourceEnd - cutStart).TotalSeconds != edit.EndSeconds ||
            original.Warnings.LastOrDefault(warning => warning.Code == AudioTranscriptionWarningCode.CorrectedTextAlignment)?.Message != edit.AlignmentProvenance)
            return false;
        if (edit.Words is null) return true;
        return original.Words.Count == edit.Words.Count && !original.Words.Where((word, index) =>
            word.Text != edit.Words[index].Text || word.IsEmphasized != edit.Words[index].IsEmphasized ||
            (word.AbsoluteSourceStart - cutStart).TotalSeconds != edit.Words[index].StartSeconds ||
            (word.AbsoluteSourceEnd - cutStart).TotalSeconds != edit.Words[index].EndSeconds).Any();
    }

    internal static string Lexical(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));
}
