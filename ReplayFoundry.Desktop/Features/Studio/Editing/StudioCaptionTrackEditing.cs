using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioCaptionSegmentEdit(
    string Id,
    string Text,
    double StartSeconds,
    double EndSeconds);

internal static class StudioCaptionTrackEditing
{
    public static IReadOnlyList<StudioCaptionSegmentEdit> CreateDrafts(
        GenerationOutputAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.Captions?.Segments
            .Select(static segment => new StudioCaptionSegmentEdit(
                segment.Id,
                segment.Text,
                segment.RelativeStart.TotalSeconds,
                segment.RelativeEnd.TotalSeconds))
            .ToArray() ?? [];
    }

    public static GenerationOutputAsset Apply(
        IGenerationOutputEditor outputEditor,
        GenerationOutputProject project,
        GenerationOutputAsset asset,
        IReadOnlyList<StudioCaptionSegmentEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(outputEditor);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(edits);
        GenerationCandidateCaptionTrack track = asset.Captions ??
            throw new InvalidOperationException(
                "The selected Studio asset has no editable caption track.");
        if (edits.Count != track.Segments.Count)
        {
            throw new ArgumentException(
                "Caption edits must preserve every retained segment.",
                nameof(edits));
        }

        AudioTranscriptionSegment[] segments = edits
            .Select((edit, index) => CreateSegment(track, edit, index))
            .ToArray();
        GenerationOutputAsset replacement = asset.WithCaptionTrack(
            track.WithEditedSegments(segments));
        outputEditor.ReplaceAsset(project.Id, replacement);
        return replacement;
    }

    private static AudioTranscriptionSegment CreateSegment(
        GenerationCandidateCaptionTrack track,
        StudioCaptionSegmentEdit edit,
        int index)
    {
        if (string.IsNullOrWhiteSpace(edit.Text) ||
            !double.IsFinite(edit.StartSeconds) ||
            !double.IsFinite(edit.EndSeconds))
        {
            throw new ArgumentException(
                $"Caption segment {index + 1} requires text and finite timing.");
        }
        TimeSpan start = TimeSpan.FromSeconds(edit.StartSeconds);
        TimeSpan end = TimeSpan.FromSeconds(edit.EndSeconds);
        if (start < TimeSpan.Zero ||
            end <= start ||
            end > track.SourceWindowDuration)
        {
            throw new ArgumentException(
                $"Caption segment {index + 1} must be positive, ordered, " +
                "and inside the clip transcription window.");
        }

        AudioTranscriptionSegment original = track.Segments[index];
        bool unchanged = original.Text.Equals(
                edit.Text.Trim(),
                StringComparison.Ordinal) &&
            original.RelativeStart == start &&
            original.RelativeEnd == end;
        TimeSpan absoluteStart = track.SourceWindowStart + start;
        TimeSpan absoluteEnd = track.SourceWindowStart + end;
        return new AudioTranscriptionSegment(
            original.Id,
            original.NeighborhoodId,
            edit.Text,
            start,
            end,
            absoluteStart,
            absoluteEnd,
            unchanged ? original.Words : [],
            unchanged ? original.ProviderReportedConfidence : null,
            original.Language,
            original.Warnings);
    }
}
