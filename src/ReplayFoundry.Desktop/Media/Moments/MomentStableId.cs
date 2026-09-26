using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MomentStableId
{
    public static string Create(
        string prefix,
        params object[] parts)
    {
        string text =
            string.Join(
                "\u001f",
                parts.Select(Format));

        byte[] digest =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(text));

        return string.Concat(
            prefix,
            "-",
            Convert.ToHexString(digest.AsSpan(0, 10))
                .ToLowerInvariant());
    }

    private static string Format(object value) =>
        value switch
        {
            TimeSpan duration =>
                duration.Ticks.ToString(
                    CultureInfo.InvariantCulture),
            double number =>
                number.ToString(
                    "R",
                    CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
}
