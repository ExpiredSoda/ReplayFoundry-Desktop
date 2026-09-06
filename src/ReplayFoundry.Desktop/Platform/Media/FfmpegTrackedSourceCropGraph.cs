using System.Globalization;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Geometry;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegTrackedSourceCropGraph
{
    internal static string Crop(StudioSourceCropTrack track, EffectiveDisplayGeometry display,
        TimeSpan sourceStart, bool manualFallback)
    {
        int width = Math.Max(2, (int)Math.Floor(track.ViewportWidth * display.Width) & ~1);
        int height = Math.Max(2, (int)Math.Floor(track.ViewportHeight * display.Height) & ~1);
        if (manualFallback)
        {
            var manual = EffectiveDisplayGeometryCalculator.CalculateCrop(display, track.ManualRegion);
            width = manual.Width; height = manual.Height;
        }
        double defaultX = manualFallback ? track.ManualRegion.X : track.Runs[0].Knots[0].X;
        double defaultY = manualFallback ? track.ManualRegion.Y : track.Runs[0].Knots[0].Y;
        string x = Position(track, sourceStart, static knot => knot.X, defaultX, display.Width);
        string y = Position(track, sourceStart, static knot => knot.Y, defaultY, display.Height);
        return $"crop={width}:{height}:x='{x}':y='{y}'";
    }
    internal static string Enabled(StudioSourceCropTrack track, TimeSpan sourceStart) =>
        string.Join('+', track.Runs.Select(run => Interval(run, sourceStart)));
    private static string Position(StudioSourceCropTrack track, TimeSpan sourceStart,
        Func<StudioCropTrackingKnot, double> value, double fallback, int dimension)
    {
        string result = Number(fallback * dimension);
        for (int runIndex = track.Runs.Count - 1; runIndex >= 0; runIndex--)
        {
            StudioCropTrackingRun run = track.Runs[runIndex];
            string blend = Number(value(run.Knots[^1]) * dimension);
            for (int index = run.Knots.Count - 2; index >= 0; index--)
            {
                var left = run.Knots[index]; var right = run.Knots[index + 1];
                double start = (left.SourcePosition - sourceStart).TotalSeconds;
                double end = (right.SourcePosition - sourceStart).TotalSeconds;
                string linear = $"{Number(value(left) * dimension)}+({Number((value(right) - value(left)) * dimension)})*(t-({Number(start)}))/{Number(end - start)}";
                blend = $"if(lt(t,{Number(end)}),{linear},{blend})";
            }
            result = $"if({Interval(run, sourceStart)},{blend},{result})";
        }
        return result;
    }
    private static string Interval(StudioCropTrackingRun run, TimeSpan sourceStart) =>
        $"between(t,{Number((run.Knots[0].SourcePosition - sourceStart).TotalSeconds)},{Number((run.Knots[^1].SourcePosition - sourceStart).TotalSeconds)})";
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
