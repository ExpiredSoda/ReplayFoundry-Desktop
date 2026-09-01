using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.CompositionTests;

internal static class CompositionCoverageTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Manual coverage has no invented sampling metrics", ManualCoverageHasNoSamplingMetrics),
        new("Full-timeline coverage preserves sampling facts", SampledCoveragePreservesFacts),
        new("Sample outcomes must account for every request", RequiresCompleteSampleAccounting),
        new("Sampled coverage requires valid decoded dimensions", RequiresDecodedDimensions),
        new("Sampled coverage requires valid intervals and gap", RequiresValidIntervalsAndGap),
        new("Dense coverage windows must stay within the source", RejectsWindowOutsideSource),
        new("Dense coverage windows must be ordered and non-overlapping", RejectsUnorderedOrOverlappingWindows),
        new("Adjacent dense coverage windows are valid", AllowsAdjacentWindows),
    ];

    private static void ManualCoverageHasNoSamplingMetrics()
    {
        var coverage = CompositionCoverage.CreateManual(CompositionTestData.SourceDuration);

        TestAssert.Equal(
            CompositionCoverageKind.Manual,
            coverage.Kind,
            "Manual coverage kind should be explicit.");
        TestAssert.Equal(TimeSpan.Zero, coverage.Start, "Coverage should begin at zero.");
        TestAssert.Equal(
            CompositionTestData.SourceDuration,
            coverage.End,
            "Coverage should describe the full source timeline.");
        TestAssert.Equal<int?>(null, coverage.RequestedSampleCount, "No sampling count is known.");
        TestAssert.Equal(0, coverage.DenseCoverageWindows.Count, "No dense windows are implied.");
    }

    private static void SampledCoveragePreservesFacts()
    {
        var requestedInterval = TimeSpan.FromSeconds(5);
        var actualInterval = TimeSpan.FromSeconds(5.1);
        var maximumGap = TimeSpan.FromSeconds(5.2);
        var window = new CompositionCoverageWindow(
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(3));

        var coverage = CompositionCoverage.CreateFullTimelineSampled(
            CompositionTestData.SourceDuration,
            requestedInterval,
            actualInterval,
            requestedSampleCount: 120,
            decodedSampleCount: 119,
            failedSampleCount: 1,
            decodedWidth: 1080,
            decodedHeight: 1920,
            pixelFormat: " yuv420p ",
            maximumGap,
            [window]);

        TestAssert.Equal(requestedInterval, coverage.RequestedSampleInterval, "Requested interval.");
        TestAssert.Equal(actualInterval, coverage.ActualSampleInterval, "Actual interval.");
        TestAssert.Equal<int?>(120, coverage.RequestedSampleCount, "Requested samples.");
        TestAssert.Equal<int?>(119, coverage.DecodedSampleCount, "Decoded samples.");
        TestAssert.Equal<int?>(1, coverage.FailedSampleCount, "Failed samples.");
        TestAssert.Equal<int?>(1080, coverage.DecodedWidth, "Decoded width.");
        TestAssert.Equal<int?>(1920, coverage.DecodedHeight, "Decoded height.");
        TestAssert.Equal("yuv420p", coverage.PixelFormat, "Pixel format should be normalized.");
        TestAssert.Equal(maximumGap, coverage.MaximumSampleGap, "Maximum gap.");
        TestAssert.Same(window, coverage.DenseCoverageWindows[0], "Window identity should remain.");
    }

    private static void RequiresCompleteSampleAccounting()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = CreateSampledCoverage(
                requestedSampleCount: 10,
                decodedSampleCount: 8,
                failedSampleCount: 1),
            "Unaccounted sample requests should be rejected.");
        TestAssert.Throws<ArgumentException>(
            () => _ = CreateSampledCoverage(
                requestedSampleCount: 10,
                decodedSampleCount: 10,
                failedSampleCount: 1),
            "Sample outcomes beyond the request count should be rejected.");
    }

    private static void RequiresDecodedDimensions()
    {
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateSampledCoverage(decodedWidth: 0),
            "Decoded width must be positive.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateSampledCoverage(decodedHeight: -1),
            "Decoded height must be positive.");
    }

    private static void RequiresValidIntervalsAndGap()
    {
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateSampledCoverage(requestedSampleInterval: TimeSpan.Zero),
            "Requested interval must be positive.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateSampledCoverage(actualSampleInterval: TimeSpan.Zero),
            "Actual interval must be positive.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateSampledCoverage(maximumSampleGap: TimeSpan.FromMinutes(11)),
            "Maximum gap cannot exceed the source duration.");
    }

    private static void RejectsWindowOutsideSource()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = CreateSampledCoverage(
                windows:
                [
                    new CompositionCoverageWindow(
                        TimeSpan.FromMinutes(9),
                        TimeSpan.FromMinutes(11)),
                ]),
            "Dense windows must remain within the source.");
    }

    private static void RejectsUnorderedOrOverlappingWindows()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = CreateSampledCoverage(
                windows:
                [
                    new CompositionCoverageWindow(
                        TimeSpan.FromMinutes(3),
                        TimeSpan.FromMinutes(4)),
                    new CompositionCoverageWindow(
                        TimeSpan.FromMinutes(2),
                        TimeSpan.FromMinutes(2.5)),
                ]),
            "Dense windows should be chronological.");

        TestAssert.Throws<ArgumentException>(
            () => _ = CreateSampledCoverage(
                windows:
                [
                    new CompositionCoverageWindow(
                        TimeSpan.FromMinutes(2),
                        TimeSpan.FromMinutes(4)),
                    new CompositionCoverageWindow(
                        TimeSpan.FromMinutes(3),
                        TimeSpan.FromMinutes(5)),
                ]),
            "Dense windows should not overlap.");
    }

    private static void AllowsAdjacentWindows()
    {
        var coverage = CreateSampledCoverage(
            windows:
            [
                new CompositionCoverageWindow(
                    TimeSpan.FromMinutes(2),
                    TimeSpan.FromMinutes(3)),
                new CompositionCoverageWindow(
                    TimeSpan.FromMinutes(3),
                    TimeSpan.FromMinutes(4)),
            ]);

        TestAssert.Equal(
            2,
            coverage.DenseCoverageWindows.Count,
            "Adjacent dense windows should preserve distinct evidence windows.");
    }

    private static CompositionCoverage CreateSampledCoverage(
        TimeSpan? requestedSampleInterval = null,
        TimeSpan? actualSampleInterval = null,
        int requestedSampleCount = 10,
        int decodedSampleCount = 10,
        int failedSampleCount = 0,
        int decodedWidth = 1080,
        int decodedHeight = 1920,
        TimeSpan? maximumSampleGap = null,
        IEnumerable<CompositionCoverageWindow>? windows = null) =>
        CompositionCoverage.CreateFullTimelineSampled(
            CompositionTestData.SourceDuration,
            requestedSampleInterval ?? TimeSpan.FromSeconds(5),
            actualSampleInterval ?? TimeSpan.FromSeconds(5),
            requestedSampleCount,
            decodedSampleCount,
            failedSampleCount,
            decodedWidth,
            decodedHeight,
            "yuv420p",
            maximumSampleGap ?? TimeSpan.FromSeconds(5),
            windows);
}
