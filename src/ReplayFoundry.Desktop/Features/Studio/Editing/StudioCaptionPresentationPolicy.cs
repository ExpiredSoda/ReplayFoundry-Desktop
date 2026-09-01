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
    private static readonly TimeSpan MaximumVisibleInterWordSilence =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MinimumLocalizedFallbackDuration =
        TimeSpan.FromMilliseconds(10);

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

        StudioCaptionWordLimitPreset effectiveWordLimit =
            ResolveEffectiveWordLimit(
                appearance.CaptionStyle,
                appearance.CaptionWordLimit);
        if (!RequiresTimedWords(appearance.CaptionStyle) &&
            effectiveWordLimit ==
                StudioCaptionWordLimitPreset.FullSegment)
        {
            return null;
        }

        return "Word timing could not be aligned reliably for every caption " +
            "in this clip. Your selected effect will use each whole phrase " +
            "instead of following individual spoken words.";
    }

    public static bool HasCompleteTimedWordCoverage(
        AudioTranscriptionSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Words.Count > 0 &&
            segment.Words.All(HasRenderableProviderTiming) &&
            TryCreateWordSpans(
                segment.Text,
                segment.Words,
                out _);
    }

    public static bool HasPresentationTimedWordCoverage(
        GenerationCandidateCaptionTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Segments.Count > 0 &&
            track.Segments.All(HasPresentationTimedWordCoverage);
    }

    public static bool HasPresentationTimedWordCoverage(
        AudioTranscriptionSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return HasCompleteTimedWordCoverage(segment);
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

    public static StudioCaptionWordLimitPreset ResolveEffectiveWordLimit(
        GenerationCaptionStylePreset style,
        StudioCaptionWordLimitPreset requestedWordLimit)
    {
        if (!Enum.IsDefined(style))
        {
            throw new ArgumentOutOfRangeException(nameof(style));
        }
        _ = GetMaximumVisibleWords(requestedWordLimit);

        // Pop is intentionally a one-word treatment. Splitting its source cue
        // into otherwise invisible 3/5/8-word pages only changes when the next
        // word is preloaded, so the control would not mean "words shown at
        // once." Keep one speech-run cue and let Pop's timed-word renderer own
        // every visible transition.
        return style == GenerationCaptionStylePreset.Pop
            ? StudioCaptionWordLimitPreset.FullSegment
            : requestedWordLimit;
    }

    public static IReadOnlyList<StudioCaptionCue> ProjectCues(
        AudioTranscriptionSegment segment,
        StudioCaptionWordLimitPreset preset)
    {
        ArgumentNullException.ThrowIfNull(segment);
        int? maximumWords = GetMaximumVisibleWords(preset);
        IReadOnlyList<AudioTranscriptionWord> words = segment.Words;
        if (words.Count == 0 ||
            !TryCreateWordSpans(
                segment.Text,
                words,
                out IReadOnlyList<StudioCaptionWordSpan> sourceSpans))
        {
            return WholeSegmentPhrase(segment);
        }

        CaptionSpeechRun[] runs = CreateSpeechRuns(words);
        if (!words.Any(HasRenderableProviderTiming))
        {
            // A text correction intentionally drops every stale provider word
            // timestamp. With no measured speech envelope to retain, keep the
            // provider's complete phrase interval rather than inventing one.
            return WholeSegmentPhrase(segment);
        }

        var cues = new List<StudioCaptionCue>();
        foreach (CaptionSpeechRun run in runs)
        {
            foreach (CaptionPresentationFragment fragment in
                     CreatePresentationFragments(run, words))
            {
                AudioTranscriptionWord[] fragmentWords = words
                    .Skip(fragment.StartIndex)
                    .Take(fragment.WordCount)
                    .ToArray();
                int nextIndex = fragment.StartIndex +
                    fragment.WordCount;
                int textStart = fragment.StartIndex == 0
                    ? 0
                    : sourceSpans[fragment.StartIndex].StartIndex;
                int textEnd = nextIndex < words.Count
                    ? sourceSpans[nextIndex].StartIndex
                    : segment.Text.Length;
                string fragmentText = segment.Text[textStart..textEnd]
                    .Trim();
                if (!fragment.RequiresPhraseFallback)
                {
                    cues.AddRange(ProjectTimedWords(
                        segment,
                        fragmentText,
                        fragmentWords,
                        maximumWords ?? int.MaxValue));
                    continue;
                }

                // A bad provider timestamp has no truthful karaoke interval.
                // Pair only its local cluster with one measured neighbor so
                // the phrase has a real speech envelope, while every valid
                // remainder keeps its timed-word animation and word limit.
                cues.Add(CreateLocalizedPhraseCue(
                    segment,
                    fragmentText,
                    fragmentWords));
            }
        }
        return cues.AsReadOnly();
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
        string text,
        IReadOnlyList<AudioTranscriptionWord> words,
        int maximumWords)
    {
        IReadOnlyList<StudioCaptionWordSpan> sourceSpans =
            CreateRequiredWordSpans(text, words);

        var cues = new List<StudioCaptionCue>();
        int groupCount = checked(
            1 + (words.Count - 1) / maximumWords);
        int baseGroupSize = words.Count / groupCount;
        int largerGroupCount = words.Count % groupCount;
        int startIndex = 0;
        for (int groupIndex = 0;
             groupIndex < groupCount;
             groupIndex++)
        {
            int count = baseGroupSize +
                (groupIndex < largerGroupCount ? 1 : 0);
            int nextIndex = startIndex + count;
            AudioTranscriptionWord[] cueWords = words
                .Skip(startIndex)
                .Take(count)
                .ToArray();
            int textStart = startIndex == 0
                ? 0
                : sourceSpans[startIndex].StartIndex;
            int textEnd = nextIndex < words.Count
                ? sourceSpans[nextIndex].StartIndex
                : text.Length;
            string groupText = text[textStart..textEnd].Trim();
            cues.Add(CreateTimedCue(
                segment,
                groupText,
                cueWords));
            startIndex = nextIndex;
        }

        for (int index = 0; index + 1 < cues.Count; index++)
        {
            StudioCaptionCue current = cues[index];
            StudioCaptionCue next = cues[index + 1];
            AudioTranscriptionWord currentLast =
                current.WordSpans[^1].Word;
            AudioTranscriptionWord nextFirst =
                next.WordSpans[0].Word;
            TimeSpan silence =
                nextFirst.RelativeStart - currentLast.RelativeEnd;
            if (silence <= MaximumVisibleInterWordSilence &&
                current.RelativeEnd < next.RelativeStart)
            {
                cues[index] = current with
                {
                    RelativeEnd = next.RelativeStart,
                    AbsoluteSourceEnd = next.AbsoluteSourceStart,
                };
                continue;
            }
            if (current.RelativeEnd > next.RelativeStart)
            {
                cues[index] = current with
                {
                    RelativeEnd = next.RelativeStart,
                    AbsoluteSourceEnd = next.AbsoluteSourceStart,
                };
            }
        }
        return cues.AsReadOnly();
    }

    private static CaptionSpeechRun[] CreateSpeechRuns(
        IReadOnlyList<AudioTranscriptionWord> words)
    {
        var runs = new List<CaptionSpeechRun>();
        int runStart = 0;
        for (int index = 1; index < words.Count; index++)
        {
            TimeSpan silence = words[index].RelativeStart -
                words[index - 1].RelativeEnd;
            if (silence <= MaximumVisibleInterWordSilence)
            {
                continue;
            }
            runs.Add(new CaptionSpeechRun(
                runStart,
                index - runStart));
            runStart = index;
        }
        runs.Add(new CaptionSpeechRun(
            runStart,
            words.Count - runStart));
        return runs.ToArray();
    }

    private static CaptionPresentationFragment[]
        CreatePresentationFragments(
            CaptionSpeechRun run,
            IReadOnlyList<AudioTranscriptionWord> words)
    {
        int runEnd = run.StartIndex + run.WordCount;
        if (words
            .Skip(run.StartIndex)
            .Take(run.WordCount)
            .All(HasRenderableProviderTiming))
        {
            return
            [
                new CaptionPresentationFragment(
                    run.StartIndex,
                    run.WordCount,
                    RequiresPhraseFallback: false),
            ];
        }
        if (!words
            .Skip(run.StartIndex)
            .Take(run.WordCount)
            .Any(HasRenderableProviderTiming))
        {
            // There is no measured neighbor from which to derive a smaller
            // truthful envelope. Retain the complete local speech run.
            return
            [
                new CaptionPresentationFragment(
                    run.StartIndex,
                    run.WordCount,
                    RequiresPhraseFallback: true),
            ];
        }

        var fragments = new List<CaptionPresentationFragment>();
        int index = run.StartIndex;
        while (index < runEnd)
        {
            if (HasRenderableProviderTiming(words[index]))
            {
                int validStart = index;
                while (index < runEnd &&
                       HasRenderableProviderTiming(words[index]))
                {
                    index++;
                }
                fragments.Add(new CaptionPresentationFragment(
                    validStart,
                    index - validStart,
                    RequiresPhraseFallback: false));
                continue;
            }

            int fallbackStart = index;
            while (index < runEnd &&
                   !HasRenderableProviderTiming(words[index]))
            {
                index++;
            }
            int fallbackEnd = index;
            if (fallbackEnd < runEnd)
            {
                // Prefer the following timed word. This keeps an invalid word
                // at a run's leading edge (the real Whisper failure pattern)
                // from downgrading every later word in the run.
                fallbackEnd++;
                index = fallbackEnd;
            }
            else if (fragments.Count > 0 &&
                     !fragments[^1].RequiresPhraseFallback)
            {
                // A trailing invalid cluster has no following envelope. Move
                // only the preceding timed word into its localized phrase.
                CaptionPresentationFragment previous = fragments[^1];
                fallbackStart--;
                if (previous.WordCount == 1)
                {
                    fragments.RemoveAt(fragments.Count - 1);
                }
                else
                {
                    fragments[^1] = previous with
                    {
                        WordCount = previous.WordCount - 1,
                    };
                }
            }

            if (fragments.Count > 0 &&
                fragments[^1].RequiresPhraseFallback &&
                fragments[^1].StartIndex + fragments[^1].WordCount ==
                    fallbackStart)
            {
                CaptionPresentationFragment previous = fragments[^1];
                fragments[^1] = previous with
                {
                    WordCount = fallbackEnd - previous.StartIndex,
                };
                continue;
            }
            fragments.Add(new CaptionPresentationFragment(
                fallbackStart,
                fallbackEnd - fallbackStart,
                RequiresPhraseFallback: true));
        }
        return fragments.ToArray();
    }

    private static bool HasRenderableProviderTiming(
        AudioTranscriptionWord word) =>
        word.RelativeEnd > word.RelativeStart &&
        word.AbsoluteSourceEnd > word.AbsoluteSourceStart &&
        QuantizeRenderBoundary(word.RelativeEnd) >
            QuantizeRenderBoundary(word.RelativeStart);

    private static IReadOnlyList<StudioCaptionCue> WholeSegmentPhrase(
        AudioTranscriptionSegment segment) =>
        Array.AsReadOnly(new[] { CreateCue(
            segment.Text,
            segment.RelativeStart,
            segment.RelativeEnd,
            segment.AbsoluteSourceStart,
            segment.AbsoluteSourceEnd,
            []) });

    private static StudioCaptionCue CreateLocalizedPhraseCue(
        AudioTranscriptionSegment segment,
        string text,
        IReadOnlyList<AudioTranscriptionWord> words)
    {
        TimeSpan relativeStart = Max(
            segment.RelativeStart,
            words[0].RelativeStart);
        TimeSpan relativeEnd = Min(
            segment.RelativeEnd,
            words.Max(static word => word.RelativeEnd));
        if (relativeEnd <= relativeStart)
        {
            // A provider point interval carries ordering and position but no
            // renderable duration. Give only that localized point the minimum
            // ASS interval; never expose it as made-up word-level timing.
            relativeEnd = Min(
                segment.RelativeEnd,
                relativeStart + MinimumLocalizedFallbackDuration);
            if (relativeEnd <= relativeStart)
            {
                relativeStart = Max(
                    segment.RelativeStart,
                    relativeEnd - MinimumLocalizedFallbackDuration);
            }
        }

        TimeSpan absoluteOffset =
            segment.AbsoluteSourceStart - segment.RelativeStart;
        return CreateCue(
            text,
            relativeStart,
            relativeEnd,
            absoluteOffset + relativeStart,
            absoluteOffset + relativeEnd,
            []);
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
            first.RelativeStart);
        TimeSpan relativeEnd = Min(
            segment.RelativeEnd,
            last.RelativeEnd);
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

    private readonly record struct CaptionSpeechRun(
        int StartIndex,
        int WordCount);

    private readonly record struct CaptionPresentationFragment(
        int StartIndex,
        int WordCount,
        bool RequiresPhraseFallback);
}
