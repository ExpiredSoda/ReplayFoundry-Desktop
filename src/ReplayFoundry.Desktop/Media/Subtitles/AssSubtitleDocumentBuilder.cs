using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Media.Subtitles;

public sealed record AssSubtitleDocument(
    string Script,
    GenerationCaptionStylePreset RequestedStyle,
    GenerationCaptionStylePreset EffectiveStyle,
    IReadOnlyList<string> Warnings);

public static class AssSubtitleDocumentBuilder
{
    public const string PolicyVersion = "2.3";

    public static AssSubtitleDocument Build(
        GenerationCandidateCaptionTrack track,
        int frameWidth,
        int frameHeight,
        TimeSpan? clipSourceStart = null,
        TimeSpan? clipDuration = null,
        double verticalPositionPercent = 82,
        StudioCaptionWordLimitPreset captionWordLimit =
            StudioCaptionWordLimitPreset.FullSegment,
        double captionMaximumWidthPercent =
            StudioClipAppearance.DefaultCaptionMaximumWidthPercent,
        double captionFontScalePercent =
            StudioClipAppearance.DefaultCaptionFontScalePercent,
        StudioCaptionTypography? captionTypography = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        TimeSpan actualSourceStart = clipSourceStart ?? track.SourceWindowStart;
        TimeSpan actualClipDuration = clipDuration ?? track.SourceWindowDuration;
        StudioCaptionCutProjectionResult cutProjection = StudioCaptionCutProjection.Project(track,
            actualSourceStart, actualSourceStart + actualClipDuration);
        track = cutProjection.Track;
        captionTypography ??= StudioCaptionTypography.Default;
        verticalPositionPercent = captionTypography.ConstrainVerticalPosition(verticalPositionPercent);
        captionMaximumWidthPercent = captionTypography.ConstrainMaximumWidth(captionMaximumWidthPercent);
        if (track.Segments.Any(s => !string.IsNullOrWhiteSpace(s.SecondaryText))) verticalPositionPercent = Math.Min(80, verticalPositionPercent);
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth));
        }
        if (!double.IsFinite(verticalPositionPercent) ||
            verticalPositionPercent is < 10 or > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verticalPositionPercent),
                "Caption position must remain between 10 and 90 percent from the top edge.");
        }

        GenerationCaptionStylePreset effective =
            StudioCaptionPresentationPolicy.ResolveEffectiveStyle(track);
        StudioCaptionWordLimitPreset effectiveWordLimit =
            StudioCaptionPresentationPolicy.ResolveEffectiveWordLimit(
                effective,
                captionWordLimit);
        StudioCaptionFrameLayout layout =
            StudioCaptionPresentationPolicy.CalculateFrameLayout(
                frameWidth,
                frameHeight,
                effective,
                captionMaximumWidthPercent,
                captionFontScalePercent);
        var warnings = new List<string>(cutProjection.Warnings);
        if (StudioCaptionFontResolver.Resolve(captionTypography.FontFamily).Warning is { } fontWarning) warnings.Add(fontWarning);
        if (StudioCaptionBoundsReview.GetWarning(track, captionTypography, effective, effectiveWordLimit,
                layout, frameWidth, frameHeight, verticalPositionPercent) is { } boundsWarning) warnings.Add(boundsWarning);
        bool hasPresentationTiming = StudioCaptionPresentationPolicy
            .HasPresentationTimedWordCoverage(track);
        if (StudioCaptionPresentationPolicy.RequiresTimedWords(effective) &&
            !hasPresentationTiming)
        {
            warnings.Add(
                "Word timing could not be aligned reliably for every caption. The selected effect uses each whole phrase instead of following individual spoken words.");
        }
        if (effectiveWordLimit !=
                StudioCaptionWordLimitPreset.FullSegment &&
            !hasPresentationTiming)
        {
            warnings.Add(
                "The selected word window was not applied to every segment because its timed words do not completely match the retained caption text. Those segments remain whole rather than dropping or inventing words.");
        }

        var builder = new StringBuilder();
        WriteHeader(builder, frameWidth, frameHeight, layout);
        if (actualSourceStart < TimeSpan.Zero ||
            actualClipDuration <= TimeSpan.Zero ||
            actualSourceStart + actualClipDuration >
                track.SourceDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clipSourceStart),
                "Caption coverage must remain inside the source.");
        }
        TimeSpan relativeShift =
            track.SourceWindowStart -
            actualSourceStart;
        double centerY = Math.Round(frameHeight * verticalPositionPercent / 100d);
        string Position(double y) => CreatePosition(frameWidth, layout.HorizontalMarginPixels, y, captionTypography);
        foreach (StudioCaptionCue cue in
                 StudioCaptionPresentationPolicy.ProjectCues(
                     track,
                     effectiveWordLimit))
        {
            StudioCaptionCue displayed = StudioCaptionDisplayText.Transform(cue, captionTypography);
            bool explicitLines = captionTypography.SafeAreaInsets is not null || captionTypography.LineSpacingPercent != 100 || captionTypography.HasCustomBackground(effective);
            if (!explicitLines)
                WriteCue(builder, displayed, effective, relativeShift, actualClipDuration, Position(centerY));
            else
            {
                IEnumerable<StudioCaptionCue> parts = effective == GenerationCaptionStylePreset.Pop && displayed.Words.Count > 0
                    ? PopDisplayCues(displayed) : [displayed];
                foreach (var part in parts)
                {
                    var lines = StudioCaptionLineLayout.Create(part.Text, captionTypography,
                        StudioCaptionPresentationPolicy.GetWpfPreviewFontSize(layout), layout.MaximumWidthPixels);
                    foreach (var line in lines.Lines.Where(line => line.Length > 0 && !string.IsNullOrWhiteSpace(line.Text)))
                        WriteCue(builder, StudioCaptionDisplayText.Slice(part, line.StartIndex, line.Length),
                            effective, relativeShift, actualClipDuration, Position(centerY + line.CenterOffset) + "{\\q2}");
                }
            }
        }

        foreach (var segment in track.Segments.Where(s => !string.IsNullOrWhiteSpace(s.SecondaryText)))
        {
            (TimeSpan start, TimeSpan end) = ClampRange(segment.AbsoluteSourceStart - actualSourceStart,
                segment.AbsoluteSourceEnd - actualSourceStart, actualClipDuration);
            if (end <= start) continue;
            double secondaryY = Math.Round(frameHeight * Math.Min(90, verticalPositionPercent + 10) / 100d);
            string font = string.Create(CultureInfo.InvariantCulture, $"{{\\fs{Math.Round(layout.BaseFontSizePixels * 0.75)}}}");
            string secondary = captionTypography.DisplayText(segment.SecondaryText!);
            if (captionTypography.SafeAreaInsets is null && captionTypography.LineSpacingPercent == 100 && !captionTypography.HasCustomBackground(GenerationCaptionStylePreset.Clean))
                WriteDialogue(builder, 0, start, end, "Clean", Position(secondaryY) + font + Escape(secondary));
            else
                foreach (var line in StudioCaptionLineLayout.Create(secondary, captionTypography,
                             StudioCaptionPresentationPolicy.GetWpfPreviewFontSize(layout) * 0.75, layout.MaximumWidthPixels).Lines)
                    if (!string.IsNullOrWhiteSpace(line.Text))
                        WriteDialogue(builder, 0, start, end, "Clean", Position(secondaryY + line.CenterOffset) + font + "{\\q2}" + Escape(line.Text));
        }

        return new AssSubtitleDocument(
            ApplyTypography(builder.ToString(), captionTypography, effective),
            track.RequestedStyle,
            effective,
            warnings.AsReadOnly());
    }

    private static IEnumerable<StudioCaptionCue> PopDisplayCues(StudioCaptionCue cue)
    {
        for (int index = 0; index < cue.Words.Count; index++)
        {
            AudioTranscriptionWord word = cue.Words[index];
            string text = StudioCaptionDisplayText.SliceWordText(cue, cue.WordSpans[index]);
            TimeSpan start = index == 0 ? cue.RelativeStart : word.RelativeStart;
            TimeSpan end = index + 1 < cue.Words.Count ? cue.Words[index + 1].RelativeStart : cue.RelativeEnd;
            yield return new StudioCaptionCue(text, start, end,
                cue.AbsoluteSourceStart + start - cue.RelativeStart, cue.AbsoluteSourceStart + end - cue.RelativeStart,
                [new StudioCaptionWordSpan(word, 0, text.Length)]);
        }
    }

    private static string CreatePosition(int width, int margin, double y, StudioCaptionTypography typography)
    {
        int alignment = typography.Alignment switch { StudioCaptionAlignment.Left => 4, StudioCaptionAlignment.Right => 6, _ => 5 };
        int x = typography.Alignment switch { StudioCaptionAlignment.Left => margin, StudioCaptionAlignment.Right => width - margin, _ => width / 2 };
        return string.Create(CultureInfo.InvariantCulture, $"{{\\an{alignment}\\pos({x},{y:0.###})}}") + (typography.RightToLeft ? "\u200F" : "");
    }

    private static string ApplyTypography(string script, StudioCaptionTypography typography, GenerationCaptionStylePreset style)
    {
        string resolvedFont = StudioCaptionFontResolver.Resolve(typography.FontFamily).Family;
        if (typography == StudioCaptionTypography.Default && resolvedFont == typography.FontFamily) return script;
        bool customPanel = typography.HasCustomBackground(style);
        string panelColor = "&H" + ((int)Math.Round(255 * (1 - typography.BackgroundOpacityPercent / 100))).ToString("X2", CultureInfo.InvariantCulture)
            + StudioCaptionTypography.AssColor(typography.BackgroundColor)[4..];
        var output = new StringBuilder();
        foreach (string original in script.Split(Environment.NewLine))
        {
            string line = original;
            if (line.StartsWith("Style: ", StringComparison.Ordinal))
            {
                string[] fields = line.Split(',');
                fields[1] = resolvedFont;
                // These internal scripts are burned with the pinned libass renderer.
                // Its -1 encoding enables whole-line bidi across emphasis override
                // runs. The leading RLM selects RTL even when a line starts with a
                // player name or digits; standard ASS forces LTR punctuation.
                if (typography.RightToLeft) fields[22] = "-1";
                if (typography.TextColor != "#FFFFFF") fields[3] = StudioCaptionTypography.AssColor(typography.TextColor);
                if (typography.AccentColor != "#FFC75E") fields[4] = StudioCaptionTypography.AssColor(typography.AccentColor);
                if (typography.OutlineColor != "#101010" || fields[0] != "Style: HighContrast" || customPanel) fields[5] = StudioCaptionTypography.AssColor(typography.OutlineColor);
                fields[7] = typography.Bold ? "-1" : "0";
                if (typography.OutlineWidth != 3) fields[16] = typography.OutlineWidth.ToString(CultureInfo.InvariantCulture);
                if (typography.ShadowDepth != 2) fields[17] = typography.ShadowDepth.ToString(CultureInfo.InvariantCulture);
                if (fields[0] == "Style: Pop") fields[3] = StudioCaptionTypography.AssColor(typography.AccentColor);
                if (fields[0] == "Style: HighContrast" && (!typography.HasBackground(style) || customPanel)) fields[15] = "1";
                output.AppendLine(string.Join(',', fields));
                if (customPanel)
                {
                    fields[0] += "Panel"; fields[3] = fields[4] = "&HFF000000";
                    fields[5] = fields[6] = panelColor; fields[15] = "3"; fields[16] = "8"; fields[17] = "0";
                    output.AppendLine(string.Join(',', fields));
                }
                continue;
            }
            if (line.StartsWith("Dialogue: ", StringComparison.Ordinal))
            {
                // Only rewrite generated override blocks: caption text and already mapped style colors stay literal.
                line = Regex.Replace(line, @"(?<!\\)\{[^{}]*\}", match => Regex.Replace(match.Value,
                    @"&H00(?:5EC7FF|00D7FF|DED9D7|A59E98|FFFFFF)", color => color.Value is "&H005EC7FF" or "&H0000D7FF"
                        ? StudioCaptionTypography.AssColor(typography.AccentColor)
                        : typography.TextColor != "#FFFFFF" ? StudioCaptionTypography.AssColor(typography.TextColor) : color.Value));
                line = Regex.Replace(line, @"(?<!\\)\{[^{}]*\}", match => Regex.Replace(match.Value, @"\\fsc([xy])([0-9.]+)", scale =>
                    "\\fsc" + scale.Groups[1].Value + (100 + (double.Parse(scale.Groups[2].Value, CultureInfo.InvariantCulture) - 100) * typography.AnimationIntensityPercent / 100).ToString("0.###", CultureInfo.InvariantCulture)));
                if (customPanel)
                {
                    string[] eventFields = line.Split(',', 10);
                    if (typography.Background != StudioCaptionBackground.Panel && eventFields[3] != "HighContrast")
                    {
                        output.AppendLine(line);
                        continue;
                    }
                    eventFields[0] = "Dialogue: -1"; eventFields[3] += "Panel";
                    eventFields[9] = Regex.Replace(eventFields[9], @"(?<!\\)\{[^{}]*\}", match =>
                    {
                        string placement = string.Concat(Regex.Matches(match.Value, @"\\(?:an[1-9]|pos\([^)]*\)|fs[0-9.]+|q[0-3])").Select(static item => item.Value));
                        return placement.Length == 0 ? "" : "{" + placement + "}";
                    });
                    output.AppendLine(string.Join(',', eventFields));
                }
            }
            output.AppendLine(line);
        }
        return output.ToString();
    }

    private static void WriteHeader(
        StringBuilder builder,
        int width,
        int height,
        StudioCaptionFrameLayout layout)
    {
        int fontSize = layout.BaseFontSizePixels;
        int bottomMargin = Math.Max(
            24,
            checked((int)Math.Round(height * 0.10)));
        builder.AppendLine("[Script Info]");
        builder.AppendLine("; Generated by Replay Foundry ASS policy " + PolicyVersion);
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine("WrapStyle: 1");
        builder.AppendLine("ScaledBorderAndShadow: yes");
        builder.AppendLine($"PlayResX: {width}");
        builder.AppendLine($"PlayResY: {height}");
        builder.AppendLine("YCbCr Matrix: TV.709");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.AppendLine(Style("Clean", fontSize, "&H00FFFFFF", "&H0000D7FF", "&H00101010", "&H78000000", 1, 3, 2, layout.HorizontalMarginPixels, bottomMargin));
        builder.AppendLine(Style("FocusBase", fontSize, "&H00DED9D7", "&H005EC7FF", "&H00101010", "&H78000000", 1, 5, 1, layout.HorizontalMarginPixels, bottomMargin));
        builder.AppendLine(Style("Karaoke", fontSize, "&H005EC7FF", "&H00A59E98", "&H00101010", "&H78000000", 1, 5, 1, layout.HorizontalMarginPixels, bottomMargin));
        builder.AppendLine(Style("Pop", layout.EffectiveFontSizePixels, "&H005EC7FF", "&H005EC7FF", "&H00101010", "&H78000000", 1, 6, 2, layout.HorizontalMarginPixels, bottomMargin));
        builder.AppendLine(Style("HighContrast", fontSize, "&H00FFFFFF", "&H005EC7FF", "&H00000000", "&H14000000", 3, 3, 0, layout.HorizontalMarginPixels, bottomMargin));
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
    }

    private static string Style(
        string name,
        int fontSize,
        string primary,
        string secondary,
        string outline,
        string back,
        int borderStyle,
        int outlineWidth,
        int shadow,
        int horizontalMargin,
        int marginV) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Style: {name},Segoe UI,{fontSize},{primary},{secondary},{outline},{back},-1,0,0,0,100,100,0,0,{borderStyle},{outlineWidth},{shadow},2,{horizontalMargin},{horizontalMargin},{marginV},1");

    private static void WriteCue(
        StringBuilder builder,
        StudioCaptionCue cue,
        GenerationCaptionStylePreset style,
        TimeSpan shift,
        TimeSpan clipDuration,
        string positionOverride)
    {
        (TimeSpan start, TimeSpan end) = ClampRange(
            cue.RelativeStart + shift,
            cue.RelativeEnd + shift,
            clipDuration);
        if (end <= start)
        {
            return;
        }
        switch (style)
        {
            case GenerationCaptionStylePreset.Clean:
                WriteDialogue(
                    builder,
                    0,
                    start,
                    end,
                    "Clean",
                    positionOverride + EscapeCue(cue));
                break;
            case GenerationCaptionStylePreset.HighContrast:
                WriteDialogue(
                    builder,
                    0,
                    start,
                    end,
                    "HighContrast",
                    positionOverride + EscapeCue(cue));
                break;
            case GenerationCaptionStylePreset.WordFocus:
                WriteWordFocus(
                    builder,
                    cue,
                    shift,
                    clipDuration,
                    positionOverride);
                break;
            case GenerationCaptionStylePreset.KaraokeSweep:
                WriteKaraoke(
                    builder,
                    cue,
                    shift,
                    clipDuration,
                    positionOverride);
                break;
            case GenerationCaptionStylePreset.Pop:
                WritePop(
                    builder,
                    cue,
                    shift,
                    clipDuration,
                    positionOverride);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(style));
        }
    }

    private static void WriteWordFocus(
        StringBuilder builder,
        StudioCaptionCue cue,
        TimeSpan shift,
        TimeSpan clipDuration,
        string positionOverride)
    {
        if (cue.WordSpans.Count == 0)
        {
            (TimeSpan start, TimeSpan end) = ClampPartitionedRange(
                cue.RelativeStart + shift,
                cue.RelativeEnd + shift,
                clipDuration);
            if (end > start)
            {
                WriteDialogue(
                    builder,
                    1,
                    start,
                    end,
                    "FocusBase",
                    positionOverride +
                    "{\\c&H0000D7FF&}" + EscapeCue(cue));
            }
            return;
        }

        TimeSpan cursor = cue.RelativeStart + shift;
        for (int index = 0; index < cue.WordSpans.Count; index++)
        {
            StudioCaptionWordSpan span = cue.WordSpans[index];
            AudioTranscriptionWord word = span.Word;
            TimeSpan rawWordStart = word.RelativeStart + shift;
            TimeSpan rawWordEnd = word.RelativeEnd + shift;
            WriteWordFocusStaticInterval(
                builder,
                cue,
                cursor,
                rawWordStart,
                clipDuration,
                positionOverride);
            (TimeSpan wordStart, TimeSpan wordEnd) =
                ClampPartitionedRange(
                rawWordStart,
                rawWordEnd,
                clipDuration);
            if (wordEnd <= wordStart)
            {
                cursor = rawWordEnd;
                continue;
            }
            var text = new StringBuilder();
            text.Append(positionOverride);
            text.Append(EscapeSlice(cue, 0, span.StartIndex));
            text.Append("{\\c&H005EC7FF&\\fscx108\\fscy108\\t(0,120,\\fscx103\\fscy103)}");
            text.Append(EscapeSlice(cue, span.StartIndex, span.Length));
            text.Append("{\\c&H00DED9D7&\\fscx100\\fscy100}");
            text.Append(EscapeSlice(cue, span.StartIndex + span.Length, cue.Text.Length - span.StartIndex - span.Length));
            WriteDialogue(
                builder,
                1,
                wordStart,
                NonZeroEnd(wordStart, wordEnd),
                "FocusBase",
                text.ToString());
            cursor = rawWordEnd;
        }
        WriteWordFocusStaticInterval(
            builder,
            cue,
            cursor,
            cue.RelativeEnd + shift,
            clipDuration,
            positionOverride);
    }

    private static void WriteWordFocusStaticInterval(
        StringBuilder builder,
        StudioCaptionCue cue,
        TimeSpan rawStart,
        TimeSpan rawEnd,
        TimeSpan clipDuration,
        string positionOverride)
    {
        (TimeSpan start, TimeSpan end) = ClampPartitionedRange(
            rawStart,
            rawEnd,
            clipDuration);
        if (end <= start)
        {
            return;
        }
        WriteDialogue(
            builder,
            0,
            start,
            end,
            "FocusBase",
            positionOverride + EscapeCue(cue));
    }

    private static void WriteKaraoke(
        StringBuilder builder,
        StudioCaptionCue cue,
        TimeSpan shift,
        TimeSpan clipDuration,
        string positionOverride)
    {
        if (cue.WordSpans.Count == 0)
        {
            (TimeSpan start, TimeSpan end) = ClampPartitionedRange(
                cue.RelativeStart + shift,
                cue.RelativeEnd + shift,
                clipDuration);
            if (end > start)
            {
                int durationCentiseconds = Math.Max(
                    1,
                    checked((int)Math.Round(
                        (end - start).TotalMilliseconds / 10d)));
                WriteDialogue(
                    builder,
                    0,
                    start,
                    end,
                    "Karaoke",
                    positionOverride +
                    "{\\kf" +
                    durationCentiseconds.ToString(
                        CultureInfo.InvariantCulture) +
                    "}" + EscapeCue(cue));
            }
            return;
        }

        TimeSpan cueStart = cue.RelativeStart + shift;
        TimeSpan cueEnd = cue.RelativeEnd + shift;
        TimeSpan cursor = cueStart;
        for (int index = 0; index < cue.WordSpans.Count; index++)
        {
            StudioCaptionWordSpan span = cue.WordSpans[index];
            TimeSpan rawWordStart = span.Word.RelativeStart + shift;
            TimeSpan rawWordEnd = span.Word.RelativeEnd + shift;
            WriteKaraokeStaticInterval(
                builder,
                cue,
                cursor,
                rawWordStart,
                clipDuration,
                positionOverride,
                span.StartIndex);

            (TimeSpan wordStart, TimeSpan wordEnd) =
                ClampPartitionedRange(
                rawWordStart,
                rawWordEnd,
                clipDuration);
            if (wordEnd > wordStart)
            {
                int durationCentiseconds = Math.Max(
                    1,
                    checked((int)Math.Round(
                        (wordEnd - wordStart).TotalMilliseconds / 10d)));
                var text = new StringBuilder(positionOverride);
                text.Append("{\\c&H00FFFFFF&}");
                text.Append(EscapeSlice(cue, 0, span.StartIndex));
                text.Append("{\\1c&H005EC7FF&\\2c&H00A59E98&\\kf");
                text.Append(durationCentiseconds.ToString(
                    CultureInfo.InvariantCulture));
                text.Append("\\fscx112\\fscy112\\t(0,140,\\fscx105\\fscy105)}");
                text.Append(EscapeSlice(cue, span.StartIndex, span.Length));
                text.Append("{\\c&H00A59E98&\\fscx100\\fscy100}");
                text.Append(EscapeSlice(cue, span.StartIndex + span.Length, cue.Text.Length - span.StartIndex - span.Length));
                WriteDialogue(
                    builder,
                    0,
                    wordStart,
                    NonZeroEnd(wordStart, wordEnd),
                    "Karaoke",
                    text.ToString());
            }
            cursor = rawWordEnd;
        }
        WriteKaraokeStaticInterval(
            builder,
            cue,
            cursor,
            cueEnd,
            clipDuration,
            positionOverride,
            futureStartIndex: -1);
    }

    private static void WriteKaraokeStaticInterval(
        StringBuilder builder,
        StudioCaptionCue cue,
        TimeSpan rawStart,
        TimeSpan rawEnd,
        TimeSpan clipDuration,
        string positionOverride,
        int futureStartIndex)
    {
        (TimeSpan start, TimeSpan end) = ClampPartitionedRange(
            rawStart,
            rawEnd,
            clipDuration);
        if (end <= start)
        {
            return;
        }

        var text = new StringBuilder(positionOverride);
        if (futureStartIndex < 0)
        {
            text.Append("{\\c&H00FFFFFF&}");
            text.Append(EscapeCue(cue));
        }
        else
        {
            text.Append("{\\c&H00FFFFFF&}");
            text.Append(EscapeSlice(cue, 0, futureStartIndex));
            text.Append("{\\c&H00A59E98&}");
            text.Append(EscapeSlice(cue, futureStartIndex, cue.Text.Length - futureStartIndex));
        }
        WriteDialogue(
            builder,
            0,
            start,
            end,
            "Karaoke",
            text.ToString());
    }

    private static void WritePop(
        StringBuilder builder,
        StudioCaptionCue cue,
        TimeSpan shift,
        TimeSpan clipDuration,
        string positionOverride)
    {
        if (cue.WordSpans.Count == 0)
        {
            (TimeSpan start, TimeSpan end) = ClampPartitionedRange(
                cue.RelativeStart + shift,
                cue.RelativeEnd + shift,
                clipDuration);
            if (end > start)
            {
                WriteDialogue(
                    builder,
                    0,
                    start,
                    end,
                    "Pop",
                    positionOverride +
                    "{\\fscx82\\fscy82\\t(0,120,\\fscx112\\fscy112)" +
                    "\\t(120,260,\\fscx100\\fscy100)}" +
                    EscapeCue(cue));
            }
            return;
        }

        TimeSpan cueStart = cue.RelativeStart + shift;
        TimeSpan cueEnd = cue.RelativeEnd + shift;
        AudioTranscriptionWord firstWord = cue.Words[0];
        (TimeSpan leadStart, TimeSpan leadEnd) = ClampPartitionedRange(
            cueStart,
            firstWord.RelativeStart + shift,
            clipDuration);
        if (leadEnd > leadStart)
        {
            WriteDialogue(
                builder,
                0,
                leadStart,
                leadEnd,
                "Pop",
                positionOverride +
                "{\\fscx82\\fscy82}" +
                EscapeWord(firstWord, StudioCaptionDisplayText.SliceWordText(cue, cue.WordSpans[0])));
        }

        for (int index = 0; index < cue.Words.Count; index++)
        {
            AudioTranscriptionWord word = cue.Words[index];
            TimeSpan rawEnd = index + 1 < cue.Words.Count
                ? cue.Words[index + 1].RelativeStart + shift
                : cueEnd;
            (TimeSpan start, TimeSpan end) = ClampPartitionedRange(
                word.RelativeStart + shift,
                rawEnd,
                clipDuration);
            if (end <= start)
            {
                continue;
            }
            WriteDialogue(
                builder,
                0,
                start,
                end,
                "Pop",
                positionOverride +
                "{\\fscx82\\fscy82\\t(0,120,\\fscx112\\fscy112)" +
                "\\t(120,260,\\fscx100\\fscy100)}" +
                EscapeWord(word, StudioCaptionDisplayText.SliceWordText(cue, cue.WordSpans[index])));
        }
    }

    private static void WriteDialogue(
        StringBuilder builder,
        int layer,
        TimeSpan start,
        TimeSpan end,
        string style,
        string text)
    {
        if (end <= start)
        {
            return;
        }
        builder.Append("Dialogue: ");
        builder.Append(layer.ToString(CultureInfo.InvariantCulture));
        builder.Append(',');
        builder.Append(FormatTime(start));
        builder.Append(',');
        builder.Append(FormatTime(end));
        builder.Append(',');
        builder.Append(style);
        builder.Append(",,0,0,0,,");
        builder.AppendLine(text);
    }

    private static string FormatTime(TimeSpan value)
    {
        TimeSpan quantized =
            StudioCaptionPresentationPolicy.QuantizeRenderBoundary(value);
        long centiseconds = quantized.Ticks /
            (TimeSpan.TicksPerMillisecond * 10);
        centiseconds = Math.Max(0, centiseconds);
        long hours = centiseconds / 360000;
        long minutes = centiseconds / 6000 % 60;
        long seconds = centiseconds / 100 % 60;
        long fraction = centiseconds % 100;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours}:{minutes:00}:{seconds:00}.{fraction:00}");
    }

    private static TimeSpan NonZeroEnd(
        TimeSpan start,
        TimeSpan end) =>
        end > start
            ? end
            : start + TimeSpan.FromMilliseconds(10);

    private static (TimeSpan Start, TimeSpan End) ClampRange(
        TimeSpan start,
        TimeSpan end,
        TimeSpan duration) =>
        (
            start < TimeSpan.Zero ? TimeSpan.Zero : start,
            end > duration ? duration : end);

    private static (TimeSpan Start, TimeSpan End)
        ClampPartitionedRange(
            TimeSpan start,
            TimeSpan end,
            TimeSpan duration)
    {
        (TimeSpan clampedStart, TimeSpan clampedEnd) = ClampRange(
            start,
            end,
            duration);
        return (
            StudioCaptionPresentationPolicy.QuantizeRenderBoundary(
                clampedStart),
            StudioCaptionPresentationPolicy.QuantizeRenderBoundary(
                clampedEnd));
    }

    private static string EscapeCue(StudioCaptionCue cue) => EscapeSlice(cue, 0, cue.Text.Length);
    private static string EscapeSlice(StudioCaptionCue cue, int start, int length)
    {
        var text = new StringBuilder();
        int end = start + length;
        bool emphasized = false;
        for (int index = start; index < end; index++)
        {
            bool next = cue.WordSpans.Any(s => s.Word.IsEmphasized && index >= s.StartIndex && index < s.StartIndex + s.Length);
            if (next != emphasized) { text.Append(next ? "{\\u1}" : "{\\u0}"); emphasized = next; }
            text.Append(Escape(cue.Text[index].ToString()));
        }
        if (emphasized) text.Append("{\\u0}");
        return text.ToString();
    }
    private static string EscapeWord(AudioTranscriptionWord word, string displayedText) => word.IsEmphasized ? "{\\u1}" + Escape(displayedText) + "{\\u0}" : Escape(displayedText);

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal)
            .Replace("\r\n", "\\N", StringComparison.Ordinal)
            .Replace("\n", "\\N", StringComparison.Ordinal)
            .Replace("\r", "\\N", StringComparison.Ordinal);

}
