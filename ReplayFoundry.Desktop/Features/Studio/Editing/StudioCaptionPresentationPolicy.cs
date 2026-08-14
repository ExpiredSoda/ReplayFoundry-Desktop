using System.Text;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioCaptionWordSpan(
    AudioTranscriptionWord Word,
    int StartIndex,
    int Length);

public sealed record StudioCaptionCue(
    string Text,
    TimeSpan RelativeStart,
    TimeSpan RelativeEnd,
    TimeSpan AbsoluteSourceStart,
    TimeSpan AbsoluteSourceEnd,
    IReadOnlyList<StudioCaptionWordSpan> WordSpans)
{
    public IReadOnlyList<AudioTranscriptionWord> Words { get; } =
        Array.AsReadOnly(
            WordSpans.Select(static span => span.Word).ToArray());
}

public readonly record struct StudioCaptionFrameLayout(
    int BaseFontSizePixels,
    int EffectiveFontSizePixels,
    int MaximumWidthPixels,
    int HorizontalMarginPixels);

public static class StudioCaptionPresentationPolicy
{
    // ASS font sizes are typographic points at 72 units per inch. WPF text is
    // measured in device-independent pixels at 96 units per inch. Converting
    // the shared render size prevents the live Studio guide from wrapping at
    // a visibly larger size than libass uses in the final burned caption.
    private const double AssPointToWpfDip = 72d / 96d;
    private const int LegacyHorizontalMarginPixels = 48;
    private const long TicksPerAssCentisecond =
        TimeSpan.TicksPerMillisecond * 10;
    private static readonly TimeSpan TimedCueLead =
        TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan TimedCueTail =
        TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan MaximumVisibleInterWordSilence =
        TimeSpan.FromMilliseconds(600);

    public static TimeSpan QuantizeRenderBoundary(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        long centiseconds = checked((long)Math.Round(
            value.Ticks / (double)TicksPerAssCentisecond,
            MidpointRounding.AwayFromZero));
        return TimeSpan.FromTicks(
            checked(centiseconds * TicksPerAssCentisecond));
    }

    public static GenerationCaptionStylePreset ResolveEffectiveStyle(
        GenerationCandidateCaptionTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return ResolveEffectiveStyle(track, track.RequestedStyle);
    }

