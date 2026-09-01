using System;
using System.Globalization;

namespace ReplayFoundry.Desktop.Media.Inspection;

public readonly struct MediaRational :
    IEquatable<MediaRational>,
    IFormattable
{
    public MediaRational(
        long numerator,
        long denominator)
    {
        if (numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numerator),
                numerator,
                "A media rational numerator must be positive.");
        }

        if (denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(denominator),
                denominator,
                "A media rational denominator must be positive.");
        }

        long greatestCommonDivisor =
            CalculateGreatestCommonDivisor(
                numerator,
                denominator);

        Numerator =
            numerator / greatestCommonDivisor;

        Denominator =
            denominator / greatestCommonDivisor;
    }

    public long Numerator { get; }

    public long Denominator { get; }

    public double ToDouble()
    {
        EnsureInitialized();

        return Numerator / (double)Denominator;
    }

    public MediaRational Invert()
    {
        EnsureInitialized();

        return new MediaRational(
            Denominator,
            Numerator);
    }

    public override string ToString()
    {
        return ToString(
            "G",
            CultureInfo.InvariantCulture);
    }

    public string ToString(
        string? format,
        IFormatProvider? formatProvider)
    {
        EnsureInitialized();

        char separator =
            string.Equals(
                format,
                "A",
                StringComparison.OrdinalIgnoreCase)
                ? ':'
                : '/';

        IFormatProvider provider =
            formatProvider ??
            CultureInfo.InvariantCulture;

        return string.Create(
            provider,
            $"{Numerator}{separator}{Denominator}");
    }

    public bool Equals(
        MediaRational other)
    {
        return Numerator == other.Numerator &&
               Denominator == other.Denominator;
    }

    public override bool Equals(
        object? obj)
    {
        return obj is MediaRational other &&
               Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Numerator,
            Denominator);
    }

    public static bool operator ==(
        MediaRational left,
        MediaRational right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(
        MediaRational left,
        MediaRational right)
    {
        return !left.Equals(right);
    }

    public static bool TryParse(
        string? value,
        out MediaRational rational)
    {
        rational = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed =
            value.Trim();

        int separatorIndex =
            trimmed.IndexOfAny(
                ['/', ':']);

        if (separatorIndex <= 0 ||
            separatorIndex >= trimmed.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(
                trimmed.AsSpan(0, separatorIndex),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long numerator) ||
            !long.TryParse(
                trimmed.AsSpan(separatorIndex + 1),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long denominator) ||
            numerator <= 0 ||
            denominator <= 0)
        {
            return false;
        }

        rational =
            new MediaRational(
                numerator,
                denominator);

        return true;
    }

    private void EnsureInitialized()
    {
        if (Numerator <= 0 ||
            Denominator <= 0)
        {
            throw new InvalidOperationException(
                "The media rational value is not initialized.");
        }
    }

    private static long CalculateGreatestCommonDivisor(
        long left,
        long right)
    {
        while (right != 0)
        {
            long remainder =
                left % right;

            left = right;
            right = remainder;
        }

        return left;
    }
}
