using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal enum GenerationCaptureContextKind { Launcher, Desktop, Loading }
internal sealed record GenerationCaptureContextAssessment(GenerationCaptureContextKind Kind,
    IReadOnlyList<int> FrameIndexes);
internal sealed record GenerationApplicationStartupLeadIn(TimeSpan FirstSpeechStart,
    IReadOnlyList<int> FrameIndexes);

internal static class GenerationCaptureContextPolicy
{
    internal static readonly TimeSpan MinimumStartupLeadIn = TimeSpan.FromSeconds(12);

    public static GenerationCaptureContextAssessment? Assess(IReadOnlyList<IReadOnlyList<string>> frames)
    {
        var classified = frames.Select((lines, index) => (Kind: ClassifyFrame(lines), Index: index)).ToArray();
        foreach (GenerationCaptureContextKind kind in Enum.GetValues<GenerationCaptureContextKind>())
        {
            int[] repeated = classified.Where(value => value.Kind == kind).Select(static value => value.Index).ToArray();
            if (repeated.Length >= 2) return new(kind, repeated);
        }
        return null;
    }

    public static bool MayScreen(GenerationDiscoveryIntent intent, ContentEmphasis emphasis) =>
        intent.Phrases.Count == 0 && intent.NaturalLanguageQuery.Length == 0 &&
        emphasis != ContentEmphasis.CommentaryFocused && intent.MomentType is
            GenerationMomentIntent.Any or GenerationMomentIntent.Action or GenerationMomentIntent.Failure;

    internal static bool ShouldExcludeAutomatically(GenerationCaptureContextAssessment assessment,
        GenerationSourceSpeechActivity speech, MomentCandidate candidate, GenerationSetupOptions setup)
    {
        if (assessment.Kind == GenerationCaptureContextKind.Loading || assessment.FrameIndexes.Count < 2 ||
            !MayScreen(setup.DiscoveryIntent, setup.ContentEmphasis)) return false;
        var media = speech.Source.PreparedSource.Media;
        // The production VAD service publishes a stream only after every source
        // chunk succeeds. Empty/missing stream collections are unavailable
        // evidence, not silence. Require every inspected audio stream here.
        if (media.AudioStreams.Count == 0 || speech.Streams.Count != media.AudioStreams.Count ||
            media.AudioStreams.Any(stream => !speech.Streams.Any(result => result.AbsoluteAudioStreamIndex == stream.Index)) ||
            speech.Streams.Any(stream => stream.ExecutionManifests.Count == 0 ||
                stream.ExecutionManifests.Any(manifest => manifest.Warnings.Any(warning => warning.Code == SpeechActivityWarningCode.RuntimeReportedWarning)) ||
                stream.Intervals.Any(interval => interval.AbsoluteStart < candidate.Window.End && interval.AbsoluteEnd > candidate.Window.Start)))
            return false;
        return !setup.MomentGuidance.ForSource(media.FullPath).Any(guidance =>
            guidance.Kind == UserMomentGuidanceKind.PriorityPoint
                ? candidate.Window.Contains(guidance.Timestamp)
                : candidate.Window.Start < guidance.End && candidate.Window.End > guidance.Start);
    }

