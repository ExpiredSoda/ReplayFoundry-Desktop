using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.CompositionTests;

internal static class NormalizedRectangleTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Normalized rectangle preserves valid geometry", PreservesValidGeometry),
        new("Normalized rectangle rejects non-finite values", RejectsNonFiniteValues),
        new("Normalized rectangle rejects negative and out-of-bounds geometry", RejectsOutOfBoundsGeometry),
        new("Normalized rectangle rejects zero dimensions", RejectsZeroDimensions),
        new("Normalized rectangle exposes overlap without forbidding it", ExposesOverlap),
    ];

    private static void PreservesValidGeometry()
    {
        var rectangle = new NormalizedRectangle(0.125, 0.25, 0.375, 0.5);

        TestAssert.Equal(0.125, rectangle.X, "X should be preserved.");
        TestAssert.Equal(0.25, rectangle.Y, "Y should be preserved.");
        TestAssert.Equal(0.375, rectangle.Width, "Width should be preserved.");
        TestAssert.Equal(0.5, rectangle.Height, "Height should be preserved.");
        TestAssert.Equal(0.5, rectangle.Right, "Right should be derived.");
        TestAssert.Equal(0.75, rectangle.Bottom, "Bottom should be derived.");
        TestAssert.Equal(0.1875, rectangle.Area, "Area should be derived.");
        TestAssert.True(
            NormalizedRectangle.FullFrame.Contains(rectangle),
            "The full frame should contain valid normalized geometry.");
    }

    private static void RejectsNonFiniteValues()
    {
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(double.NaN, 0, 1, 1),
            "NaN X should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(0, double.PositiveInfinity, 1, 1),
            "Infinite Y should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(0, 0, double.NegativeInfinity, 1),
            "Infinite width should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(0, 0, 1, double.NaN),
            "NaN height should be rejected.");
    }

    private static void RejectsOutOfBoundsGeometry()
    {
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(-0.1, 0, 0.5, 1),
            "Negative X should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(0, -0.1, 1, 0.5),
            "Negative Y should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(1, 0, 0.1, 1),
            "X at one should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(0.8, 0, 0.3, 1),
            "X plus width beyond one should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(0, 0.8, 1, 0.3),
            "Y plus height beyond one should be rejected.");
    }

    private static void RejectsZeroDimensions()
    {
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(0, 0, 0, 1),
            "Zero width should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new NormalizedRectangle(0, 0, 1, 0),
            "Zero height should be rejected.");
    }

    private static void ExposesOverlap()
    {
        var first = new NormalizedRectangle(0, 0, 0.75, 0.75);
        var second = new NormalizedRectangle(0.5, 0.5, 0.5, 0.5);

        TestAssert.True(first.Intersects(second), "Overlapping regions should be detectable.");
        TestAssert.False(
            first.Contains(second),
            "Intersection must remain distinct from containment.");
    }
}
