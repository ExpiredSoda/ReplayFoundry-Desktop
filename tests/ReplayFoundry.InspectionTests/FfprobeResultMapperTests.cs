using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.InspectionTests;

internal static class FfprobeResultMapperTests
{
    public static IEnumerable<TestCase> GetTests()
    {
        yield return new TestCase(
            "Mapper derives safe portrait metadata with provenance",
            DerivesSafePortraitMetadataWithProvenance);

        yield return new TestCase(
            "Mapper preserves exact fractional frame rates",
            PreservesExactFractionalFrameRates);

        yield return new TestCase(
            "Mapper derives rotated display aspect ratio",
            DerivesRotatedDisplayAspectRatio);

        yield return new TestCase(
            "Mapper keeps unknown audio channel layout unknown",
            KeepsUnknownAudioChannelLayoutUnknown);

        yield return new TestCase(
            "Mapper preserves audio track titles as metadata only",
            PreservesAudioTrackTitlesAsMetadataOnly);

        yield return new TestCase(
            "Mapper preserves reported channel layout",
            PreservesReportedChannelLayout);

        yield return new TestCase(
            "Mapper rejects input without a video stream",
            RejectsInputWithoutVideoStream);
    }

    private static void DerivesSafePortraitMetadataWithProvenance()
    {
        FfprobeDocument document =
            CreatePortraitAv1Document();

        MediaProbeResult result =
            FfprobeResultMapper.Map(
                @"C:\Fixtures\portrait.mkv",
                document,
                CreateManifest());

        VideoStreamInfo video =
            result.PrimaryVideoStream;

        TestAssert.Equal(
            new MediaRational(60, 1),
            video.AverageFrameRateExact!.Value,
            "The exact average frame rate should be preserved.");

        TestAssert.Equal(
            8,
            video.BitDepth!.Value,
            "Plain yuv420p should safely derive 8-bit component depth.");

        TestAssert.Equal(
            MediaValueSource.DerivedFromPixelFormat,
            video.BitDepthSource,
            "Derived bit depth must retain provenance.");

        TestAssert.Equal(
            new MediaRational(1, 1),
            video.SampleAspectRatioExact!.Value,
            "Missing sample aspect ratio should use a square-pixel assumption.");

        TestAssert.Equal(
            MediaValueSource.AssumedSquarePixels,
            video.SampleAspectRatioSource,
            "The square-pixel assumption must be explicit.");

        TestAssert.Equal(
            new MediaRational(9, 16),
            video.DisplayAspectRatioExact!.Value,
            "Portrait dimensions should derive a 9:16 display aspect ratio.");

        TestAssert.Equal(
            MediaValueSource.DerivedFromDimensions,
            video.DisplayAspectRatioSource,
            "Derived display aspect ratio must retain provenance.");

        TestAssert.Contains(
            result.Warnings,
            warning =>
                warning.Code ==
                MediaInspectionWarningCode.BitDepthDerived,
            "A derived bit-depth warning should be present.");

        TestAssert.Contains(
            result.Warnings,
            warning =>
                warning.Code ==
                MediaInspectionWarningCode.SampleAspectRatioAssumed,
            "A square-pixel warning should be present.");

        TestAssert.Contains(
            result.Warnings,
            warning =>
                warning.Code ==
                MediaInspectionWarningCode.DisplayAspectRatioDerived,
            "A derived display-aspect warning should be present.");

        TestAssert.Contains(
            result.Warnings,
            warning =>
                warning.Code ==
                MediaInspectionWarningCode.PrimaryVideoStreamNotMarked,
            "A missing-default-stream warning should be present.");
    }

    private static void PreservesExactFractionalFrameRates()
    {
        FfprobeDocument document =
            CreateBaselineDocument(
                averageFrameRate: "60000/1001",
                realFrameRate: "60000/1001",
                bitsPerRawSample: "10",
                pixelFormat: "yuv420p10le",
                sampleAspectRatio: "1:1",
                displayAspectRatio: "16:9",
                rotation: null);

        MediaProbeResult result =
            FfprobeResultMapper.Map(
                @"C:\Fixtures\fractional.mp4",
                document,
                CreateManifest());

        VideoStreamInfo video =
            result.PrimaryVideoStream;

        TestAssert.Equal(
            new MediaRational(60000, 1001),
            video.AverageFrameRateExact!.Value,
            "The exact average frame-rate fraction should be retained.");

        TestAssert.NearlyEqual(
            59.94005994,
            video.AverageFrameRate!.Value,
            0.000001,
            "The decimal frame rate should be derived from the exact fraction.");

        TestAssert.Equal(
            MediaValueSource.ReportedByProbe,
            video.BitDepthSource,
            "Reported bit depth must take precedence over pixel-format derivation.");

        TestAssert.Equal(
            MediaValueSource.ReportedByProbe,
            video.SampleAspectRatioSource,
            "Reported sample aspect ratio should remain reported.");

        TestAssert.Equal(
            MediaValueSource.ReportedByProbe,
            video.DisplayAspectRatioSource,
            "Reported display aspect ratio should remain reported.");
    }

