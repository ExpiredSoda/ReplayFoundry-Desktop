using System.Globalization;
using System.Text;
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
    public const string PolicyVersion = "1.5";

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
            StudioClipAppearance.DefaultCaptionFontScalePercent)
    {
        ArgumentNullException.ThrowIfNull(track);
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

        _ = StudioCaptionPresentationPolicy.GetMaximumVisibleWords(
            captionWordLimit);
        GenerationCaptionStylePreset effective =
            StudioCaptionPresentationPolicy.ResolveEffectiveStyle(track);
        StudioCaptionFrameLayout layout =
            StudioCaptionPresentationPolicy.CalculateFrameLayout(
                frameWidth,
                frameHeight,
                effective,
                captionMaximumWidthPercent,
                captionFontScalePercent);
        var warnings = new List<string>();
        if (StudioCaptionPresentationPolicy.RequiresTimedWords(effective) &&
            !StudioCaptionPresentationPolicy.HasCompleteTimedWordCoverage(track))
        {
            warnings.Add(
                "Exact word-by-word timing is not available for every caption. The selected effect remains active across each spoken phrase; no word boundaries were guessed.");
        }
        if (captionWordLimit != StudioCaptionWordLimitPreset.FullSegment &&
            !StudioCaptionPresentationPolicy.HasCompleteTimedWordCoverage(track))
        {
            warnings.Add(
                "The selected word window was not applied to every segment because its timed words do not completely match the retained caption text. Those segments remain whole rather than dropping or inventing words.");
        }

        var builder = new StringBuilder();
        WriteHeader(builder, frameWidth, frameHeight, layout);
        TimeSpan actualSourceStart =
            clipSourceStart ??
            track.SourceWindowStart;
        TimeSpan actualClipDuration =
            clipDuration ??
            track.SourceWindowDuration;
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
        string positionOverride = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\\an5\\pos({frameWidth / 2},{Math.Round(frameHeight * verticalPositionPercent / 100d)})}}");
        foreach (AudioTranscriptionSegment segment in track.Segments)
        {
            foreach (StudioCaptionCue cue in
                     StudioCaptionPresentationPolicy.ProjectCues(
                         segment,
                         captionWordLimit))
            {
                WriteCue(
                    builder,
                    cue,
                    effective,
                    relativeShift,
                    actualClipDuration,
                    positionOverride);
            }
        }

        return new AssSubtitleDocument(
            builder.ToString(),
            track.RequestedStyle,
            effective,
            warnings.AsReadOnly());
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
        builder.AppendLine(Style("Clean", fontSize, "&H00FFFFFF", "&H0000D7FF", "&H00101010", "&H78000000", 1, 4, 1, layout.HorizontalMarginPixels, bottomMargin));
        builder.AppendLine(Style("FocusBase", fontSize, "&H00B8B8B8", "&H005EC7FF", "&H00101010", "&H78000000", 1, 5, 1, layout.HorizontalMarginPixels, bottomMargin));
        builder.AppendLine(Style("Karaoke", fontSize, "&H0098928A", "&H005EC7FF", "&H00101010", "&H78000000", 1, 5, 1, layout.HorizontalMarginPixels, bottomMargin));
        builder.AppendLine(Style("Pop", checked((int)Math.Round(fontSize * 1.18)), "&H005EC7FF", "&H005EC7FF", "&H00101010", "&H78000000", 1, 6, 2, layout.HorizontalMarginPixels, bottomMargin));
        builder.AppendLine(Style("HighContrast", fontSize, "&H00FFFFFF", "&H005EC7FF", "&H00000000", "&H28000000", 3, 3, 0, layout.HorizontalMarginPixels, bottomMargin));
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
                    positionOverride + Escape(cue.Text));
                break;
            case GenerationCaptionStylePreset.HighContrast:
                WriteDialogue(
                    builder,
                    0,
                    start,
                    end,
                    "HighContrast",
                    positionOverride + Escape(cue.Text));
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
                    "{\\c&H0000D7FF&}" + Escape(cue.Text));
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
            text.Append(Escape(cue.Text[..span.StartIndex]));
            text.Append("{\\c&H005EC7FF&\\fscx108\\fscy108\\t(0,120,\\fscx103\\fscy103)}");
            text.Append(Escape(cue.Text.Substring(
                span.StartIndex,
                span.Length)));
            text.Append("{\\c&H00B8B8B8&\\fscx100\\fscy100}");
            text.Append(Escape(cue.Text[(span.StartIndex + span.Length)..]));
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
            positionOverride + Escape(cue.Text));
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
                    "}" + Escape(cue.Text));
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
                var text = new StringBuilder(positionOverride);
                text.Append("{\\c&H00FFFFFF&}");
                text.Append(Escape(cue.Text[..span.StartIndex]));
                text.Append("{\\c&H005EC7FF&\\fscx112\\fscy112\\t(0,140,\\fscx105\\fscy105)}");
                text.Append(Escape(cue.Text.Substring(
                    span.StartIndex,
                    span.Length)));
                text.Append("{\\c&H0098928A&\\fscx100\\fscy100}");
                text.Append(Escape(
                    cue.Text[(span.StartIndex + span.Length)..]));
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
            text.Append(Escape(cue.Text));
        }
        else
        {
            text.Append("{\\c&H00FFFFFF&}");
            text.Append(Escape(cue.Text[..futureStartIndex]));
            text.Append("{\\c&H0098928A&}");
            text.Append(Escape(cue.Text[futureStartIndex..]));
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
                    Escape(cue.Text));
            }
            return;
        }

        foreach (AudioTranscriptionWord word in cue.Words)
        {
            (TimeSpan start, TimeSpan end) = ClampRange(
                word.RelativeStart + shift,
                word.RelativeEnd + shift,
                clipDuration);
            if (end <= start) continue;
            WriteDialogue(
                builder,
                0,
                start,
                NonZeroEnd(start, end),
                "Pop",
                positionOverride +
                "{\\fscx82\\fscy82\\t(0,120,\\fscx112\\fscy112)" +
                "\\t(120,260,\\fscx100\\fscy100)}" +
                Escape(word.Text));
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

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal)
            .Replace("\r\n", "\\N", StringComparison.Ordinal)
            .Replace("\n", "\\N", StringComparison.Ordinal)
            .Replace("\r", "\\N", StringComparison.Ordinal);

}
