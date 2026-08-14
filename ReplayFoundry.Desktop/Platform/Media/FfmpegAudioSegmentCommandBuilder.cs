using System.Globalization;
using System.IO;
using ReplayFoundry.Desktop.Media.AudioExtraction;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed record FfmpegAudioSegmentCommand(
    IReadOnlyList<string> Arguments,
    int SampleRate,
    int ChannelCount,
    int BitsPerSample);

internal static class FfmpegAudioSegmentCommandBuilder
{
    public const int OutputSampleRate = 16000;
    public const int OutputChannelCount = 1;
    public const int OutputBitsPerSample = 16;

    public static FfmpegAudioSegmentCommand Build(
        AudioSegmentExtractionRequest request,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(outputPath) ||
            !Path.IsPathFullyQualified(outputPath))
        {
            throw new ArgumentException(
                "FFmpeg audio output must be fully qualified.",
                nameof(outputPath));
        }

        string start =
            request.Start.TotalSeconds.ToString(
                "0.#########",
                CultureInfo.InvariantCulture);
        string duration =
            request.Duration.TotalSeconds.ToString(
                "0.#########",
                CultureInfo.InvariantCulture);

        string[] arguments =
        [
            "-hide_banner",
            "-nostdin",
            "-v",
            "error",
            "-n",
            "-i",
            request.SourcePath,
            "-ss",
            start,
            "-t",
            duration,
            "-map",
            $"0:{request.AbsoluteAudioStreamIndex}",
            "-vn",
            "-sn",
            "-dn",
            "-ac",
            OutputChannelCount.ToString(
                CultureInfo.InvariantCulture),
            "-ar",
            OutputSampleRate.ToString(
                CultureInfo.InvariantCulture),
            "-c:a",
            "pcm_s16le",
            "-f",
            "wav",
            outputPath,
        ];

        return new FfmpegAudioSegmentCommand(
            Array.AsReadOnly(arguments),
            OutputSampleRate,
            OutputChannelCount,
            OutputBitsPerSample);
    }
}
