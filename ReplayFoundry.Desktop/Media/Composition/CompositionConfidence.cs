using System;
using System.Globalization;

namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// A normalized confidence value between zero and one.
/// </summary>
public readonly struct CompositionConfidence :
    IEquatable<CompositionConfidence>,
    IFormattable
{
    public CompositionConfidence(
        double value)
    {
        if (!double.IsFinite(value) ||
            value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Composition confidence must be finite and between zero and one.");
        }

        Value = value;
    }

    public static CompositionConfidence None =>
        new(0);

    public static CompositionConfidence Certain =>
        new(1);

    public double Value { get; }

    public bool IsCertain =>
        Value == 1d;

    public bool Equals(
        CompositionConfidence other)
    {
        return Value.Equals(
            other.Value);
    }

    public override bool Equals(
        object? obj)
    {
        return obj is CompositionConfidence other &&
               Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return ToString(
            "0.###",
            CultureInfo.InvariantCulture);
    }

    public string ToString(
        string? format,
        IFormatProvider? formatProvider)
    {
        return Value.ToString(
            format,
            formatProvider);
    }

    public static bool operator ==(
        CompositionConfidence left,
        CompositionConfidence right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(
        CompositionConfidence left,
        CompositionConfidence right)
    {
        return !left.Equals(right);
    }
}
