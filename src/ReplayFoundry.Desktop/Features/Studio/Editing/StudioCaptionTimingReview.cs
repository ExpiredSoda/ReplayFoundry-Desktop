using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioCaptionTimingIssue(string AssetId, string SegmentId, string ClipTitle,
    double StartSeconds, string Text, string Reason, bool NeedsWordTiming, bool BlocksPop)
{
    public string Label => NeedsWordTiming ? "Needs timing" : "Listen to check";
    public string Location => $"{TimeSpan.FromSeconds(Math.Max(0, StartSeconds)):mm\\:ss\\.ff} · {ClipTitle}";
    public string AccessibleName => $"Review caption at {Location}. {Label}. {Reason}";
}

/// <summary>Observable timing defects and advisory acoustic fit, never a transcription confidence estimate.</summary>
internal static class StudioCaptionTimingReview
{
    internal static IReadOnlyList<StudioCaptionTimingIssue> Find(GenerationOutputAsset asset,
        IReadOnlyList<StudioCaptionSegmentEdit>? edits = null)
    {
        bool blocks = asset.RenderSettings.BurnCaptions && asset.Appearance.CaptionStyle == GenerationCaptionStylePreset.Pop;
        var issues = new List<StudioCaptionTimingIssue>();
        var phrases = edits ?? StudioCaptionTrackEditing.CreateDrafts(asset);
        foreach (var edit in phrases)
        {
            if (edit.EndSeconds <= 0 || edit.StartSeconds >= asset.Duration.TotalSeconds) continue;
            string? reason = null;
            bool missing = false;
            if (!double.IsFinite(edit.StartSeconds) || !double.IsFinite(edit.EndSeconds) || edit.EndSeconds <= edit.StartSeconds)
            { reason = "Set a valid start and end for this phrase."; missing = true; }
            else
            {
                var words = CreateWordRows(edit);
                var invalid = words.Where(word => !HasTiming(word) || word.StartSeconds < edit.StartSeconds ||
                    word.EndSeconds > edit.EndSeconds).ToArray();
                if (invalid.Length > 0 || words.Count == 0)
                {
                    string names = string.Join(", ", invalid.Take(4).Select(word => $"“{word.Text}”"));
                    reason = names.Length == 0 ? "This phrase needs word timing." : $"Timing missing or invalid for {names}" +
                        (invalid.Length > 4 ? $" and {invalid.Length - 4} more words." : ".");
                    missing = true;
                }
                else if (words.Zip(words.Skip(1)).Any(pair => pair.First.EndSeconds > pair.Second.StartSeconds))
                { reason = "Word times overlap or run out of order. Adjust their boundaries."; missing = true; }
                else
                    reason = StudioCaptionPacingReview.Assess(edit, phrases, words, asset.Appearance.CaptionStyle, asset.Duration.TotalSeconds) ??
                        (words.Any(word => word.AcousticScore < .15)
                            ? "Some audio matches are weak. Listen to check the timing; the words are not necessarily wrong." : null);
            }
            if (reason is not null) issues.Add(new(asset.Id, edit.Id, asset.EditorialMetadata?.Title ?? asset.DisplayName,
                double.IsFinite(edit.StartSeconds) ? Math.Max(0, edit.StartSeconds) : 0,
                edit.Text, reason, missing, missing && blocks));
        }
        return issues;
    }

    internal static bool HasTiming(StudioCaptionWordEdit word) => double.IsFinite(word.StartSeconds) &&
        double.IsFinite(word.EndSeconds) && Math.Round(word.EndSeconds * 100, MidpointRounding.AwayFromZero) >
            Math.Round(word.StartSeconds * 100, MidpointRounding.AwayFromZero);

    // Recover unique ordered matches around omitted words. Missing clocks stay
    // blank. Repeated, ambiguous text must not borrow a different word's clock.
    internal static IReadOnlyList<StudioCaptionWordEdit> CreateWordRows(StudioCaptionSegmentEdit edit)
    {
        string[] tokens = Regex.Matches(edit.Text, @"\S+").Select(match => match.Value)
            .Where(token => token.Any(char.IsLetterOrDigit)).ToArray();
        var words = (edit.Words ?? []).Where(word => word.Text.Any(char.IsLetterOrDigit)).ToArray();
        string[] source = tokens.Select(StudioCaptionTrackEditing.Lexical).ToArray();
        string[] expected = words.Select(word => StudioCaptionTrackEditing.Lexical(word.Text)).ToArray();
        if (source.SequenceEqual(expected)) return words.Select((word, index) => word with { Text = tokens[index] }).ToArray();
        var result = tokens.Select(token => new StudioCaptionWordEdit(token, double.NaN, double.NaN)).ToArray();
        if (tokens.Length > 512 || words.Length > tokens.Length) return result;
        var paths = new byte[tokens.Length + 1, words.Length + 1];
        for (int i = 0; i <= tokens.Length; i++) paths[i, words.Length] = 1;
        for (int i = tokens.Length - 1; i >= 0; i--)
            for (int j = words.Length - 1; j >= 0; j--)
                paths[i, j] = (byte)Math.Min(2, paths[i + 1, j] + (source[i] == expected[j] ? paths[i + 1, j + 1] : 0));
        if (paths[0, 0] != 1) return result;
        int wordIndex = 0;
        for (int i = 0; i < tokens.Length && wordIndex < words.Length; i++)
            if (source[i] == expected[wordIndex] && paths[i + 1, wordIndex + 1] > 0)
                result[i] = words[wordIndex++] with { Text = tokens[i] };
        return result;
    }
}
