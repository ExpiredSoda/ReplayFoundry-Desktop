using System.Text.RegularExpressions;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal enum GenerationCaptureContextKind { Launcher, Desktop, Loading, Startup, GameMenu }
internal sealed record GenerationCaptureContextAssessment(GenerationCaptureContextKind Kind,
    IReadOnlyList<int> FrameIndexes, bool DominatesSampledWindow = false);
internal sealed record GenerationApplicationStartupLeadIn(TimeSpan FirstSpeechStart,
    IReadOnlyList<int> FrameIndexes);

internal static class GenerationCaptureContextPolicy
{
    internal static readonly TimeSpan MinimumStartupLeadIn = TimeSpan.FromSeconds(12);

    internal static GenerationCaptureContextAssessment? Assess(IReadOnlyList<VisualTextFrameObservation> frames)
    {
        var lines = frames.Select(frame => (IReadOnlyList<string>)frame.Lines.Select(static line => line.Text).ToArray()).ToArray();
        GenerationCaptureContextAssessment? repeated = Assess(lines);
        if (repeated is not null) return repeated with
        {
            DominatesSampledWindow = frames.Count >= 3 && repeated.FrameIndexes.Count * 3 >= frames.Count * 2,
        };
        // An application window followed by blank loading frames is one startup
        // sequence. Missing OCR alone is never evidence that a picture is blank.
        int[] application = Enumerable.Range(0, frames.Count).Where(index => ClassifyFrame(lines[index]) is
            GenerationCaptureContextKind.Launcher or GenerationCaptureContextKind.Desktop or
            GenerationCaptureContextKind.Startup or GenerationCaptureContextKind.GameMenu).ToArray();
        if (frames.Count >= 3 && application.Length > 0 && Enumerable.Range(0, frames.Count)
            .All(index => application.Contains(index) || IsNearlyBlack(frames[index])))
            return new(GenerationCaptureContextKind.Startup, Enumerable.Range(0, frames.Count).ToArray(), true);
        return null;
    }

    private static bool IsNearlyBlack(VisualTextFrameObservation frame)
    {
        try
        {
            using var stream = new MemoryStream(frame.Request.Frame.PngData.ToArray(), writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var scaled = new TransformedBitmap(decoder.Frames[0], new ScaleTransform(
                64d / decoder.Frames[0].PixelWidth, 64d / decoder.Frames[0].PixelHeight));
            var bitmap = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            int dark = 0;
            for (int index = 0; index < pixels.Length; index += 4)
                if (pixels[index] < 24 && pixels[index + 1] < 24 && pixels[index + 2] < 24) dark++;
            return dark >= pixels.Length / 4d * 0.94;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

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
        bool requestedByUser = setup.MomentGuidance.ForSource(media.FullPath).Any(guidance =>
            guidance.Kind == UserMomentGuidanceKind.PriorityPoint
                ? candidate.Window.Contains(guidance.Timestamp)
                : candidate.Window.Start < guidance.End && candidate.Window.End > guidance.Start);
        if (requestedByUser) return false;
        // Speech over a launcher or a startup screen does not establish action.
        // Explicit commentary/humor requests bypass this screen through MayScreen.
        if (assessment.DominatesSampledWindow) return true;
        // The production VAD service publishes a stream only after every source
        // chunk succeeds. Empty/missing stream collections are unavailable
        // evidence, not silence. Require every inspected audio stream here.
        if (media.AudioStreams.Count == 0 || speech.Streams.Count != media.AudioStreams.Count ||
            media.AudioStreams.Any(stream => !speech.Streams.Any(result => result.AbsoluteAudioStreamIndex == stream.Index)) ||
            speech.Streams.Any(stream => stream.ExecutionManifests.Count == 0 ||
                stream.ExecutionManifests.Any(manifest => manifest.Warnings.Any(warning => warning.Code == SpeechActivityWarningCode.RuntimeReportedWarning)) ||
                stream.Intervals.Any(interval => interval.AbsoluteStart < candidate.Window.End && interval.AbsoluteEnd > candidate.Window.Start)))
            return false;
        return true;
    }

    internal static GenerationApplicationStartupLeadIn? FindApplicationStartupLeadIn(
        GenerationCaptureContextAssessment assessment, IReadOnlyList<TimeSpan> frameTimestamps,
        GenerationSourceSpeechActivity speech, MomentCandidate candidate, GenerationSetupOptions setup)
    {
        if (assessment.Kind is not (GenerationCaptureContextKind.Launcher or GenerationCaptureContextKind.Desktop or GenerationCaptureContextKind.Startup) ||
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
            frames.Count(lines => ClassifyFrame(lines) is GenerationCaptureContextKind.Launcher or GenerationCaptureContextKind.Desktop or
                GenerationCaptureContextKind.Startup or GenerationCaptureContextKind.GameMenu) != 1)
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
        if (Has("store library community") || normalized.Contains("steam settings"))
            return GenerationCaptureContextKind.Launcher;
        if (Has("select a save file") && Has("new game") ||
            Has("select difficulty") && new[] { "recruit", "regular", "hardened", "veteran", "realism", "easy", "normal", "hard" }.Count(Has) >= 2 ||
            Has("new game") && Has("continue") && (Has("settings") || Has("load game")) ||
            Has("play") && Has("weapons") && Has("operators") && Has("battle pass") ||
            Has("campaign") && Has("multiplayer") && Has("zombies"))
            return GenerationCaptureContextKind.GameMenu;
        if (normalized.Any(static line => line is "activision" or "treyarch" or "high moon studios" or "sledgehammer games" or "raven software") ||
            Has("activision") && (Has("high moon studios") || Has("treyarch") || Has("sledgehammer games")) ||
            Has("broadcasting is") && Has("any steam") && Has("broadcast") ||
            Regex.IsMatch(all, @"\bsledgehamm\p{L}*\b") && Has("games") && Regex.IsMatch(all, @"\bshanghai studi\p{L}*\b"))
            return GenerationCaptureContextKind.Startup;
        if (Has("recycle bin") && Has("this pc") && (Has("network") || Has("control panel")) ||
            Has("task manager") && Has("processes") && Has("performance") && Has("startup apps"))
            return GenerationCaptureContextKind.Desktop;
        if (normalized.Any(static line => line is "loading" or "loading please wait" or "please wait loading" ||
            Regex.IsMatch(line, @"^loading\s+\d{1,3}$")))
            return GenerationCaptureContextKind.Loading;
        return null;
    }
}
