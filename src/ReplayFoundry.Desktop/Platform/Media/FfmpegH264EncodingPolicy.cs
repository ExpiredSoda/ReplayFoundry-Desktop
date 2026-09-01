using System.Globalization;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegH264EncodingPolicy
{
    private const int MainProfile = 77;

    public static IReadOnlyList<string> CreateArguments(int bitsPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitsPerSecond);

        return
        [
            "-c:v",
            "h264_mf",
            "-rate_control",
            "pc_vbr",
            "-scenario",
            "archive",
            "-hw_encoding",
            "0",
            "-b:v",
            bitsPerSecond.ToString(CultureInfo.InvariantCulture),
            "-maxrate",
            checked(bitsPerSecond * 5 / 4).ToString(CultureInfo.InvariantCulture),
            "-bufsize",
            checked(bitsPerSecond * 2).ToString(CultureInfo.InvariantCulture),
            "-profile:v",
            MainProfile.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt",
            "yuv420p",
        ];
    }
}
