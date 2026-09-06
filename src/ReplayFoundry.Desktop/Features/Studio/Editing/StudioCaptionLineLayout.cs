using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioCaptionLine(string Text, int StartIndex, int Length, double Width,
    double Height, double Top, double CenterOffset, double BaselineOffset);

public sealed record StudioCaptionLineLayoutResult(IReadOnlyList<StudioCaptionLine> Lines,
    double Height, double LineHeight, double LineStride);

/// <summary>Shared explicit line breaks and vertical positions for preview and ASS.</summary>
public static class StudioCaptionLineLayout
{
    public static StudioCaptionLineLayoutResult Create(string text, StudioCaptionTypography typography,
        double fontSize, double maximumWidth, double pixelsPerDip = 1)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(typography);
        if (!double.IsFinite(fontSize) || fontSize <= 0 || !double.IsFinite(maximumWidth) || maximumWidth <= 0 ||
            !double.IsFinite(pixelsPerDip) || pixelsPerDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (text.Length == 0) return new([], 0, 0, 0);
        var typeface = new Typeface(new FontFamily(StudioCaptionFontResolver.Resolve(typography.FontFamily).Family), FontStyles.Normal,
            typography.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        FormattedText Measure(string value) => new(value, CultureInfo.CurrentUICulture,
            typography.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            typeface, fontSize, Brushes.White, pixelsPerDip);
        var metrics = Measure("Mg");
        var measured = new List<(int Start, int Length, FormattedText Measure)>();
        int paragraphStart = 0;
        while (paragraphStart <= text.Length)
        {
            int newline = text.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029'], paragraphStart);
            int paragraphEnd = newline < 0 ? text.Length : newline;
            AddParagraph(paragraphStart, paragraphEnd);
            if (newline < 0) break;
            paragraphStart = newline + (text[newline] == '\r' && newline + 1 < text.Length && text[newline + 1] == '\n' ? 2 : 1);
        }
        double lineHeight = Math.Max(metrics.Height, measured.Select(static line => line.Measure.Height).DefaultIfEmpty(0).Max());
        double stride = lineHeight * typography.LineSpacingPercent / 100;
        double height = measured.Count == 0 ? 0 : lineHeight + (measured.Count - 1) * stride;
        var lines = measured.Select((line, index) =>
        {
            double actualHeight = line.Length == 0 ? metrics.Height : line.Measure.Height;
            double top = index * stride + (lineHeight - actualHeight) / 2;
            return new StudioCaptionLine(text.Substring(line.Start, line.Length), line.Start, line.Length,
                line.Measure.WidthIncludingTrailingWhitespace, actualHeight, top,
                top + actualHeight / 2 - height / 2,
                top + (line.Length == 0 ? metrics.Baseline : line.Measure.Baseline) - height / 2);
        }).ToArray();
        return new(Array.AsReadOnly(lines), height, lineHeight, stride);

        void AddParagraph(int start, int end)
        {
            int initialCount = measured.Count;
            int[] elements = StringInfo.ParseCombiningCharacters(text[start..end]);
            int index = 0;
            if (elements.Length == 0)
            {
                measured.Add((start, 0, Measure(string.Empty)));
                return;
            }
            while (index < elements.Length)
            {
                while (index < elements.Length && char.IsWhiteSpace(text[start + elements[index]])) index++;
                if (index == elements.Length) break;
                int current = start + elements[index];
                int low = index + 1, high = elements.Length, fit = index + 1;
                while (low <= high)
                {
                    int middle = low + (high - low) / 2;
                    int boundary = middle == elements.Length ? end : start + elements[middle];
                    if (Measure(text[current..boundary]).WidthIncludingTrailingWhitespace <= maximumWidth)
                    {
                        fit = middle;
                        low = middle + 1;
                    }
                    else high = middle - 1;
                }
                // Prefer a word boundary. If one grapheme is wider than the
                // available line, retain that entire grapheme rather than
                // breaking a surrogate pair, combining mark, or emoji sequence.
                if (fit < elements.Length)
                {
                    for (int boundary = fit; boundary > index; boundary--)
                    {
                        if (char.IsWhiteSpace(text[start + elements[boundary]]))
                        {
                            fit = boundary;
                            break;
                        }
                    }
                }
                int cut = fit == elements.Length ? end : start + elements[fit];
                int visibleEnd = cut;
                while (visibleEnd > current && char.IsWhiteSpace(text[visibleEnd - 1])) visibleEnd--;
                measured.Add((current, visibleEnd - current, Measure(text[current..visibleEnd])));
                index = fit;
            }
            if (measured.Count == initialCount) measured.Add((start, 0, Measure(string.Empty)));
        }
    }
}
