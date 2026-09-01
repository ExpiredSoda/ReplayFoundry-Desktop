using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfprobeVideoStreamMapper
{
    public static VideoStreamInfo Map(
        FfprobeStream stream,
        ICollection<MediaInspectionWarning> warnings)
    {
        int width = stream.Width ??
            throw new MediaProbeException(
                $"Video stream {stream.Index} does not report a width.");
        int height = stream.Height ??
            throw new MediaProbeException(
                $"Video stream {stream.Index} does not report a height.");
        double? rotation = FfprobeValueParser.ParseRotation(stream);
        (int? bitDepth, MediaValueSource bitDepthSource) =
            FfprobeValueParser.ResolveBitDepth(
                stream.BitsPerRawSample,
                stream.PixelFormat);
        AddBitDepthWarning(stream, bitDepth, bitDepthSource, warnings);

        MediaRational sampleAspectRatio = ResolveSampleAspectRatio(
            stream,
            warnings,
            out MediaValueSource sampleAspectRatioSource);
        MediaRational? displayAspectRatio = ResolveDisplayAspectRatio(
            stream,
            width,
            height,
            sampleAspectRatio,
            rotation,
            warnings,
            out MediaValueSource displayAspectRatioSource);

        return new VideoStreamInfo(
            stream.Index,
            stream.CodecName ?? "unknown",
            stream.CodecLongName ?? stream.CodecName ?? "Unknown video codec",
            FfprobeValueParser.NormalizeOptional(stream.Profile),
            width,
            height,
            stream.CodedWidth,
            stream.CodedHeight,
            FfprobeValueParser.ParseRational(stream.AverageFrameRate),
            FfprobeValueParser.ParseRational(stream.RealFrameRate),
            FfprobeValueParser.NormalizeOptional(stream.PixelFormat),
            bitDepth,
            bitDepthSource,
            sampleAspectRatio,
            sampleAspectRatioSource,
            displayAspectRatio,
            displayAspectRatioSource,
            rotation,
            FfprobeValueParser.NormalizeOptional(stream.FieldOrder),
            FfprobeValueParser.NormalizeOptional(stream.ColorRange),
            FfprobeValueParser.NormalizeOptional(stream.ColorPrimaries),
            FfprobeValueParser.NormalizeOptional(stream.ColorTransfer),
            FfprobeValueParser.NormalizeOptional(stream.ColorSpace),
            FfprobeValueParser.NormalizeOptional(stream.ChromaLocation),
            FfprobeValueParser.ParseInt64(stream.BitRate),
            FfprobeValueParser.ParseSeconds(stream.Duration),
            stream.Disposition?.Default == 1);
    }

    private static void AddBitDepthWarning(
        FfprobeStream stream,
        int? bitDepth,
        MediaValueSource source,
        ICollection<MediaInspectionWarning> warnings)
    {
        if (source != MediaValueSource.DerivedFromPixelFormat)
        {
            return;
        }

        warnings.Add(
            new MediaInspectionWarning(
                MediaInspectionWarningCode.BitDepthDerived,
                $"Video stream {stream.Index} did not report bit depth. " +
                $"Replay Foundry derived {bitDepth}-bit from pixel format " +
                $"'{stream.PixelFormat}'.",
                stream.Index));
    }

    private static MediaRational ResolveSampleAspectRatio(
        FfprobeStream stream,
        ICollection<MediaInspectionWarning> warnings,
        out MediaValueSource source)
    {
        MediaRational? ratio =
            FfprobeValueParser.ParseRational(stream.SampleAspectRatio);
        if (ratio is not null)
        {
            source = MediaValueSource.ReportedByProbe;
            return ratio.Value;
        }

        source = MediaValueSource.AssumedSquarePixels;
        warnings.Add(
            new MediaInspectionWarning(
                MediaInspectionWarningCode.SampleAspectRatioAssumed,
                $"Video stream {stream.Index} did not report sample " +
                "aspect ratio. Replay Foundry is using square pixels " +
                "for effective display calculations.",
                stream.Index));
        return new MediaRational(1, 1);
    }

    private static MediaRational? ResolveDisplayAspectRatio(
        FfprobeStream stream,
        int width,
        int height,
        MediaRational sampleAspectRatio,
        double? rotation,
        ICollection<MediaInspectionWarning> warnings,
        out MediaValueSource source)
    {
        MediaRational? ratio =
            FfprobeValueParser.ParseRational(stream.DisplayAspectRatio);
        if (ratio is not null)
        {
            source = MediaValueSource.ReportedByProbe;
            return ratio;
        }

        ratio = FfprobeValueParser.DeriveDisplayAspectRatio(
            width,
            height,
            sampleAspectRatio,
            rotation);
        source = ratio is null
            ? MediaValueSource.NotAvailable
            : MediaValueSource.DerivedFromDimensions;
        if (ratio is not null)
        {
            warnings.Add(
                new MediaInspectionWarning(
                    MediaInspectionWarningCode.DisplayAspectRatioDerived,
                    $"Video stream {stream.Index} did not report display " +
                    $"aspect ratio. Replay Foundry derived " +
                    $"{ratio.Value.ToString("A", null)} from dimensions, " +
                    "pixel aspect ratio, and rotation.",
                    stream.Index));
        }

        return ratio;
    }
}
