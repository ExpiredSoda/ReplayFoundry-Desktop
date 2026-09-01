using System.Globalization;
using System.IO;
using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed record FfmpegVisualSemanticReviewVideoCommand(
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

internal static class FfmpegVisualSemanticReviewVideoCommandBuilder
{
    public static FfmpegVisualSemanticReviewVideoCommand Build(
        VisualSemanticReviewVideoMaterializationRequest request,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(outputPath) ||
            !Path.IsPathFullyQualified(outputPath) ||
            !string.Equals(
                Path.GetExtension(outputPath),
                ".mp4",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A visual-semantic review output must be a fully qualified MP4 path.",
                nameof(outputPath));
        }

        string start = Seconds(request.SourceStart);
        string duration = Seconds(request.Duration);
        EffectiveDisplayGeometry display =
            EffectiveDisplayGeometryCalculator.Calculate(
                request.Media.PrimaryVideoStream);
        var filters = new List<string>
        {
            $"scale={display.Width}:{display.Height}:flags=lanczos",
            "setsar=1",
        };
        if (request.ContentRegion is not null)
        {
            PixelRectangle crop =
                EffectiveDisplayGeometryCalculator.CalculateCrop(
                    display,
                    request.ContentRegion);
            filters.Add(
                $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}");
        }
        filters.Add(
            "scale=w='if(gte(iw,ih),min(512,iw),-2)':h='if(gte(iw,ih),-2,min(512,ih))':flags=lanczos");
        filters.Add("fps=10");
        filters.Add("format=yuv420p");
        var arguments = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-v",
            "error",
            "-n",
            "-ss",
            start,
            "-i",
            request.Media.FullPath,
            "-t",
            duration,
            "-map",
            $"0:{request.Media.PrimaryVideoStream.Index}",
            "-an",
            "-sn",
            "-dn",
            "-vf",
            string.Join(',', filters),
        };
        arguments.AddRange(FfmpegH264EncodingPolicy.CreateArguments(2_000_000));
        arguments.AddRange(
        [
            "-movflags",
            "+faststart",
            outputPath,
        ]);

        return new(
            arguments.AsReadOnly(),
            TimeSpan.FromSeconds(
                Math.Clamp(request.Duration.TotalSeconds * 4, 90, 600)));
    }

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString(
            "0.#########",
            CultureInfo.InvariantCulture);
}
