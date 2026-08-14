using System.Globalization;
using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureJsonReader;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlHostFailureParserValidation
{
    internal static DateTimeOffset Utc(
        JsonElement parent,
        string name,
        string path)
    {
        string value =
            Text(parent, name, path, 64);

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset result) ||
            result.Offset != TimeSpan.Zero)
        {
            throw Failure(
                $"{path}.{name} must be a UTC round-trip timestamp.");
        }

        return result;
    }

    internal static void RequireExact(
        string actual,
        string expected,
        string path)
    {
        if (!string.Equals(
                actual,
                expected,
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path} must be exactly '{expected}'.");
        }
    }

    internal static Qwen3VlOutputParseException Failure(
        string message,
        Exception? innerException = null) =>
        new(message, innerException: innerException);
}
