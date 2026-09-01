using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.PreparationTests;

internal static class FfmpegPreviewCommandBuilderTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Preview command maps the exact primary stream and preserves ArgumentList paths",
            MapsPrimaryStreamAndPath),
        new(
            "Preview command derives bounded even 9:16 dimensions",
            CalculatesPortraitDimensions),
        new(
            "Preview command preserves 16:9 dimensions",
            CalculatesLandscapeDimensions),
        new(
            "Preview command applies rotation to reported display aspect ratio",
            AppliesRotationToReportedAspectRatio),
        new(
            "Preview command crops confirmed Gameplay geometry before bounded scaling",
            CropsEffectiveDisplayRegion),
        new(
            "PNG dimension reader validates signature IHDR and dimensions",
            ValidatesPngHeader),
    ];

    private static Task MapsPrimaryStreamAndPath()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "source with spaces.mkv");

        var media =
            TestMediaFactory.Create(
                path,
                videoStreamIndex: 3);

        var request =
            new VideoPreviewFrameRequest(
                media,
                TimeSpan.FromSeconds(12));

        string outputPath =
            TestMediaFactory.CreateSourcePath(
                "preview output.png");

        FfmpegPreviewCommand command =
            FfmpegPreviewCommandBuilder.Build(
                request,
                outputPath);

        var arguments =
            command.Arguments.ToList();

        int seekIndex =
            arguments.IndexOf("-ss");

        int inputIndex =
            arguments.IndexOf("-i");

        int mapIndex =
            arguments.IndexOf("-map");

        int framesIndex =
            arguments.IndexOf("-frames:v");

        int filterIndex =
            arguments.IndexOf("-vf");

        TestAssert.True(
            seekIndex >= 0 &&
            seekIndex < inputIndex,
            "Input seeking should avoid decoding from source zero when possible.");

        TestAssert.Equal(
            path,
            command.Arguments[inputIndex + 1],
            "Input path should remain one ArgumentList value.");

        TestAssert.Equal(
            "0:3",
            command.Arguments[mapIndex + 1],
            "Exact absolute primary stream should be mapped.");

        TestAssert.Equal(
            "1",
            command.Arguments[framesIndex + 1],
            "Exactly one frame should be requested.");

        TestAssert.True(
            command.Arguments[filterIndex + 1].Contains(
                "setsar=1",
                StringComparison.Ordinal),
            "Output pixels should be square.");

        TestAssert.False(
            command.Arguments.Contains(
                "-noautorotate",
                StringComparer.Ordinal),
            "FFmpeg normal autorotation should remain enabled.");

        TestAssert.Equal(
            outputPath,
            command.Arguments[^1],
            "Output path should remain one ArgumentList value.");

        return Task.CompletedTask;
    }

    private static Task CalculatesPortraitDimensions()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "portrait.mkv");

        var media =
            TestMediaFactory.Create(
                path,
                width: 1080,
                height: 1920,
                displayAspectRatio:
                    new MediaRational(9, 16),
                displayAspectRatioSource:
                    MediaValueSource.DerivedFromDimensions);

        var request =
            new VideoPreviewFrameRequest(
                media,
                TimeSpan.Zero);

        FfmpegPreviewCommand command =
            FfmpegPreviewCommandBuilder.Build(
                request,
                TestMediaFactory.CreateSourcePath(
                    "portrait.png"));

        TestAssert.Equal(
            404,
            command.ExpectedWidth,
            "9:16 preview should fit inside 1280x720 with an even width.");

        TestAssert.Equal(
            720,
            command.ExpectedHeight,
            "Portrait preview should use the full height.");

        return Task.CompletedTask;
    }

    private static Task CalculatesLandscapeDimensions()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "landscape.mkv");

        var media =
            TestMediaFactory.Create(
                path,
                width: 1920,
                height: 1080,
                displayAspectRatio:
                    new MediaRational(16, 9));

        var request =
            new VideoPreviewFrameRequest(
                media,
                TimeSpan.Zero);

        FfmpegPreviewCommand command =
            FfmpegPreviewCommandBuilder.Build(
                request,
                TestMediaFactory.CreateSourcePath(
                    "landscape.png"));

        TestAssert.Equal(
            1280,
            command.ExpectedWidth,
            "Landscape preview should use the full width.");

        TestAssert.Equal(
            720,
            command.ExpectedHeight,
            "Landscape preview should preserve 16:9.");

        return Task.CompletedTask;
    }

    private static Task AppliesRotationToReportedAspectRatio()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "rotated.mp4");

        var media =
            TestMediaFactory.Create(
                path,
                width: 1920,
                height: 1080,
                displayAspectRatio:
                    new MediaRational(16, 9),
                displayAspectRatioSource:
                    MediaValueSource.ReportedByProbe,
                rotationDegrees: 90);

        var request =
            new VideoPreviewFrameRequest(
                media,
                TimeSpan.Zero);

        FfmpegPreviewCommand command =
            FfmpegPreviewCommandBuilder.Build(
                request,
                TestMediaFactory.CreateSourcePath(
                    "rotated.png"));

        TestAssert.Equal(
            404,
            command.ExpectedWidth,
            "A quarter-turn should invert a reported 16:9 display ratio.");

        TestAssert.Equal(
            720,
            command.ExpectedHeight,
            "Rotated output should fit the portrait preview height.");

        return Task.CompletedTask;
    }

    private static Task ValidatesPngHeader()
    {
        byte[] valid =
            TestMediaFactory.CreatePngHeader(
                1280,
                720);

        (int width, int height) =
            PngDimensionsReader.Read(
                valid);

        TestAssert.Equal(
            1280,
            width,
            "PNG width should be read from IHDR.");

        TestAssert.Equal(
            720,
            height,
            "PNG height should be read from IHDR.");

        TestAssert.Throws<InvalidDataException>(
            () => PngDimensionsReader.Read(
                [1, 2, 3]),
            "Truncated PNG data should fail.");

        byte[] invalidSignature =
            valid.ToArray();
        invalidSignature[0] = 0;

        TestAssert.Throws<InvalidDataException>(
            () => PngDimensionsReader.Read(
                invalidSignature),
            "Invalid PNG signature should fail.");

        byte[] invalidIhdrLength =
            valid.ToArray();
        invalidIhdrLength[11] = 12;

        TestAssert.Throws<InvalidDataException>(
            () => PngDimensionsReader.Read(
                invalidIhdrLength),
            "Invalid IHDR length should fail.");

        byte[] invalidDimensions =
            valid.ToArray();
        Array.Clear(
            invalidDimensions,
            16,
            4);

        TestAssert.Throws<InvalidDataException>(
            () => PngDimensionsReader.Read(
                invalidDimensions),
            "Non-positive PNG dimensions should fail.");

        return Task.CompletedTask;
    }

    private static Task CropsEffectiveDisplayRegion()
    {
        var media = TestMediaFactory.Create(
            TestMediaFactory.CreateSourcePath("vertical-gameplay.mkv"),
            width: 1080,
            height: 1920,
            displayAspectRatio: new MediaRational(9, 16),
            displayAspectRatioSource: MediaValueSource.DerivedFromDimensions);
        var region = new NormalizedRectangle(.075, .125, .85, .425);
        var request = new VideoPreviewFrameRequest(
            media,
            TimeSpan.FromSeconds(5),
            contentRegion: region);

        FfmpegPreviewCommand command = FfmpegPreviewCommandBuilder.Build(
            request,
            TestMediaFactory.CreateSourcePath("gameplay-crop.png"));
        string filter = command.Arguments[
            command.Arguments.ToList().IndexOf("-vf") + 1];

        TestAssert.True(
            filter.Contains("crop=920:816:80:240", StringComparison.Ordinal),
            "Crop must use shared effective-display rounding semantics.");
        TestAssert.Equal(810, command.ExpectedWidth, "Cropped output width.");
        TestAssert.Equal(720, command.ExpectedHeight, "Cropped output height.");
        return Task.CompletedTask;
    }
}
