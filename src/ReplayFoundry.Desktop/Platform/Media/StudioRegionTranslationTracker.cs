using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed record StudioTrackingSample(TimeSpan SourcePosition, StudioCropTrackingState State,
    double? MatchScore, double? AmbiguityMargin, NormalizedRectangle? Feature, NormalizedRectangle? Viewport)
{
    public override string ToString() => $"{SourcePosition:hh\\:mm\\:ss\\.fff} · {State}" +
        (MatchScore is double score ? $" · match {score:F2}" : "");
}

internal sealed record StudioTranslationAnalysis(IReadOnlyList<StudioTrackingSample> Samples,
    IReadOnlyList<StudioCropTrackingRun> Runs, string Summary);

/// <summary>Fixed-template translation only; score and ambiguity margin are similarities, not probabilities.</summary>
internal static class StudioRegionTranslationTracker
{
    internal const int FramesPerSecond = 8;
    internal const int MaximumFrames = 480;
    internal const int MaximumLongEdge = 320;
    internal const int MaximumBytes = 64 * 1024 * 1024;

    internal static StudioTranslationAnalysis Analyze(IReadOnlyList<byte[]> frames, int width, int height,
        TimeSpan sourceStart, NormalizedRectangle seed, NormalizedRectangle bounds,
        NormalizedRectangle initialViewport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (width < 8 || height < 8 || Math.Max(width, height) > MaximumLongEdge || frames.Count is < 2 or > MaximumFrames ||
            (long)frames.Count * width * height > MaximumBytes || frames.Any(frame => frame.Length != width * height) ||
            !bounds.Contains(seed) || !bounds.Contains(initialViewport))
            throw new ArgumentException("Tracking requires 2–480 grayscale frames, at most 320 pixels on the long edge, and a seed inside the allowed region.");
        int patchW = (int)Math.Round(seed.Width * width), patchH = (int)Math.Round(seed.Height * height);
        if (patchW < 8 || patchH < 8 || patchW > 96 || patchH > 96)
            throw new ArgumentException("Choose a textured seed between 8 and 96 analysis pixels wide and high.", nameof(seed));
        int seedX = Math.Clamp((int)Math.Round(seed.X * width), 0, width - patchW);
        int seedY = Math.Clamp((int)Math.Round(seed.Y * height), 0, height - patchH);
        int minX = (int)Math.Ceiling(bounds.X * width), minY = (int)Math.Ceiling(bounds.Y * height);
        int maxX = (int)Math.Floor(bounds.Right * width) - patchW, maxY = (int)Math.Floor(bounds.Bottom * height) - patchH;
        if (seedX < minX || seedX > maxX || seedY < minY || seedY > maxY)
            throw new ArgumentException("Keep the complete seed inside the allowed gameplay region.", nameof(seed));
        int gridW = Math.Min(12, patchW), gridH = Math.Min(12, patchH);
        var offsets = new int[gridW * gridH]; var template = new double[offsets.Length];
        for (int y = 0; y < gridH; y++)
            for (int x = 0; x < gridW; x++)
            {
                int i = y * gridW + x;
                offsets[i] = (int)Math.Round(y * (patchH - 1d) / (gridH - 1)) * width +
                    (int)Math.Round(x * (patchW - 1d) / (gridW - 1));
                template[i] = frames[0][seedY * width + seedX + offsets[i]];
            }
        double mean = template.Average(), energy = 0;
        for (int i = 0; i < template.Length; i++) { template[i] -= mean; energy += template[i] * template[i]; }
        var samples = new List<StudioTrackingSample>(frames.Count);
        StudioCropTrackingState stopped = energy / template.Length < 64 ? StudioCropTrackingState.Uninformative : StudioCropTrackingState.Tracked;
        int previousX = seedX, previousY = seedY;
        for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan time = sourceStart + TimeSpan.FromSeconds(frameIndex / (double)FramesPerSecond);
            double? score = null, margin = null;
            if (frameIndex > 0 && stopped == StudioCropTrackingState.Tracked)
            {
                if (HistogramDistance(frames[frameIndex - 1], frames[frameIndex]) > .65)
                    stopped = StudioCropTrackingState.SceneChanged;
                else
                {
                    int left = Math.Max(minX, previousX - 24), right = Math.Min(maxX, previousX + 24);
                    int top = Math.Max(minY, previousY - 24), bottom = Math.Min(maxY, previousY + 24);
                    var matches = new List<(int X, int Y, double Score)>((right - left + 1) * (bottom - top + 1));
                    (int X, int Y, double Score) best = (previousX, previousY, -1);
                    for (int y = top; y <= bottom; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = left; x <= right; x++)
                        {
                            double correlation = Correlate(frames[frameIndex], y * width + x, offsets, template, energy);
                            var match = (x, y, correlation); matches.Add(match);
                            if (correlation > best.Score) best = match;
                        }
                    }
                    double second = -1;
                    int exclusion = Math.Max(3, Math.Min(patchW, patchH) / 3);
                    foreach (var match in matches)
                        if (Math.Abs(match.X - best.X) > exclusion || Math.Abs(match.Y - best.Y) > exclusion)
                            second = Math.Max(second, match.Score);
                    score = best.Score; margin = best.Score - second;
                    if (best.Score < .78) stopped = StudioCropTrackingState.Lost;
                    else if (margin < .10) stopped = StudioCropTrackingState.Ambiguous;
                    else if (best.X == left && left > minX || best.X == right && right < maxX ||
                        best.Y == top && top > minY || best.Y == bottom && bottom < maxY)
                        stopped = StudioCropTrackingState.Lost;
                    else { previousX = best.X; previousY = best.Y; }
                }
            }
            bool accepted = stopped == StudioCropTrackingState.Tracked;
            NormalizedRectangle? feature = accepted ? new(previousX / (double)width, previousY / (double)height,
                patchW / (double)width, patchH / (double)height) : null;
            NormalizedRectangle? viewport = accepted ? new(
                Math.Clamp(initialViewport.X + (previousX - seedX) / (double)width, bounds.X, bounds.Right - initialViewport.Width),
                Math.Clamp(initialViewport.Y + (previousY - seedY) / (double)height, bounds.Y, bounds.Bottom - initialViewport.Height),
                initialViewport.Width, initialViewport.Height) : null;
            samples.Add(new(time, accepted && frameIndex == 0 ? StudioCropTrackingState.Seed : stopped,
                score, margin, feature, viewport));
        }
        StudioTrackingSample[] valid = samples.TakeWhile(static sample => sample.Viewport is not null).ToArray();
        if (valid.Length < 2)
            return new(samples.AsReadOnly(), [], "No safe motion interval. Choose a more distinctive seed or a different start frame. The manual layout remains active.");
        IReadOnlyList<StudioCropTrackingKnot> knots = Simplify(valid, width, height, cancellationToken);
        if (knots.Count > StudioSourceCropTrack.MaximumKnots)
            return new(samples.AsReadOnly(), [], $"Motion needs {knots.Count} knots at the one-analysis-pixel tolerance; the limit is 16. Shorten the interval and analyze again. No motion was truncated or applied.");
        double seconds = (valid[^1].SourcePosition - valid[0].SourcePosition).TotalSeconds;
        return new(samples.AsReadOnly(), [new(knots)],
            $"Accepted {valid.Length}/{samples.Count} frames ({seconds:F2}s), {knots.Count} motion knots. " +
            (valid.Length < samples.Count ? $"Stopped at {samples[valid.Length].SourcePosition:hh\\:mm\\:ss\\.fff}: {samples[valid.Length].State}. " : "") +
            "Review the source frames before Apply. Outside this accepted interval the saved manual region is used; no motion is inferred across a loss or scene change.");
    }

    private static double Correlate(byte[] frame, int origin, int[] offsets, double[] template, double energy)
    {
        double sum = 0, squared = 0, cross = 0;
        for (int i = 0; i < offsets.Length; i++)
        {
            double value = frame[origin + offsets[i]];
            sum += value; squared += value * value; cross += value * template[i];
        }
        double variance = squared - sum * sum / offsets.Length;
        return variance < offsets.Length * 16 ? -1 : Math.Clamp(cross / Math.Sqrt(variance * energy), -1, 1);
    }
    private static double HistogramDistance(byte[] left, byte[] right)
    {
        Span<int> a = stackalloc int[16], b = stackalloc int[16];
        a.Clear(); b.Clear();
        int count = 0;
        for (int i = 0; i < left.Length; i += 17) { a[left[i] / 16]++; b[right[i] / 16]++; count++; }
        double distance = 0;
        for (int i = 0; i < 16; i++) distance += Math.Abs(a[i] - b[i]);
        return distance / (2 * count);
    }
    internal static IReadOnlyList<StudioCropTrackingKnot> Simplify(IReadOnlyList<StudioTrackingSample> samples,
        int width, int height, CancellationToken cancellationToken)
    {
        var keep = new SortedSet<int> { 0, samples.Count - 1 };
        var pending = new Stack<(int Start, int End)>(); pending.Push((0, samples.Count - 1));
        while (pending.TryPop(out var interval))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = samples[interval.Start]; var right = samples[interval.End];
            double greatest = 1; int selected = -1;
            for (int i = interval.Start + 1; i < interval.End; i++)
            {
                double t = (samples[i].SourcePosition - left.SourcePosition) / (right.SourcePosition - left.SourcePosition);
                double x = left.Viewport!.X + (right.Viewport!.X - left.Viewport.X) * t;
                double y = left.Viewport.Y + (right.Viewport.Y - left.Viewport.Y) * t;
                double error = Math.Sqrt(Math.Pow((samples[i].Viewport!.X - x) * width, 2) + Math.Pow((samples[i].Viewport!.Y - y) * height, 2));
                if (error > greatest) { greatest = error; selected = i; }
            }
            if (selected < 0) continue;
            keep.Add(selected); pending.Push((interval.Start, selected)); pending.Push((selected, interval.End));
        }
        return keep.Select(index => new StudioCropTrackingKnot(samples[index].SourcePosition,
            samples[index].Viewport!.X, samples[index].Viewport!.Y)).ToArray();
    }
}
