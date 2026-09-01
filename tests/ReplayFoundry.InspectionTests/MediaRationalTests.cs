using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.InspectionTests;

internal static class MediaRationalTests
{
    public static IEnumerable<TestCase> GetTests()
    {
        yield return new TestCase(
            "MediaRational reduces and formats values",
            ReducesAndFormatsValues);

        yield return new TestCase(
            "MediaRational parses slash and colon separators",
            ParsesSlashAndColonSeparators);

        yield return new TestCase(
            "MediaRational rejects zero frame-rate values",
            RejectsZeroValues);
    }

    private static void ReducesAndFormatsValues()
    {
        var rational =
            new MediaRational(
                60000,
                1000);

        TestAssert.Equal(
            60L,
            rational.Numerator,
            "The numerator should be reduced.");

        TestAssert.Equal(
            1L,
            rational.Denominator,
            "The denominator should be reduced.");

        TestAssert.Equal(
            "60/1",
            rational.ToString(),
            "The default format should use a slash.");

        TestAssert.Equal(
            "60:1",
            rational.ToString("A", null),
            "The aspect format should use a colon.");
    }

    private static void ParsesSlashAndColonSeparators()
    {
        TestAssert.True(
            MediaRational.TryParse(
                "60000/1001",
                out MediaRational frameRate),
            "A valid frame-rate rational should parse.");

        TestAssert.Equal(
            new MediaRational(60000, 1001),
            frameRate,
            "The exact frame-rate rational should be preserved.");

        TestAssert.True(
            MediaRational.TryParse(
                "9:16",
                out MediaRational aspectRatio),
            "A valid aspect ratio should parse.");

        TestAssert.Equal(
            new MediaRational(9, 16),
            aspectRatio,
            "The aspect ratio should be preserved.");
    }

    private static void RejectsZeroValues()
    {
        TestAssert.False(
            MediaRational.TryParse(
                "0/0",
                out _),
            "A zero frame-rate rational must remain unavailable.");

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => new MediaRational(0, 1),
            "The constructor must reject a zero numerator.");
    }
}