    private static void DerivesRotatedDisplayAspectRatio()
    {
        FfprobeDocument document =
            CreateBaselineDocument(
                averageFrameRate: "30/1",
                realFrameRate: "30/1",
                bitsPerRawSample: "8",
                pixelFormat: "yuv420p",
                sampleAspectRatio: "1:1",
                displayAspectRatio: null,
                rotation: 90,
                width: 1920,
                height: 1080);

        MediaProbeResult result =
            FfprobeResultMapper.Map(
                @"C:\Fixtures\rotated.mp4",
                document,
                CreateManifest());

        TestAssert.Equal(
            new MediaRational(9, 16),
            result.PrimaryVideoStream.DisplayAspectRatioExact!.Value,
            "A quarter-turn rotation should invert the unrotated display ratio.");
    }

    private static void KeepsUnknownAudioChannelLayoutUnknown()
    {
        FfprobeDocument document =
            CreatePortraitAv1Document();

        MediaProbeResult result =
            FfprobeResultMapper.Map(
                @"C:\Fixtures\unknown-layout.mkv",
                document,
                CreateManifest());

        AudioStreamInfo audio =
            result.AudioStreams[0];

        TestAssert.Equal(
            2,
            audio.Channels!.Value,
            "The reported channel count should be retained.");

        TestAssert.Null(
            audio.ChannelLayout,
            "Two channels must not be silently guessed as stereo.");

        TestAssert.Contains(
            result.Warnings,
            warning =>
                warning.Code ==
                    MediaInspectionWarningCode.AudioChannelLayoutNotReported &&
                warning.StreamIndex == audio.Index,
            "Unknown channel layout should produce a stream-specific warning.");
    }

    private static void PreservesAudioTrackTitlesAsMetadataOnly()
    {
        FfprobeDocument document =
            CreatePortraitAv1Document();

        MediaProbeResult result =
            FfprobeResultMapper.Map(
                @"C:\Fixtures\misleading-title.mkv",
                document,
                CreateManifest());

        TestAssert.Equal(
            "Microphone_vertical",
            result.AudioStreams[0].Title,
            "The first track title should be preserved exactly as metadata.");

        TestAssert.Equal(
            "Track3_vertical",
            result.AudioStreams[1].Title,
            "The second track title should be preserved exactly as metadata.");

        TestAssert.False(
            result.AudioStreams[0].IsDefault,
            "A microphone-like title must not make a track default.");

        TestAssert.False(
            result.AudioStreams[1].IsDefault,
            "An arbitrary track title must not make a track default.");
    }

    private static void PreservesReportedChannelLayout()
    {
        FfprobeDocument document =
            CreateBaselineDocument(
                averageFrameRate: "30/1",
                realFrameRate: "30/1",
                bitsPerRawSample: "8",
                pixelFormat: "yuv420p",
                sampleAspectRatio: "1:1",
                displayAspectRatio: "16:9",
                rotation: null);

        document.Streams!.Add(
            new FfprobeStream
            {
                Index = 1,
                CodecName = "aac",
                CodecLongName = "AAC",
                CodecType = "audio",
                SampleRate = "48000",
                Channels = 2,
                ChannelLayout = "stereo",
                BitsPerSample = 0,
                BitRate = "192000",
                Disposition = new FfprobeDisposition
                {
                    Default = 1,
                },
            });

        MediaProbeResult result =
            FfprobeResultMapper.Map(
                @"C:\Fixtures\stereo.mp4",
                document,
                CreateManifest());

        TestAssert.Equal(
            "stereo",
            result.AudioStreams[0].ChannelLayout,
            "A reported channel layout should be preserved.");

        TestAssert.False(
            result.Warnings.Any(
                warning =>
                    warning.Code ==
                    MediaInspectionWarningCode.AudioChannelLayoutNotReported),
            "A reported layout should not produce a missing-layout warning.");
    }

    private static void RejectsInputWithoutVideoStream()
    {
        var document =
            new FfprobeDocument
            {
                Format = new FfprobeFormat
                {
                    FormatName = "matroska,webm",
                    FormatLongName = "Matroska / WebM",
                    Duration = "10.0",
                    Size = "1000",
                    ProbeScore = 100,
                },
                Streams =
                [
                    new FfprobeStream
                    {
                        Index = 0,
                        CodecName = "aac",
                        CodecLongName = "AAC",
                        CodecType = "audio",
                        SampleRate = "48000",
                        Channels = 2,
                    },
                ],
            };

        TestAssert.Throws<MediaProbeException>(
            () =>
                FfprobeResultMapper.Map(
                    @"C:\Fixtures\audio-only.mka",
                    document,
                    CreateManifest()),
            "Structural inspection must reject a source without video.");
    }