    internal static GenerationApplicationStartupLeadIn? FindApplicationStartupLeadIn(
        GenerationCaptureContextAssessment assessment, IReadOnlyList<TimeSpan> frameTimestamps,
        GenerationSourceSpeechActivity speech, MomentCandidate candidate, GenerationSetupOptions setup)
    {
        if (assessment.Kind is not (GenerationCaptureContextKind.Launcher or GenerationCaptureContextKind.Desktop) ||
            !MayScreen(setup.DiscoveryIntent, setup.ContentEmphasis)) return null;
        var media = speech.Source.PreparedSource.Media;
        if (candidate.Window.SourceDuration != media.Duration || media.AudioStreams.Count == 0 ||
            speech.Streams.Count != media.AudioStreams.Count ||
            media.AudioStreams.Any(stream => !speech.Streams.Any(result => result.AbsoluteAudioStreamIndex == stream.Index)) ||
            speech.Streams.Any(stream => stream.ExecutionManifests.Count == 0 || stream.ExecutionManifests.Any(manifest =>
                manifest.Warnings.Any(static warning => warning.Code == SpeechActivityWarningCode.RuntimeReportedWarning)))) return null;
        // A published source result is complete only after all VAD chunks succeed.
        // NoSpeechDetected and deterministic padding/duration splitting retain
        // bounded speech intervals; runtime warnings and missing work do not.
        // Require speech later in this cut: the full-silence rule is separate.
        SpeechActivityInterval[] spoken = speech.Streams.SelectMany(static stream => stream.Intervals)
            .Where(interval => interval.AbsoluteStart < candidate.Window.End && interval.AbsoluteEnd > candidate.Window.Start).ToArray();
        if (spoken.Length == 0) return null;
        TimeSpan first = spoken.Min(static interval => interval.AbsoluteStart);
        TimeSpan leading = first - candidate.Window.Start;
        if (leading < MinimumStartupLeadIn || leading.Ticks < candidate.Window.Duration.Ticks / 2 ||
            setup.MomentGuidance.ForSource(media.FullPath).Any(guidance =>
                guidance.Kind == UserMomentGuidanceKind.PriorityPoint
                    ? candidate.Window.Contains(guidance.Timestamp)
                    : candidate.Window.Start < guidance.End && candidate.Window.End > guidance.Start)) return null;
        int[] leadingFrames = assessment.FrameIndexes
            .Where(index => index >= 0 && index < frameTimestamps.Count &&
                frameTimestamps[index] >= candidate.Window.Start && frameTimestamps[index] < first)
            .DistinctBy(index => frameTimestamps[index]).ToArray();
        return leadingFrames.Length >= 2 ? new(first, leadingFrames) : null;
    }

    internal static IReadOnlyList<TimeSpan> AdditionalSampleTimestamps(MomentCandidateWindow window,
        IReadOnlyList<TimeSpan> timestamps, IReadOnlyList<IReadOnlyList<string>> frames)
    {
        if (timestamps.Count != frames.Count || timestamps.Count > 3 || Assess(frames) is not null ||
            frames.Count(lines => ClassifyFrame(lines) is GenerationCaptureContextKind.Launcher or GenerationCaptureContextKind.Desktop) != 1)
            return [];
        return new[] { window.Start + TimeSpan.FromTicks(window.Duration.Ticks / 4),
                window.Start + TimeSpan.FromTicks(window.Duration.Ticks * 3 / 4) }
            .Where(timestamp => timestamp >= window.Start && timestamp < window.End && !timestamps.Contains(timestamp))
            .Distinct().Take(2).ToArray();
    }

    internal static string NormalizeLine(string line) => Regex.Replace(line.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    internal static GenerationCaptureContextKind? ClassifyFrame(IReadOnlyList<string> lines)
    {
        string[] normalized = lines.Select(NormalizeLine).ToArray();
        string all = " " + string.Join(" ", normalized) + " ";
        bool Has(string value) => all.Contains(" " + value + " ", StringComparison.Ordinal);
        // Multiple labels that jointly identify software chrome. Gameplay HUD
        // words such as health, ammo, inventory or achievement are insufficient.
        if (Has("cloud status") && Has("last played") &&
            (Has("play time") || Has("playtime") || Has("community hub") || Has("write a review")))
            return GenerationCaptureContextKind.Launcher;
        if (Has("recycle bin") && Has("this pc") && (Has("network") || Has("control panel")) ||
            Has("task manager") && Has("processes") && Has("performance") && Has("startup apps"))
            return GenerationCaptureContextKind.Desktop;
        if (normalized.Any(static line => line is "loading" or "loading please wait" or "please wait loading" ||
            Regex.IsMatch(line, @"^loading\s+\d{1,3}$")))
            return GenerationCaptureContextKind.Loading;
        return null;
    }
}
