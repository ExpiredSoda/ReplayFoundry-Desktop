using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public enum CompositionRegionResizeHandle
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

internal static class CompositionRegionGeometryEditor
{
    public static double NormalizeDragDelta(
        double logicalDelta,
        double logicalExtent)
    {
        ValidateFiniteDelta(
            logicalDelta,
            nameof(logicalDelta));

        if (!double.IsFinite(logicalExtent) ||
            logicalExtent <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(logicalExtent));
        }

        return logicalDelta / logicalExtent;
    }

    public static NormalizedRectangle Move(
        NormalizedRectangle geometry,
        double horizontalDelta,
        double verticalDelta)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ValidateFiniteDelta(horizontalDelta, nameof(horizontalDelta));
        ValidateFiniteDelta(verticalDelta, nameof(verticalDelta));

        return new NormalizedRectangle(
            Math.Clamp(geometry.X + horizontalDelta, 0, 1 - geometry.Width),
            Math.Clamp(geometry.Y + verticalDelta, 0, 1 - geometry.Height),
            geometry.Width,
            geometry.Height);
    }

    public static NormalizedRectangle Resize(
        NormalizedRectangle geometry,
        CompositionRegionResizeHandle handle,
        double horizontalDelta,
        double verticalDelta,
        double minimumSize)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!Enum.IsDefined(handle))
        {
            throw new ArgumentOutOfRangeException(nameof(handle));
        }

        ValidateFiniteDelta(horizontalDelta, nameof(horizontalDelta));
        ValidateFiniteDelta(verticalDelta, nameof(verticalDelta));
        if (!double.IsFinite(minimumSize) || minimumSize <= 0 || minimumSize > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSize));
        }

        double left = geometry.X;
        double top = geometry.Y;
        double right = geometry.X + geometry.Width;
        double bottom = geometry.Y + geometry.Height;

        switch (handle)
        {
            case CompositionRegionResizeHandle.TopLeft:
                left = Math.Clamp(
                    left + horizontalDelta,
                    0,
                    right - minimumSize);
                top = Math.Clamp(
                    top + verticalDelta,
                    0,
                    bottom - minimumSize);
                break;

            case CompositionRegionResizeHandle.TopRight:
                right = Math.Clamp(
                    right + horizontalDelta,
                    left + minimumSize,
                    1);
                top = Math.Clamp(
                    top + verticalDelta,
                    0,
                    bottom - minimumSize);
                break;

            case CompositionRegionResizeHandle.BottomLeft:
                left = Math.Clamp(
                    left + horizontalDelta,
                    0,
                    right - minimumSize);
                bottom = Math.Clamp(
                    bottom + verticalDelta,
                    top + minimumSize,
                    1);
                break;

            case CompositionRegionResizeHandle.BottomRight:
                right = Math.Clamp(
                    right + horizontalDelta,
                    left + minimumSize,
                    1);
                bottom = Math.Clamp(
                    bottom + verticalDelta,
                    top + minimumSize,
                    1);
                break;
        }

        return new NormalizedRectangle(
            left,
            top,
            right - left,
            bottom - top);
    }

    private static void ValidateFiniteDelta(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Interactive geometry deltas must be finite.");
        }
    }
}