    private static FfprobeDocument CreatePortraitAv1Document()
    {
        return new FfprobeDocument
        {
            Format = new FfprobeFormat
            {
                FileName = @"C:\Fixtures\portrait.mkv",
                FormatName = "matroska,webm",
                FormatLongName = "Matroska / WebM",
                StartTime = "0.000000",
                Duration = "4721.472",
                Size = "8042322166",
                BitRate = "13618000",
                ProbeScore = 100,
                Tags = new Dictionary<string, string>
                {
                    ["ENCODER"] = "Lavf61.7.100",
                },
            },
            Streams =
            [
                new FfprobeStream
                {
                    Index = 0,
                    CodecName = "av1",
                    CodecLongName = "Alliance for Open Media AV1",
                    Profile = "Main",
                    CodecType = "video",
                    Width = 1080,
                    Height = 1920,
                    CodedWidth = 1080,
                    CodedHeight = 1920,
                    AverageFrameRate = "60/1",
                    RealFrameRate = "60/1",
                    PixelFormat = "yuv420p",
                    ColorRange = "tv",
                    ColorPrimaries = "bt709",
                    ColorTransfer = "bt709",
                    ColorSpace = "bt709",
                    ChromaLocation = "left",
                    Disposition = new FfprobeDisposition
                    {
                        Default = 0,
                    },
                },
                CreatePcmAudioStream(
                    index: 1,
                    title: "Microphone_vertical"),
                CreatePcmAudioStream(
                    index: 2,
                    title: "Track3_vertical"),
            ],
        };
    }

    private static FfprobeDocument CreateBaselineDocument(
        string averageFrameRate,
        string realFrameRate,
        string? bitsPerRawSample,
        string pixelFormat,
        string? sampleAspectRatio,
        string? displayAspectRatio,
        double? rotation,
        int width = 1920,
        int height = 1080)
    {
        var stream =
            new FfprobeStream
            {
                Index = 0,
                CodecName = "h264",
                CodecLongName = "H.264",
                Profile = "High",
                CodecType = "video",
                Width = width,
                Height = height,
                CodedWidth = width,
                CodedHeight = height,
                AverageFrameRate = averageFrameRate,
                RealFrameRate = realFrameRate,
                PixelFormat = pixelFormat,
                BitsPerRawSample = bitsPerRawSample,
                SampleAspectRatio = sampleAspectRatio,
                DisplayAspectRatio = displayAspectRatio,
                ColorRange = "tv",
                ColorPrimaries = "bt709",
                ColorTransfer = "bt709",
                ColorSpace = "bt709",
                ChromaLocation = "left",
                Disposition = new FfprobeDisposition
                {
                    Default = 1,
                },
            };

        if (rotation is not null)
        {
            stream.SideDataList =
            [
                new FfprobeSideData
                {
                    SideDataType = "Display Matrix",
                    Rotation = rotation,
                },
            ];
        }

        return new FfprobeDocument
        {
            Format = new FfprobeFormat
            {
                FileName = @"C:\Fixtures\baseline.mp4",
                FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
                FormatLongName = "QuickTime / MOV",
                StartTime = "0.000000",
                Duration = "60.0",
                Size = "1000000",
                BitRate = "1000000",
                ProbeScore = 100,
            },
            Streams =
            [
                stream,
            ],
        };
    }

    private static FfprobeStream CreatePcmAudioStream(
        int index,
        string title)
    {
        return new FfprobeStream
        {
            Index = index,
            CodecName = "pcm_f32le",
            CodecLongName =
                "PCM 32-bit floating point little-endian",
            CodecType = "audio",
            SampleRate = "48000",
            Channels = 2,
            BitsPerSample = 32,
            BitRate = "3072000",
            Tags = new Dictionary<string, string>
            {
                ["title"] = title,
            },
            Disposition = new FfprobeDisposition
            {
                Default = 0,
            },
        };
    }

    private static MediaInspectionManifest CreateManifest()
    {
        return new MediaInspectionManifest(
            "ReplayFoundry.FfprobeMediaProbe",
            "1.1.0",
            "ffprobe",
            "ffprobe version test",
            @"C:\Tools\ffprobe.exe",
            new DateTimeOffset(
                2026,
                7,
                25,
                12,
                0,
                0,
                TimeSpan.Zero));
    }
}
