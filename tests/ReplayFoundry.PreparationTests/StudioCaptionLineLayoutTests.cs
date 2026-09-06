using System.Globalization;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.PreparationTests;

internal static class StudioCaptionLineLayoutTests
{
    public static IEnumerable<TestCase> GetTests()
    {
        yield return new("Caption line spacing changes line positions without scaling glyphs or changing wrapping", SpacingPreservesWrapping);
        yield return new("Caption explicit wrapping retains graphemes and source highlight offsets", GraphemesAndOffsetsSurvive);
    }

    private static Task SpacingPreservesWrapping()
    {
        const string text = "one two three four five six seven eight";
        var normal = StudioCaptionLineLayout.Create(text, new(lineSpacingPercent: 100), 32, 170);
        var spaced = StudioCaptionLineLayout.Create(text, new(lineSpacingPercent: 150), 32, 170);
        TestAssert.True(normal.Lines.Count > 1, "The fixture must exercise multiple measured lines.");
        TestAssert.Equal(normal.Lines.Count, spaced.Lines.Count, "Line spacing cannot alter wrapping.");
        TestAssert.Equal(normal.LineHeight, spaced.LineHeight, "Glyph height remains unchanged.");
        TestAssert.Equal(normal.LineStride * 1.5, spaced.LineStride, "Spacing controls the distance between lines.");
        TestAssert.Equal(spaced.LineHeight + (spaced.Lines.Count - 1) * spaced.LineStride, spaced.Height,
            "The block height includes the exact shared line offsets.");
        for (int i = 0; i < normal.Lines.Count; i++)
        {
            TestAssert.Equal(normal.Lines[i].Text, spaced.Lines[i].Text, "Preview and ASS consume the same explicit text lines.");
            TestAssert.Equal(normal.Lines[i].Width, spaced.Lines[i].Width, "Line spacing cannot stretch text width.");
        }
        return Task.CompletedTask;
    }

    private static Task GraphemesAndOffsetsSurvive()
    {
        const string text = "e\u0301 👩‍🚀 漢字";
        var layout = StudioCaptionLineLayout.Create(text, StudioCaptionTypography.Default, 32, 1);
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToHashSet();
        TestAssert.True(layout.Lines.Any(static line => line.Text == "e\u0301") &&
            layout.Lines.Any(static line => line.Text == "👩‍🚀"), "Narrow widths preserve complete combining marks and emoji sequences.");
        foreach (var line in layout.Lines)
        {
            TestAssert.True(boundaries.Contains(line.StartIndex) && boundaries.Contains(line.StartIndex + line.Length),
                "Highlight offsets cannot point inside a grapheme.");
            TestAssert.Equal(text.Substring(line.StartIndex, line.Length), line.Text,
                "Every line preserves its original UTF-16 text offsets.");
        }
        var paragraphs = StudioCaptionLineLayout.Create("first\r\n\r\nlast", StudioCaptionTypography.Default, 32, 1000);
        TestAssert.Equal(3, paragraphs.Lines.Count, "Explicit empty paragraphs must retain their vertical spacing.");
        TestAssert.Equal(9, paragraphs.Lines[2].StartIndex, "CRLF delimiters preserve exact source indices.");
        return Task.CompletedTask;
    }
}
