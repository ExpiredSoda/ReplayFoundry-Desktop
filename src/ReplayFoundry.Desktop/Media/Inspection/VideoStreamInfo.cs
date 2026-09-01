using System;

namespace ReplayFoundry.Desktop.Media.Inspection;

public sealed class VideoStreamInfo
{
    public VideoStreamInfo(
        int index,
        string codecName,
        string codecLongName,
        string? profile,
        int width,
        int height,
        int? codedWidth,
        int? codedHeight,
        MediaRational? averageFrameRate,
        MediaRational? realFrameRate,
        string? pixelFormat,
        int? bitDepth,
        MediaValueSource bitDepthSource,
        MediaRational? sampleAspectRatio,
        MediaValueSource sampleAspectRatioSource,
        MediaRational? displayAspectRatio,
        MediaValueSource displayAspectRatioSource,
        double? rotationDegrees,
        string? fieldOrder,
        string? colorRange,
        string? colorPrimaries,
        string? colorTransfer,
        string? colorMatrix,
        string? chromaLocation,
        long? bitRate,
        TimeSpan? duration,
        bool isDefault)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                "A stream index cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(codecName))
        {
            throw new ArgumentException(
                "A video stream requires a codec name.",
                nameof(codecName));
        }

        if (string.IsNullOrWhiteSpace(codecLongName))
        {
            throw new ArgumentException(
                "A video stream requires a codec display name.",
                nameof(codecLongName));
        }

        if (width <= 0 ||
            height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "A video stream must have positive dimensions.");
        }

        if (codedWidth is <= 0 ||
            codedHeight is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(codedWidth),
                "Coded dimensions must be positive when supplied.");
        }

        if (bitDepth is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitDepth),
                bitDepth,
                "Bit depth must be positive when supplied.");
        }

        ValidateValueSource(
            bitDepth,
            bitDepthSource,
            nameof(bitDepthSource));

        ValidateValueSource(
            sampleAspectRatio,
            sampleAspectRatioSource,
            nameof(sampleAspectRatioSource));

        ValidateValueSource(
            displayAspectRatio,
            displayAspectRatioSource,
            nameof(displayAspectRatioSource));

        if (rotationDegrees is double rotation &&
            !double.IsFinite(rotation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rotationDegrees),
                rotationDegrees,
                "Rotation must be finite when supplied.");
        }

        if (bitRate is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitRate),
                bitRate,
                "Bitrate must be positive when supplied.");
        }

        if (duration is TimeSpan actualDuration &&
            actualDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "Stream duration must be positive when supplied.");
        }

        Index = index;
        CodecName = codecName;
        CodecLongName = codecLongName;
        Profile = profile;
        Width = width;
        Height = height;
        CodedWidth = codedWidth;
        CodedHeight = codedHeight;
        AverageFrameRateExact = averageFrameRate;
        RealFrameRateExact = realFrameRate;
        PixelFormat = pixelFormat;
        BitDepth = bitDepth;
        BitDepthSource = bitDepthSource;
        SampleAspectRatioExact = sampleAspectRatio;
        SampleAspectRatioSource = sampleAspectRatioSource;
        DisplayAspectRatioExact = displayAspectRatio;
        DisplayAspectRatioSource = displayAspectRatioSource;
        RotationDegrees = rotationDegrees;
        FieldOrder = fieldOrder;
        ColorRange = colorRange;
        ColorPrimaries = colorPrimaries;
        ColorTransfer = colorTransfer;
        ColorMatrix = colorMatrix;
        ChromaLocation = chromaLocation;
        BitRate = bitRate;
        Duration = duration;
        IsDefault = isDefault;
    }

    public int Index { get; }

    public string CodecName { get; }

    public string CodecLongName { get; }

    public string? Profile { get; }

    public int Width { get; }

    public int Height { get; }

    public int? CodedWidth { get; }

    public int? CodedHeight { get; }

    public MediaRational? AverageFrameRateExact { get; }

    public MediaRational? RealFrameRateExact { get; }

    public double? AverageFrameRate =>
        AverageFrameRateExact?.ToDouble();

    public double? RealFrameRate =>
        RealFrameRateExact?.ToDouble();

    public MediaRational? PreferredFrameRateExact =>
        AverageFrameRateExact ??
        RealFrameRateExact;

    public double? PreferredFrameRate =>
        PreferredFrameRateExact?.ToDouble();

    public string? PixelFormat { get; }

    public int? BitDepth { get; }

    public MediaValueSource BitDepthSource { get; }

    public MediaRational? SampleAspectRatioExact { get; }

    public MediaValueSource SampleAspectRatioSource { get; }

    public string? SampleAspectRatio =>
        SampleAspectRatioExact?.ToString(
            "A",
            null);

    public MediaRational? DisplayAspectRatioExact { get; }

    public MediaValueSource DisplayAspectRatioSource { get; }

    public string? DisplayAspectRatio =>
        DisplayAspectRatioExact?.ToString(
            "A",
            null);

    public double? RotationDegrees { get; }

    public string? FieldOrder { get; }

    public string? ColorRange { get; }

    public string? ColorPrimaries { get; }

    public string? ColorTransfer { get; }

    public string? ColorMatrix { get; }

    public string? ChromaLocation { get; }

    public long? BitRate { get; }

    public TimeSpan? Duration { get; }

    public bool IsDefault { get; }

    private static void ValidateValueSource<TValue>(
        TValue? value,
        MediaValueSource source,
        string sourceParameterName)
        where TValue : struct
    {
        if (!Enum.IsDefined(
                typeof(MediaValueSource),
                source))
        {
            throw new ArgumentOutOfRangeException(
                sourceParameterName,
                source,
                "The media value source is not defined.");
        }

        if (value is null &&
            source != MediaValueSource.NotAvailable)
        {
            throw new ArgumentException(
                "A missing media value must use the NotAvailable source.",
                sourceParameterName);
        }

        if (value is not null &&
            source == MediaValueSource.NotAvailable)
        {
            throw new ArgumentException(
                "An available media value must identify its source.",
                sourceParameterName);
        }
    }
}
