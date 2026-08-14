using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureJsonReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureParserValidation;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlHostFailureArrayReader
{
    internal static int[]? NullableIntegerArray(
        JsonElement parent,
        string name,
        string path,
        int maximumCount)
    {
        JsonElement value =
            Property(parent, name, path);

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Failure(
                $"{path}.{name} must be an array or null.");
        }

        JsonElement[] items =
            value.EnumerateArray().ToArray();

        if (items.Length > maximumCount ||
            items.Any(
                static item =>
                    item.ValueKind != JsonValueKind.Number ||
                    !item.TryGetInt32(out _)))
        {
            throw Failure(
                $"{path}.{name} contains invalid integer values.");
        }

        return items
            .Select(static item => item.GetInt32())
            .ToArray();
    }

    internal static bool StrictlyIncreasing(
        IReadOnlyList<int> values)
    {
        for (int index = 1;
             index < values.Count;
             index++)
        {
            if (values[index] <= values[index - 1])
            {
                return false;
            }
        }

        return true;
    }

    internal static bool StrictlyIncreasing(
        IReadOnlyList<double> values)
    {
        for (int index = 1;
             index < values.Count;
             index++)
        {
            if (values[index] <= values[index - 1])
            {
                return false;
            }
        }

        return true;
    }

    internal static double[]? NullableNumberArray(
        JsonElement parent,
        string name,
        string path,
        int maximumCount)
    {
        JsonElement value =
            Property(parent, name, path);

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Failure(
                $"{path}.{name} must be an array or null.");
        }

        JsonElement[] items =
            value.EnumerateArray().ToArray();

        if (items.Length > maximumCount ||
            items.Any(
                static item =>
                    item.ValueKind != JsonValueKind.Number ||
                    !item.TryGetDouble(out double number) ||
                    !double.IsFinite(number)))
        {
            throw Failure(
                $"{path}.{name} contains invalid numeric values.");
        }

        return items
            .Select(static item => item.GetDouble())
            .ToArray();
    }

    internal static string[]? StringArray(
        JsonElement parent,
        string name,
        string path,
        int maximumCount,
        int maximumItemLength,
        bool nullable)
    {
        JsonElement value =
            Property(parent, name, path);

        if (nullable &&
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Failure($"{path}.{name} must be an array.");
        }

        JsonElement[] items =
            value.EnumerateArray().ToArray();

        if (items.Length > maximumCount)
        {
            throw Failure(
                $"{path}.{name} exceeds its bounded item count.");
        }

        string[] result = new string[items.Length];

        for (int index = 0; index < items.Length; index++)
        {
            if (items[index].ValueKind !=
                JsonValueKind.String)
            {
                throw Failure(
                    $"{path}.{name}[{index}] must be text.");
            }

            string? item = items[index].GetString();

            if (string.IsNullOrWhiteSpace(item) ||
                item.Length > maximumItemLength ||
                !string.Equals(
                    item,
                    item.Trim(),
                    StringComparison.Ordinal))
            {
                throw Failure(
                    $"{path}.{name}[{index}] must be bounded, nonblank, untrimmed text.");
            }

            result[index] = item;
        }

        return result;
    }
}
