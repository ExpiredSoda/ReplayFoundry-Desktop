using System;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Geometry;

/// <summary>
/// Applies the effective-display interpretation shared by preview extraction
/// and full-precision evidence analysis.
/// </summary>
public static class EffectiveDisplayGeometryCalculator
{
    public static EffectiveDisplayGeometry Calculate(
        VideoStreamInfo video)
    {
        ArgumentNullException.ThrowIfNull(video);

        bool quarterTurn =
            AutorotationChangesOrientation(
                video.RotationDegrees);

        double displayAspectRatio =
            GetEffectiveDisplayAspectRatio(video);

        int autorotatedHeight =
            quarterTurn
                ? video.Width
                : video.Height;

        int effectiveHeight =
            ToPositiveEvenAtMost(
                autorotatedHeight);

        int effectiveWidth =
            ToPositiveEvenNearest(
                effectiveHeight *
                displayAspectRatio);

        return new EffectiveDisplayGeometry(
            effectiveWidth,
            effectiveHeight,
            displayAspectRatio,
            quarterTurn);
    }

    public static double GetEffectiveDisplayAspectRatio(
        VideoStreamInfo video)
    {
        ArgumentNullException.ThrowIfNull(video);

        bool quarterTurn =
            AutorotationChangesOrientation(
                video.RotationDegrees);

        if (video.DisplayAspectRatioExact is
            MediaRational displayAspectRatio)
        {
            double ratio =
                displayAspectRatio.ToDouble();

            // Reported DAR describes the encoded orientation. Derived DAR in
            // Replay Foundry already includes rotation.
            if (video.DisplayAspectRatioSource ==
                    MediaValueSource.ReportedByProbe &&
                quarterTurn)
            {
                ratio =
                    1d / ratio;
            }

            ValidateRatio(ratio);
            return ratio;
        }

        double fallbackRatio =
            video.Width /
            (double)video.Height;

        if (video.SampleAspectRatioExact is
            MediaRational sampleAspectRatio)
        {
            fallbackRatio *=
                sampleAspectRatio.ToDouble();
        }

        if (quarterTurn)
        {
            fallbackRatio =
                1d / fallbackRatio;
        }

        ValidateRatio(fallbackRatio);
        return fallbackRatio;
    }

    public static PixelRectangle CalculateCrop(
        EffectiveDisplayGeometry display,
        NormalizedRectangle requested)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(requested);

        int left =
            RoundDownToEven(
                requested.X *
                display.Width);

        int top =
            RoundDownToEven(
                requested.Y *
                display.Height);

        int right =
            RoundUpToEven(
                requested.Right *
                display.Width);

        int bottom =
            RoundUpToEven(
                requested.Bottom *
                display.Height);

        left =
            Math.Clamp(
                left,
                0,
                display.Width - 2);

        top =
            Math.Clamp(
                top,
                0,
                display.Height - 2);

        right =
            Math.Clamp(
                right,
                left + 2,
                display.Width);

        bottom =
            Math.Clamp(
                bottom,
                top + 2,
                display.Height);

        var crop =
            new PixelRectangle(
                left,
                top,
                right - left,
                bottom - top);

        if (crop.Right > display.Width ||
            crop.Bottom > display.Height ||
            (crop.X & 1) != 0 ||
            (crop.Y & 1) != 0 ||
            (crop.Width & 1) != 0 ||
            (crop.Height & 1) != 0)
        {
            throw new InvalidOperationException(
                "Effective-display crop conversion produced invalid geometry.");
        }

        return crop;
    }

    public static bool AutorotationChangesOrientation(
        double? rotationDegrees)
    {
        double normalized =
            (rotationDegrees ?? 0) %
            360;

        if (normalized < 0)
        {
            normalized += 360;
        }

        return Math.Abs(normalized - 90) < 0.01 ||
               Math.Abs(normalized - 270) < 0.01;
    }

    private static int RoundDownToEven(
        double value)
    {
        int rounded =
            checked(
                (int)Math.Floor(value));

        return rounded & ~1;
    }

    private static int RoundUpToEven(
        double value)
    {
        int rounded =
            checked(
                (int)Math.Ceiling(value));

        return (rounded + 1) & ~1;
    }

    private static int ToPositiveEvenAtMost(
        int value)
    {
        int even =
            value & ~1;

        if (even < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "The decoded display dimension is too small for an even analysis frame.");
        }

        return even;
    }

    private static int ToPositiveEvenNearest(
        double value)
    {
        if (!double.IsFinite(value) ||
            value < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "The effective display width is too small.");
        }

        int rounded =
            checked(
                (int)Math.Round(
                    value,
                    MidpointRounding.AwayFromZero));

        if ((rounded & 1) != 0)
        {
            rounded +=
                value >= rounded
                    ? 1
                    : -1;
        }

        return Math.Max(
            rounded,
            2);
    }

    private static void ValidateRatio(
        double ratio)
    {
        if (!double.IsFinite(ratio) ||
            ratio <= 0)
        {
            throw new ArgumentException(
                "Replay Foundry could not determine a valid effective display aspect ratio.");
        }
    }
}
