using System.Globalization;
using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlStrictJsonValues
{
    internal static double? RequireNullableFiniteDouble(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(
                name,
                out JsonElement value))
        {
            throw Failure(
                $"{path}.{name} is required.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out double result) ||
            !double.IsFinite(result))
        {
            throw Failure(
                $"{path}.{name} must be a finite number or null.");
        }

        return result;
    }

    internal static IEnumerable<string> Contradiction(
        bool condition,
        string field)
    {
        if (condition)
        {
            yield return field;
        }
    }

    internal static bool NullableEqual(
        double? first,
        double? second) =>
        first.HasValue == second.HasValue &&
        (!first.HasValue ||
         Math.Abs(first.Value - second!.Value) <=
            Qwen3VlActualPtsCoverageCalculator
                .ComparisonEpsilon);

    internal static int RequireInt32(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int result))
        {
            throw Failure($"{path}.{name} must be an integer.");
        }

        return result;
    }

    internal static int? RequireNullableInt32(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(
                name,
                out JsonElement value))
        {
            throw Failure(
                $"{path}.{name} is required.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int result))
        {
            throw Failure(
                $"{path}.{name} must be an integer or null.");
        }

        return result;
    }

    internal static bool RequireBoolean(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind is not (
                JsonValueKind.True or
                JsonValueKind.False
            ))
        {
            throw Failure($"{path}.{name} must be a boolean.");
        }

        return value.GetBoolean();
    }

    internal static DateTimeOffset RequireUtcDateTimeOffset(
        JsonElement parent,
        string name,
        string path)
    {
        string value =
            RequireUntrimmedString(
                parent,
                name,
                path,
                64);

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset result) ||
            result.Offset != TimeSpan.Zero)
        {
            throw Failure(
                $"{path}.{name} must be a round-trip timestamp with zero UTC offset.");
        }

        return result;
    }

    internal static double RequireFiniteDouble(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out double result) ||
            !double.IsFinite(result))
        {
            throw Failure($"{path}.{name} must be a finite number.");
        }

        return result;
    }

    internal static long? RequireNullableInt64(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            throw Failure($"{path}.{name} is required.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out long result))
        {
            throw Failure($"{path}.{name} must be an integer or null.");
        }

        return result;
    }

    internal static TimeSpan Seconds(double value, string path)
    {
        if (!double.IsFinite(value) ||
            value < 0 ||
            value > TimeSpan.MaxValue.TotalSeconds)
        {
            throw Failure($"{path} must be finite, non-negative seconds.");
        }

        return TimeSpan.FromSeconds(value);
    }

    internal static void RequireExactValue(
        string actual,
        string expected,
        string path)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw Failure(
                $"{path} must be exactly '{expected}'.");
        }
    }
}
