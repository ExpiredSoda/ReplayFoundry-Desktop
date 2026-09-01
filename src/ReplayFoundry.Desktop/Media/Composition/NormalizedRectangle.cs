using System;

namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// An immutable rectangle in normalized effective-display coordinates.
/// </summary>
public sealed class NormalizedRectangle
{
    public NormalizedRectangle(
        double x,
        double y,
        double width,
        double height)
    {
        ValidateFinite(
            x,
            nameof(x));

        ValidateFinite(
            y,
            nameof(y));

        ValidateFinite(
            width,
            nameof(width));

        ValidateFinite(
            height,
            nameof(height));

        if (x is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                x,
                "Rectangle X must be greater than or equal to zero and less than one.");
        }

        if (y is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(y),
                y,
                "Rectangle Y must be greater than or equal to zero and less than one.");
        }

        if (width is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "Rectangle width must be greater than zero and no greater than one.");
        }

        if (height is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "Rectangle height must be greater than zero and no greater than one.");
        }

        if (x + width > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "Rectangle X plus width cannot extend beyond the normalized canvas.");
        }

        if (y + height > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "Rectangle Y plus height cannot extend beyond the normalized canvas.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static NormalizedRectangle FullFrame =>
        new(
            0,
            0,
            1,
            1);

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public double Right =>
        X + Width;

    public double Bottom =>
        Y + Height;

    public double Area =>
        Width * Height;

    public bool Intersects(
        NormalizedRectangle other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return X < other.Right &&
               Right > other.X &&
               Y < other.Bottom &&
               Bottom > other.Y;
    }

    public bool Contains(
        NormalizedRectangle other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return other.X >= X &&
               other.Y >= Y &&
               other.Right <= Right &&
               other.Bottom <= Bottom;
    }

    private static void ValidateFinite(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Normalized rectangle values must be finite.");
        }
    }
}
