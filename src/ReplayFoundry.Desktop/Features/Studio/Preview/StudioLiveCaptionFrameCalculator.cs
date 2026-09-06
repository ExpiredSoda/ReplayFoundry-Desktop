using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

internal sealed class StudioLiveCaptionFrameCalculator
{
    private GenerationCandidateCaptionTrack? _projectedTrack;
    private StudioCaptionWordLimitPreset? _projectedWordLimit;
    private IReadOnlyList<StudioCaptionCue> _projectedCues = [];
    private GenerationCandidateCaptionTrack? _sourceTrack;
    private GenerationCandidateCaptionTrack? _cutTrack;
    private TimeSpan _cutStart;
    private TimeSpan _cutEnd;

    public void Reset()
    {
        _projectedTrack = null;
        _projectedWordLimit = null;
        _projectedCues = [];
        _sourceTrack = null;
        _cutTrack = null;
    }

    public LiveCaptionFrameState Calculate(
        GenerationCandidateCaptionTrack? captions,
        StudioCaptionWordLimitPreset wordLimit,
        GenerationCaptionStylePreset style,
        double position,
        TimeSpan rangeStart,
        TimeSpan rangeEnd)
    {
        if (!ReferenceEquals(_sourceTrack, captions) || _cutStart != rangeStart || _cutEnd != rangeEnd)
        {
            _sourceTrack = captions;
            _cutStart = rangeStart;
            _cutEnd = rangeEnd;
            _cutTrack = captions is null ? null : StudioCaptionCutProjection.Project(captions, rangeStart, rangeEnd).Track;
        }
        captions = _cutTrack;
        string? secondary = captions?.Segments.FirstOrDefault(s =>
            Quantize(s.AbsoluteSourceStart.TotalSeconds, rangeStart) <= position &&
            Quantize(s.AbsoluteSourceEnd.TotalSeconds, rangeStart) > position)?.SecondaryText;
        StudioCaptionCue? cue = FindCue(
            captions,
            wordLimit,
            style,
            position,
            rangeStart);
        if (cue is null)
        {
            return LiveCaptionFrameState.Empty with { SecondaryText = secondary };
        }

        StudioCaptionWordSpan? active =
            style == GenerationCaptionStylePreset.Pop
                ? FindPopDisplayWord(
                    cue,
                    position,
                    rangeStart)
                : FindActiveWord(
                    cue,
                    position,
                    rangeStart);
        string? activeWord = active is null ? null : style == GenerationCaptionStylePreset.Pop
            ? StudioCaptionDisplayText.SliceWordText(cue, active) : active.Word.Text;
        (int start, int length, int sweep, double progress) = FindAccent(
            cue,
            style,
            position,
            rangeStart,
            rangeEnd);
        return new LiveCaptionFrameState(
            style == GenerationCaptionStylePreset.Pop &&
            cue.WordSpans.Count > 0
                ? activeWord
                : cue.Text,
            style is GenerationCaptionStylePreset.WordFocus or
                GenerationCaptionStylePreset.KaraokeSweep
                    ? activeWord
                    : null,
            start,
            length,
            sweep,
            progress,
            FindScale(cue, style, active, position, rangeStart),
            style == GenerationCaptionStylePreset.Pop
                ? active?.Word.IsEmphasized == true ? [new StudioCaptionWordSpan(active.Word, 0, activeWord!.Length)] : []
                : cue.WordSpans.Where(s => s.Word.IsEmphasized).ToArray(), secondary);
    }

    private StudioCaptionCue? FindCue(
        GenerationCandidateCaptionTrack? captions,
        StudioCaptionWordLimitPreset wordLimit,
        GenerationCaptionStylePreset style,
        double position,
        TimeSpan rangeStart)
    {
        if (captions is null)
        {
            return null;
        }
        StudioCaptionWordLimitPreset effectiveWordLimit =
            StudioCaptionPresentationPolicy.ResolveEffectiveWordLimit(
                style,
                wordLimit);
        if (!ReferenceEquals(_projectedTrack, captions) ||
            _projectedWordLimit != effectiveWordLimit)
        {
            _projectedTrack = captions;
            _projectedWordLimit = effectiveWordLimit;
            _projectedCues = StudioCaptionPresentationPolicy.ProjectCues(
                captions,
                effectiveWordLimit);
        }
        return _projectedCues.FirstOrDefault(cue =>
            Quantize(cue.AbsoluteSourceStart.TotalSeconds, rangeStart) <=
                position &&
            Quantize(cue.AbsoluteSourceEnd.TotalSeconds, rangeStart) >
                position);
    }

    private static StudioCaptionWordSpan? FindActiveWord(
        StudioCaptionCue cue,
        double position,
        TimeSpan rangeStart) =>
        cue.WordSpans.FirstOrDefault(span =>
            Quantize(
                span.Word.AbsoluteSourceStart.TotalSeconds,
                rangeStart) <= position &&
            Quantize(
                span.Word.AbsoluteSourceEnd.TotalSeconds,
                rangeStart) > position);

