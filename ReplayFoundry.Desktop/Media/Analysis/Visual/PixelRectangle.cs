using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Visual;

/// <summary>
/// An immutable integer rectangle in an effective-display pixel frame.
/// </summary>
public readonly struct PixelRectangle :
    IEquatable<PixelRectangle>
{
    public PixelRectangle(
        int x,
        int y,
        int width,
        int height)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                x,
                "Pixel rectangle X cannot be negative.");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(y),
                y,
                "Pixel rectangle Y cannot be negative.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "Pixel rectangle width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "Pixel rectangle height must be positive.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public int Right =>
        checked(X + Width);

    public int Bottom =>
        checked(Y + Height);

    public bool Equals(
        PixelRectangle other)
    {
        return X == other.X &&
               Y == other.Y &&
               Width == other.Width &&
               Height == other.Height;
    }

    public override bool Equals(
        object? obj)
    {
        return obj is PixelRectangle other &&
               Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            X,
            Y,
            Width,
            Height);
    }

    public static bool operator ==(
        PixelRectangle left,
        PixelRectangle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(
        PixelRectangle left,
        PixelRectangle right)
    {
        return !left.Equals(right);
    }
}
