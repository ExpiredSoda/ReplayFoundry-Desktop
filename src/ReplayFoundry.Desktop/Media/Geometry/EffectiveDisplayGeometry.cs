using System;

namespace ReplayFoundry.Desktop.Media.Geometry;

/// <summary>
/// Full-precision, square-pixel geometry after FFmpeg's normal autorotation.
/// </summary>
public sealed class EffectiveDisplayGeometry
{
    public EffectiveDisplayGeometry(
        int width,
        int height,
        double displayAspectRatio,
        bool autorotationChangesOrientation)
    {
        if (width <= 0 ||
            height <= 0 ||
            (width & 1) != 0 ||
            (height & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Effective-display dimensions must be positive even values.");
        }

        if (!double.IsFinite(displayAspectRatio) ||
            displayAspectRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(displayAspectRatio),
                displayAspectRatio,
                "Effective display aspect ratio must be finite and positive.");
        }

        Width = width;
        Height = height;
        DisplayAspectRatio = displayAspectRatio;
        AutorotationChangesOrientation =
            autorotationChangesOrientation;
    }

    public int Width { get; }

    public int Height { get; }

    public double DisplayAspectRatio { get; }

    public bool AutorotationChangesOrientation { get; }
}
