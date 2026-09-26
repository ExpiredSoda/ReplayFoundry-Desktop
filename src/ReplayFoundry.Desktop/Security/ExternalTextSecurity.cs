using System.Globalization;
using System.Text;

namespace ReplayFoundry.Desktop.Security;

/// <summary>
/// Normalizes user-controlled text immediately before it crosses an online
/// service boundary. It removes invisible direction/control syntax while
/// preserving ordinary Unicode audience copy.
/// </summary>
internal static class ExternalTextSecurity
{
    internal static string SingleLine(string value, int maximumLength)
    {
        string secured = Normalize(value, allowLineBreaks: false);
        return secured.Length <= maximumLength
            ? secured
            : secured[..maximumLength];
    }

    internal static string MultiLine(string value, int maximumLength)
    {
        string secured = Normalize(value, allowLineBreaks: true);
        return secured.Length <= maximumLength
            ? secured
            : secured[..maximumLength];
    }

    private static string Normalize(string value, bool allowLineBreaks)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new StringBuilder(value.Length);
        bool pendingSpace = false;
        foreach (Rune rune in value.Normalize(NormalizationForm.FormC)
                     .EnumerateRunes())
        {
            int code = rune.Value;
            if (IsUnsafe(code)) continue;
            if (code is '\r' or '\n')
            {
                if (allowLineBreaks)
                {
                    while (result.Length > 0 && result[^1] == ' ')
                    {
                        result.Length--;
                    }
                    if (result.Length == 0 || result[^1] != '\n')
                    {
                        result.Append('\n');
                    }
                    pendingSpace = false;
                }
                else
                {
                    pendingSpace = result.Length > 0;
                }
                continue;
            }
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = result.Length > 0 && result[^1] != '\n';
                continue;
            }
            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }
            result.Append(rune.ToString());
        }
        return result.ToString().Trim();
    }

    private static bool IsUnsafe(int code) =>
        (code is <= 0x08 or 0x0B or 0x0C or >= 0x0E and <= 0x1F or
        >= 0x7F and <= 0x9F or
        0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF or
        >= 0x202A and <= 0x202E or
        >= 0x2066 and <= 0x2069 or
        >= 0xFDD0 and <= 0xFDEF) ||
        (code & 0xFFFF) == 0xFFFE ||
        (code & 0xFFFF) == 0xFFFF;
}
