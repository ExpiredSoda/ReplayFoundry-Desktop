using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.PreparationTests;

internal static class VideoPreviewFrameRequestTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Preview request accepts an in-range timestamp and practical defaults",
            AcceptsValidTimestamp),
        new(
            "Preview request rejects negative and source-end timestamps",
            RejectsInvalidTimestamp),
        new(
            "Preview request requires bounded positive even dimensions",
            ValidatesDimensions),
    ];

    private static Task AcceptsValidTimestamp()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "valid.mkv");

        var media =
            TestMediaFactory.Create(path);

        var request =
            new VideoPreviewFrameRequest(
                media,
                TimeSpan.FromSeconds(30));

        TestAssert.Same(
            media,
            request.Media,
            "Media identity should be preserved.");

        TestAssert.Equal(
            1280,
            request.MaximumWidth,
            "Default preview width should be practical.");

        TestAssert.Equal(
            720,
            request.MaximumHeight,
            "Default preview height should be practical.");

        var region = new NormalizedRectangle(.1, .2, .7, .5);
        var cropped = new VideoPreviewFrameRequest(
            media,
            TimeSpan.FromSeconds(30),
            contentRegion: region);
        TestAssert.Same(
            region,
            cropped.ContentRegion!,
            "Confirmed crop identity should be retained.");

        return Task.CompletedTask;
    }

    private static Task RejectsInvalidTimestamp()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "timestamp.mkv");

        var media =
            TestMediaFactory.Create(
                path,
                TimeSpan.FromMinutes(1));

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new VideoPreviewFrameRequest(
                media,
                TimeSpan.FromTicks(-1)),
            "Negative timestamp should fail.");

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new VideoPreviewFrameRequest(
                media,
                media.Duration),
            "Source end should be exclusive.");

        return Task.CompletedTask;
    }

    private static Task ValidatesDimensions()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "dimensions.mkv");

        var media =
            TestMediaFactory.Create(path);

        TestAssert.Throws<ArgumentException>(
            () => _ = new VideoPreviewFrameRequest(
                media,
                TimeSpan.Zero,
                maximumWidth: 1279,
                maximumHeight: 720),
            "Odd dimensions should fail.");

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new VideoPreviewFrameRequest(
                media,
                TimeSpan.Zero,
                maximumWidth: 0,
                maximumHeight: 720),
            "Zero dimensions should fail.");

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new VideoPreviewFrameRequest(
                media,
                TimeSpan.Zero,
                maximumWidth: 8200,
                maximumHeight: 720),
            "Unbounded dimensions should fail.");

        return Task.CompletedTask;
    }
}
