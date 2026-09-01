using System.Globalization;
using System.IO;
using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegPreviewCommandBuilder
{
    public static FfmpegPreviewCommand Build(
        VideoPreviewFrameRequest request,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(outputPath) ||
            !Path.IsPathFullyQualified(outputPath))
        {
            throw new ArgumentException(
                "Preview output path must be fully qualified.",
                nameof(outputPath));
        }

        VideoStreamInfo video =
            request.Media.PrimaryVideoStream;

        (int width, int height) = CalculateOutputDimensions(
            video,
            request.MaximumWidth,
            request.MaximumHeight,
            request.ContentRegion);

        string filter;
        if (request.ContentRegion is null)
        {
            // Preserve the established full-frame command byte-for-byte.
            filter = $"scale={width}:{height}:flags=lanczos,setsar=1";
        }
        else
        {
            EffectiveDisplayGeometry display =
                EffectiveDisplayGeometryCalculator.Calculate(video);
            PixelRectangle crop =
                EffectiveDisplayGeometryCalculator.CalculateCrop(
                    display,
                    request.ContentRegion);
            filter = string.Join(',',
                $"scale={display.Width}:{display.Height}:flags=lanczos",
                "setsar=1",
                $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}",
                $"scale={width}:{height}:flags=lanczos",
                "setsar=1");
        }

        string timestamp =
            request.Timestamp.TotalSeconds.ToString(
                "0.######",
                CultureInfo.InvariantCulture);

        return new FfmpegPreviewCommand(
        [
            "-hide_banner",
            "-nostdin",
            "-v",
            "error",
            "-y",
            "-ss",
            timestamp,
            "-i",
            request.Media.FullPath,
            "-map",
            $"0:{video.Index}",
            "-an",
            "-sn",
            "-dn",
            "-frames:v",
            "1",
            "-vf",
            filter,
            "-c:v",
            "png",
            "-compression_level",
            "6",
            outputPath,
        ],
        width,
        height);
    }

    internal static (int Width, int Height) CalculateOutputDimensions(
        VideoStreamInfo video,
        int maximumWidth,
        int maximumHeight,
        NormalizedRectangle? contentRegion = null)
    {
        ArgumentNullException.ThrowIfNull(video);

        ValidateMaximumDimension(
            maximumWidth,
            nameof(maximumWidth));
        ValidateMaximumDimension(
            maximumHeight,
            nameof(maximumHeight));

        double displayRatio = EffectiveDisplayGeometryCalculator
            .GetEffectiveDisplayAspectRatio(video);
        if (contentRegion is not null)
        {
            EffectiveDisplayGeometry display =
                EffectiveDisplayGeometryCalculator.Calculate(video);
            PixelRectangle crop =
                EffectiveDisplayGeometryCalculator.CalculateCrop(
                    display,
                    contentRegion);
            displayRatio = crop.Width / (double)crop.Height;
        }

        if (!double.IsFinite(displayRatio) ||
            displayRatio <= 0)
        {
            throw new VideoPreviewFrameException(
                "Replay Foundry could not determine a valid display aspect ratio for the preview.");
        }

        double boundingRatio =
            maximumWidth /
            (double)maximumHeight;

        if (displayRatio >= boundingRatio)
        {
            int height =
                ToEvenAtMost(
                    maximumWidth / displayRatio,
                    maximumHeight);

            return (
                maximumWidth,
                height);
        }

        int width =
            ToEvenAtMost(
                maximumHeight * displayRatio,
                maximumWidth);

        return (
            width,
            maximumHeight);
    }

    private static int ToEvenAtMost(
        double requested,
        int maximum)
    {
        int value =
            Math.Min(
                maximum,
                (int)Math.Floor(requested));

        if ((value & 1) != 0)
        {
            value--;
        }

        if (value < VideoPreviewFrameRequest.MinimumDimension)
        {
            throw new VideoPreviewFrameException(
                "The source display aspect ratio cannot fit within the requested preview bounds.");
        }

        return value;
    }

    private static void ValidateMaximumDimension(
        int value,
        string parameterName)
    {
        if (value is <
                VideoPreviewFrameRequest.MinimumDimension or >
                VideoPreviewFrameRequest.MaximumDimension ||
            (value & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Maximum preview dimensions must be bounded positive even values.");
        }
    }
}
