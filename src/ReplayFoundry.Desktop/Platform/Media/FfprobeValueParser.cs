using System.Globalization;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static partial class FfprobeValueParser
{
    private static readonly HashSet<string> KnownEightBitPixelFormats =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "yuv420p", "yuv422p", "yuv444p",
            "yuvj420p", "yuvj422p", "yuvj444p",
            "yuva420p", "yuva422p", "yuva444p",
            "nv12", "nv21", "gray", "gray8", "gbrp", "gbrap",
            "rgb24", "bgr24", "rgba", "bgra", "argb", "abgr",
            "0rgb", "rgb0", "0bgr", "bgr0",
        };

    public static TimeSpan? ParseSeconds(string? value)
    {
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double seconds) ||
            !double.IsFinite(seconds) ||
            seconds < 0 ||
            seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public static long? ParseInt64(string? value) =>
        long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long parsed) && parsed > 0
                ? parsed
                : null;

    public static int? ParseInt32(string? value) =>
        int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsed) && parsed > 0
                ? parsed
                : null;

    public static MediaRational? ParseRational(string? value) =>
        MediaRational.TryParse(value, out MediaRational rational)
            ? rational
            : null;

    public static (int? Value, MediaValueSource Source) ResolveBitDepth(
        string? bitsPerRawSample,
        string? pixelFormat)
    {
        int? explicitDepth = ParseInt32(bitsPerRawSample);
        if (explicitDepth is not null)
        {
            return (explicitDepth, MediaValueSource.ReportedByProbe);
        }

        int? derivedDepth = InferPixelFormatBitDepth(pixelFormat);
        return derivedDepth is null
            ? (null, MediaValueSource.NotAvailable)
            : (derivedDepth, MediaValueSource.DerivedFromPixelFormat);
    }

    public static MediaRational? DeriveDisplayAspectRatio(
        int width,
        int height,
        MediaRational sampleAspectRatio,
        double? rotationDegrees)
    {
        try
        {
            var unrotatedRatio = new MediaRational(
                checked(width * sampleAspectRatio.Numerator),
                checked(height * sampleAspectRatio.Denominator));
            return IsQuarterTurn(rotationDegrees)
                ? unrotatedRatio.Invert()
                : unrotatedRatio;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public static double? ParseRotation(FfprobeStream stream)
    {
        double? sideDataRotation = stream.SideDataList?
            .Select(static item => item.Rotation)
            .FirstOrDefault(static rotation => rotation is not null);
        if (sideDataRotation is not null)
        {
            return NormalizeRotation(sideDataRotation.Value);
        }

        return double.TryParse(
            GetTag(stream.Tags, "rotate"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double tagRotation) && double.IsFinite(tagRotation)
                ? NormalizeRotation(tagRotation)
                : null;
    }

    public static string? GetTag(
        IReadOnlyDictionary<string, string>? tags,
        string key)
    {
        if (tags is null)
        {
            return null;
        }

        foreach ((string tagKey, string value) in tags)
        {
            if (string.Equals(tagKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeOptional(value);
            }
        }

        return null;
    }

    public static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("unspecified", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private static int? InferPixelFormatBitDepth(string? pixelFormat)
    {
        string? normalized = NormalizeOptional(pixelFormat);
        if (normalized is null)
        {
            return null;
        }

        if (KnownEightBitPixelFormats.Contains(normalized))
        {
            return 8;
        }

        Match match = PixelFormatBitDepthRegex().Match(normalized);
        return match.Success &&
               int.TryParse(
                   match.Groups["depth"].Value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out int inferredDepth) &&
               inferredDepth > 0
            ? inferredDepth
            : null;
    }

    private static bool IsQuarterTurn(double? rotationDegrees) =>
        rotationDegrees is double rotation &&
        Math.Abs(Math.Abs(rotation) - 90d) < 0.01d;

    private static double NormalizeRotation(double degrees)
    {
        double normalized = degrees % 360;
        if (normalized > 180)
        {
            normalized -= 360;
        }
        else if (normalized <= -180)
        {
            normalized += 360;
        }

        return normalized;
    }

    [GeneratedRegex(
        @"(?:p|p0)(?<depth>9|10|12|14|16)(?:le|be)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PixelFormatBitDepthRegex();
}
