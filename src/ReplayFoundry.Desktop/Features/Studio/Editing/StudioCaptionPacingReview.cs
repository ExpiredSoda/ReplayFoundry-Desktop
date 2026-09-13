using System.Globalization;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal static class StudioCaptionPacingReview
{
    // A review heuristic, not a claim about speech or a reason to alter measured timing.
    internal const double LongPopWordSeconds = 2;

    internal static string? Assess(StudioCaptionSegmentEdit edit, IReadOnlyList<StudioCaptionSegmentEdit> phrases,
        IReadOnlyList<StudioCaptionWordEdit> words, GenerationCaptionStylePreset style, double duration)
    {
        bool overlap = phrases.Any(other => other.Id != edit.Id && double.IsFinite(other.StartSeconds) &&
            double.IsFinite(other.EndSeconds) && Math.Max(0, Math.Max(edit.StartSeconds, other.StartSeconds)) <
                Math.Min(duration, Math.Min(edit.EndSeconds, other.EndSeconds)));
        if (overlap) return "This phrase overlaps another caption in this cut. Listen for simultaneous speakers and check whether both captions should appear together.";
        if (style != GenerationCaptionStylePreset.Pop) return null;
        var held = words.Where(word => word.EndSeconds > 0 && word.StartSeconds < duration &&
            word.EndSeconds - word.StartSeconds >= LongPopWordSeconds)
            .OrderByDescending(word => word.EndSeconds - word.StartSeconds).FirstOrDefault();
        if (held is null) return null;
        string seconds = (held.EndSeconds - held.StartSeconds).ToString("0.#", CultureInfo.InvariantCulture);
        return $"“{held.Text}” is timed for {seconds} seconds. Listen for a pause and check its end time; a long spoken word can be correct.";
    }
}