    public static GenerationCaptionStylePreset ResolveEffectiveStyle(
        GenerationCandidateCaptionTrack track,
        GenerationCaptionStylePreset requestedStyle)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!Enum.IsDefined(requestedStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedStyle));
        }
        // Word timing controls animation granularity, not whether the user's
        // selected visual treatment is honored. When only a phrase interval
        // is trustworthy, preview and render animate that complete phrase
        // instead of silently changing the requested effect to Clean.
        return requestedStyle;
    }

    public static bool RequiresTimedWords(
        GenerationCaptionStylePreset style) => style is
        GenerationCaptionStylePreset.WordFocus or
        GenerationCaptionStylePreset.KaraokeSweep or
        GenerationCaptionStylePreset.Pop;

    public static bool HasCompleteTimedWordCoverage(
        GenerationCandidateCaptionTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Segments.Count > 0 &&
            track.Segments.All(HasCompleteTimedWordCoverage);
    }

    public static string? GetPresentationWarning(
        GenerationCandidateCaptionTrack? track,
        StudioClipAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        if (track is null || HasCompleteTimedWordCoverage(track))
        {
            return null;
        }

        if (!RequiresTimedWords(appearance.CaptionStyle) &&
            appearance.CaptionWordLimit ==
                StudioCaptionWordLimitPreset.FullSegment)
        {
            return null;
        }

        return "Exact word-by-word timing is not available for every caption " +
            "in this clip. Your selected effect stays active and follows each " +
            "spoken phrase; Studio will not guess word boundaries.";
    }

    public static bool HasCompleteTimedWordCoverage(
        AudioTranscriptionSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Words.Count > 0 &&
            segment.Words.All(static word =>
                word.RelativeEnd > word.RelativeStart &&
                word.AbsoluteSourceEnd > word.AbsoluteSourceStart) &&
            TryCreateWordSpans(
            segment.Text,
            segment.Words,
            out _);
    }

    public static int? GetMaximumVisibleWords(
        StudioCaptionWordLimitPreset preset) => preset switch
        {
            StudioCaptionWordLimitPreset.FullSegment => null,
            StudioCaptionWordLimitPreset.Balanced => 8,
            StudioCaptionWordLimitPreset.Streamlined => 5,
            StudioCaptionWordLimitPreset.Punchy => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };

    public static IReadOnlyList<StudioCaptionCue> ProjectCues(
        AudioTranscriptionSegment segment,
        StudioCaptionWordLimitPreset preset)
    {
        ArgumentNullException.ThrowIfNull(segment);
        int? maximumWords = GetMaximumVisibleWords(preset);
        if (HasCompleteTimedWordCoverage(segment))
        {
            return ProjectTimedWords(
                segment,
                maximumWords ?? int.MaxValue);
        }

        // A user correction intentionally drops stale provider word timing.
        // Keep that segment whole instead of inventing new display boundaries.
        return Array.AsReadOnly(new[] { CreateCue(
            segment.Text,
            segment.RelativeStart,
            segment.RelativeEnd,
            segment.AbsoluteSourceStart,
            segment.AbsoluteSourceEnd,
            []) });
    }

    public static IReadOnlyList<StudioCaptionCue> ProjectCues(
        GenerationCandidateCaptionTrack track,
        StudioCaptionWordLimitPreset preset)
    {
        ArgumentNullException.ThrowIfNull(track);
        _ = GetMaximumVisibleWords(preset);
        return Array.AsReadOnly(
            track.Segments
                .SelectMany(segment => ProjectCues(segment, preset))
                .ToArray());
    }

    public static StudioCaptionCue? FindActiveCue(
        GenerationCandidateCaptionTrack track,
        StudioCaptionWordLimitPreset preset,
        TimeSpan absoluteSourcePosition)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (absoluteSourcePosition < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(absoluteSourcePosition));
        }

        return ProjectCues(track, preset).FirstOrDefault(
            cue =>
                cue.AbsoluteSourceStart <= absoluteSourcePosition &&
                cue.AbsoluteSourceEnd > absoluteSourcePosition);
    }

    public static StudioCaptionFrameLayout CalculateFrameLayout(
        int frameWidth,
        int frameHeight,
        GenerationCaptionStylePreset style,
        double maximumWidthPercent,
        double fontScalePercent)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth));
        }
        if (!Enum.IsDefined(style))
        {
            throw new ArgumentOutOfRangeException(nameof(style));
        }
        RequirePercent(
            maximumWidthPercent,
            StudioClipAppearance.MinimumCaptionMaximumWidthPercent,
            StudioClipAppearance.MaximumCaptionMaximumWidthPercent,
            nameof(maximumWidthPercent));
        RequirePercent(
            fontScalePercent,
            StudioClipAppearance.MinimumCaptionFontScalePercent,
            StudioClipAppearance.MaximumCaptionFontScalePercent,
            nameof(fontScalePercent));

        int legacyFontSize = Math.Max(
            28,
            checked((int)Math.Round(frameHeight * 0.047)));
        int baseFontSize = Math.Max(
            1,
            checked((int)Math.Round(
                legacyFontSize * fontScalePercent / 100d)));
        int effectiveFontSize = style == GenerationCaptionStylePreset.Pop
            ? checked((int)Math.Round(baseFontSize * 1.16))
            : baseFontSize;

        int legacyMargin = Math.Min(
            LegacyHorizontalMarginPixels,
            Math.Max(0, (frameWidth - 1) / 2));
        int legacyMaximumWidth = frameWidth - legacyMargin * 2;
        int requestedWidth = Math.Max(
            1,
            checked((int)Math.Round(
                legacyMaximumWidth * maximumWidthPercent / 100d)));
        int horizontalMargin = Math.Max(
            0,
            checked((int)Math.Round(
                (frameWidth - requestedWidth) / 2d)));
        int maximumWidth = Math.Max(1, frameWidth - horizontalMargin * 2);
        return new StudioCaptionFrameLayout(
            baseFontSize,
            effectiveFontSize,
            maximumWidth,
            horizontalMargin);
    }

    public static double GetWpfPreviewFontSize(
        StudioCaptionFrameLayout layout) =>
        layout.EffectiveFontSizePixels * AssPointToWpfDip;

    private static IReadOnlyList<StudioCaptionCue> ProjectTimedWords(
        AudioTranscriptionSegment segment,
        int maximumWords)
    {
        IReadOnlyList<StudioCaptionWordSpan> sourceSpans =
            CreateRequiredWordSpans(segment.Text, segment.Words);
        var runs = new List<(int Start, int Count)>();
        int runStart = 0;
        for (int index = 1; index < segment.Words.Count; index++)
        {
            TimeSpan silence = segment.Words[index].RelativeStart -
                segment.Words[index - 1].RelativeEnd;
            if (silence > MaximumVisibleInterWordSilence)
            {
                runs.Add((runStart, index - runStart));
                runStart = index;
            }
        }
        runs.Add((runStart, segment.Words.Count - runStart));

        var cues = new List<StudioCaptionCue>();
        foreach ((int speechRunStart, int speechRunCount) in runs)
        {
            int groupCount = checked(
                1 + (speechRunCount - 1) / maximumWords);
            int baseGroupSize = speechRunCount / groupCount;
            int largerGroupCount = speechRunCount % groupCount;
            int startIndex = speechRunStart;
            for (int groupIndex = 0;
                 groupIndex < groupCount;
                 groupIndex++)
            {
                int count = baseGroupSize +
                    (groupIndex < largerGroupCount ? 1 : 0);
                int nextIndex = startIndex + count;
                AudioTranscriptionWord[] words = segment.Words
                    .Skip(startIndex)
                    .Take(count)
                    .ToArray();
                int textStart = startIndex == 0
                    ? 0
                    : sourceSpans[startIndex].StartIndex;
                int textEnd = nextIndex < segment.Words.Count
                    ? sourceSpans[nextIndex].StartIndex
                    : segment.Text.Length;
                string groupText = segment.Text[textStart..textEnd].Trim();
                cues.Add(CreateTimedCue(
                    segment,
                    groupText,
                    words));
                startIndex = nextIndex;
            }
        }

        for (int index = 0; index + 1 < cues.Count; index++)
        {
            StudioCaptionCue current = cues[index];
            StudioCaptionCue next = cues[index + 1];
            if (current.RelativeEnd <= next.RelativeStart)
            {
                continue;
            }
            cues[index] = current with
            {
                RelativeEnd = next.RelativeStart,
                AbsoluteSourceEnd = next.AbsoluteSourceStart,
            };
        }
        return cues.AsReadOnly();
    }

    private static StudioCaptionCue CreateTimedCue(
        AudioTranscriptionSegment segment,
        string text,
        IReadOnlyList<AudioTranscriptionWord> words)
    {
        AudioTranscriptionWord first = words[0];
        AudioTranscriptionWord last = words[^1];
        TimeSpan relativeStart = Max(
            segment.RelativeStart,
            first.RelativeStart - TimedCueLead);
        TimeSpan relativeEnd = Min(
            segment.RelativeEnd,
            last.RelativeEnd + TimedCueTail);
        TimeSpan absoluteOffset =
            segment.AbsoluteSourceStart - segment.RelativeStart;
        return CreateCue(
            text,
            relativeStart,
            relativeEnd,
            absoluteOffset + relativeStart,
            absoluteOffset + relativeEnd,
            words);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

    private static StudioCaptionCue CreateCue(
        string text,
        TimeSpan relativeStart,
        TimeSpan relativeEnd,
        TimeSpan absoluteSourceStart,
        TimeSpan absoluteSourceEnd,
        IEnumerable<AudioTranscriptionWord> words)
    {
        AudioTranscriptionWord[] snapshot = words.ToArray();
        IReadOnlyList<StudioCaptionWordSpan> spans =
            snapshot.Length > 0 &&
            TryCreateWordSpans(text, snapshot, out var mappedSpans)
                ? mappedSpans
                : Array.AsReadOnly(
                    Array.Empty<StudioCaptionWordSpan>());
        return new StudioCaptionCue(
            text,
            relativeStart,
            relativeEnd,
            absoluteSourceStart,
            absoluteSourceEnd,
            spans);
    }

    private static IReadOnlyList<StudioCaptionWordSpan>
        CreateRequiredWordSpans(
            string text,
            IReadOnlyList<AudioTranscriptionWord> words)
    {
        if (!TryCreateWordSpans(text, words, out var spans))
        {
            throw new InvalidOperationException(
                "Timed caption words must map exactly onto the retained caption text.");
        }
        return spans;
    }

    private static bool TryCreateWordSpans(
        string text,
        IReadOnlyList<AudioTranscriptionWord> words,
        out IReadOnlyList<StudioCaptionWordSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(words);
        spans = Array.AsReadOnly(Array.Empty<StudioCaptionWordSpan>());
        if (words.Count == 0)
        {
            return false;
        }

        IReadOnlyList<(char Value, int SourceIndex)> sourceCharacters =
            BuildLexicalCharacters(text);
        if (sourceCharacters.Count == 0)
        {
            return false;
        }

        var wordCharacters = new List<IReadOnlyList<char>>(words.Count);
        foreach (AudioTranscriptionWord word in words)
        {
            char[] lexical = BuildLexicalCharacters(word.Text)
                .Select(static item => item.Value)
                .ToArray();
            if (lexical.Length == 0)
            {
                return false;
            }
            wordCharacters.Add(Array.AsReadOnly(lexical));
        }

        char[] expected = wordCharacters.SelectMany(static value => value)
            .ToArray();
        if (expected.Length != sourceCharacters.Count)
        {
            return false;
        }
        for (int index = 0; index < expected.Length; index++)
        {
            if (expected[index] != sourceCharacters[index].Value)
            {
                return false;
            }
        }

        var result = new StudioCaptionWordSpan[words.Count];
        int lexicalOffset = 0;
        for (int index = 0; index < words.Count; index++)
        {
            int lexicalLength = wordCharacters[index].Count;
            int start = sourceCharacters[lexicalOffset].SourceIndex;
            int end = sourceCharacters[
                lexicalOffset + lexicalLength - 1].SourceIndex + 1;
            result[index] = new StudioCaptionWordSpan(
                words[index],
                start,
                end - start);
            lexicalOffset += lexicalLength;
        }
        spans = Array.AsReadOnly(result);
        return true;
    }

    private static IReadOnlyList<(char Value, int SourceIndex)>
        BuildLexicalCharacters(string value)
    {
        var result = new List<(char Value, int SourceIndex)>();
        for (int sourceIndex = 0;
             sourceIndex < value.Length;
             sourceIndex++)
        {
            string normalized = value[sourceIndex]
                .ToString()
                .Normalize(NormalizationForm.FormKC);
            foreach (char character in normalized)
            {
                if (char.IsLetterOrDigit(character))
                {
                    result.Add((
                        char.ToUpperInvariant(character),
                        sourceIndex));
                }
            }
        }
        return result.AsReadOnly();
    }

    private static void RequirePercent(
        double value,
        double minimum,
        double maximum,
        string parameterName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