    private static StudioCaptionWordSpan? FindPopDisplayWord(
        StudioCaptionCue cue,
        double position,
        TimeSpan rangeStart)
    {
        StudioCaptionWordSpan? visible = null;
        foreach (StudioCaptionWordSpan span in cue.WordSpans)
        {
            double start = Quantize(
                span.Word.AbsoluteSourceStart.TotalSeconds,
                rangeStart);
            if (start > position)
            {
                break;
            }
            visible = span;
        }
        return visible ?? cue.WordSpans.FirstOrDefault();
    }

    private static (int Start, int Length, int Sweep, double Progress)
        FindAccent(
            StudioCaptionCue cue,
            GenerationCaptionStylePreset style,
            double position,
            TimeSpan rangeStart,
            TimeSpan rangeEnd)
    {
        if (style is not
            (GenerationCaptionStylePreset.WordFocus or
             GenerationCaptionStylePreset.KaraokeSweep))
        {
            return (-1, 0, 0, 0);
        }
        if (cue.WordSpans.Count == 0)
        {
            double start = Quantize(
                cue.AbsoluteSourceStart.TotalSeconds,
                rangeStart);
            double end = Quantize(
                cue.AbsoluteSourceEnd.TotalSeconds,
                rangeStart);
            if (end <= start)
            {
                return (-1, 0, 0, 0);
            }
            double progress = Math.Clamp(
                (position - start) / (end - start),
                0,
                1);
            return (0, cue.Text.Length, cue.Text.Length, progress);
        }

        foreach (StudioCaptionWordSpan span in cue.WordSpans)
        {
            double start = Math.Max(
                Quantize(
                    span.Word.AbsoluteSourceStart.TotalSeconds,
                    rangeStart),
                rangeStart.TotalSeconds);
            double end = Math.Min(
                Quantize(
                    span.Word.AbsoluteSourceEnd.TotalSeconds,
                    rangeStart),
                rangeEnd.TotalSeconds);
            if (end <= start)
            {
                continue;
            }
            if (style == GenerationCaptionStylePreset.WordFocus)
            {
                if (start <= position && end > position)
                {
                    double progress = Math.Clamp(
                        (position - start) / Math.Max(0.001, end - start),
                        0,
                        1);
                    return (
                        span.StartIndex,
                        span.Length,
                        span.Length,
                        progress);
                }
                continue;
            }
            if (position < start)
            {
                return (span.StartIndex, 0, span.Length, 0);
            }
            if (end > position)
            {
                double progress = Math.Clamp(
                    (position - start) / Math.Max(0.001, end - start),
                    0,
                    1);
                return (
                    span.StartIndex,
                    span.Length,
                    span.Length,
                    progress);
            }
        }
        return style == GenerationCaptionStylePreset.KaraokeSweep
            ? (cue.Text.Length, 0, 0, 1)
            : (-1, 0, 0, 0);
    }

    private static double FindScale(
        StudioCaptionCue cue,
        GenerationCaptionStylePreset style,
        StudioCaptionWordSpan? active,
        double position,
        TimeSpan rangeStart)
    {
        if (style != GenerationCaptionStylePreset.Pop ||
            active is null && cue.WordSpans.Count > 0)
        {
            return 1;
        }
        TimeSpan activeStart = active is null
            ? cue.AbsoluteSourceStart
            : active.Word.AbsoluteSourceStart;
        double visibleStart = Math.Max(
            Quantize(activeStart.TotalSeconds, rangeStart),
            rangeStart.TotalSeconds);
        double milliseconds = Math.Max(
            0,
            (position - visibleStart) * 1000d);
        if (milliseconds <= 120)
        {
            return 0.82 + (1.12 - 0.82) * milliseconds / 120d;
        }
        return milliseconds <= 260
            ? 1.12 + (1 - 1.12) * (milliseconds - 120d) / 140d
            : 1;
    }

    private static double Quantize(
        double absoluteSourceSeconds,
        TimeSpan rangeStart)
    {
        double relative = Math.Max(
            0,
            absoluteSourceSeconds - rangeStart.TotalSeconds);
        TimeSpan quantized =
            StudioCaptionPresentationPolicy.QuantizeRenderBoundary(
                TimeSpan.FromSeconds(relative));
        return rangeStart.TotalSeconds + quantized.TotalSeconds;
    }
}

internal readonly record struct LiveCaptionFrameState(
    string? Text,
    string? ActiveWord,
    int AccentStart,
    int AccentLength,
    int SweepLength,
    double AccentProgress,
    double Scale,
    IReadOnlyList<StudioCaptionWordSpan>? EmphasisSpans = null,
    string? SecondaryText = null)
{
    internal static LiveCaptionFrameState Empty { get; } = new(
        null,
        null,
        -1,
        0,
        0,
        0,
        1);
}
