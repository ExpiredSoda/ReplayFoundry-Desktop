using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Generate.Handoff;

internal static class GenerationSourceBindingPolicy
{
    internal static void RequireCaptionSelection(
        GenerationCaptionSourceSelection selection,
        MediaProbeResult sourceMedia,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(sourceMedia);
        if (!SameSource(selection.SourceFullPath, sourceMedia.FullPath) ||
            !sourceMedia.AudioStreams.Any(stream =>
                stream.Index == selection.AbsoluteAudioStreamIndex))
        {
            throw new ArgumentException(
                "Caption audio must use an inspected stream from the same source media.",
                parameterName);
        }
    }

    internal static void RequireCaptionTrack(
        GenerationCandidateCaptionTrack track,
        string candidateId,
        MediaProbeResult sourceMedia,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentNullException.ThrowIfNull(sourceMedia);
        RequireCaptionSelection(
            track.SourceSelection,
            sourceMedia,
            parameterName);
        TimeSpan captionEnd =
            track.SourceWindowStart + track.SourceWindowDuration;
        if (!track.CandidateId.Equals(
                candidateId,
                StringComparison.Ordinal) ||
            track.SourceDuration != sourceMedia.Duration ||
            track.SourceWindowStart < TimeSpan.Zero ||
            captionEnd > sourceMedia.Duration ||
            sourceStart < TimeSpan.Zero ||
            sourceEnd <= sourceStart ||
            sourceEnd > sourceMedia.Duration)
        {
            throw new ArgumentException(
                "Captions must belong to the same candidate and source, with both retained windows bounded by that source.",
                parameterName);
        }
    }

    internal static void RequireEditorialContext(
        ClipEditorialContext context,
        string candidateId,
        MediaProbeResult sourceMedia,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        bool requireExactRange,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentNullException.ThrowIfNull(sourceMedia);
        if (!context.CandidateId.Equals(
                candidateId,
                StringComparison.Ordinal) ||
            !SameSource(context.SourceFullPath, sourceMedia.FullPath) ||
            context.SourceDuration != sourceMedia.Duration ||
            requireExactRange &&
            (context.SourceStart != sourceStart ||
             context.SourceEnd != sourceEnd))
        {
            throw new ArgumentException(
                requireExactRange
                    ? "Editorial context must belong to the same candidate, source, and exact hidden-moment range."
                    : "Editorial context must belong to the same candidate and source media.",
                parameterName);
        }
    }

    private static bool SameSource(string left, string right) =>
        Path.GetFullPath(left).Equals(
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
}
