using System.Globalization;
using System.Text;

namespace ReplayFoundry.Desktop.Platform.Intelligence;

/// <summary>The pinned MiniLM BertTokenizer: uncased, accent stripping, basic tokenization and greedy WordPiece.</summary>
internal sealed class BertWordPieceTokenizer
{
    internal const int MaximumTokens = 256;
    private readonly IReadOnlyDictionary<string, long> _vocabulary;

    internal BertWordPieceTokenizer(IReadOnlyList<string> vocabulary)
    {
        _vocabulary = vocabulary.Select((token, index) => (token, index))
            .ToDictionary(static item => item.token, static item => (long)item.index, StringComparer.Ordinal);
        foreach (string required in new[] { "[PAD]", "[UNK]", "[CLS]", "[SEP]" })
            if (!_vocabulary.ContainsKey(required)) throw new ArgumentException("The BERT vocabulary is incomplete.", nameof(vocabulary));
    }

    internal long[] Encode(string text)
    {
        var tokens = new List<long> { _vocabulary["[CLS]"] };
        foreach (string basic in BasicTokens(text))
        {
            var pieces = new List<long>();
            if (basic.Length > 100) pieces.Add(_vocabulary["[UNK]"]);
            else
            {
                int start = 0;
                while (start < basic.Length)
                {
                    int end = basic.Length;
                    long id = 0;
                    while (end > start && !_vocabulary.TryGetValue((start == 0 ? "" : "##") + basic[start..end], out id)) end--;
                    if (end == start) { pieces.Clear(); pieces.Add(_vocabulary["[UNK]"]); break; }
                    pieces.Add(id);
                    start = end;
                }
            }
            foreach (long piece in pieces)
            {
                if (tokens.Count == MaximumTokens - 1) break;
                tokens.Add(piece);
            }
            if (tokens.Count == MaximumTokens - 1) break;
        }
        tokens.Add(_vocabulary["[SEP]"]);
        return tokens.ToArray();
    }

    internal static IEnumerable<string> BasicTokens(string text)
    {
        var current = new StringBuilder();
        Rune[] runes = text.ToLowerInvariant().Normalize(NormalizationForm.FormD).EnumerateRunes().ToArray();
        foreach (Rune rune in runes)
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (Rune.IsWhiteSpace(rune) || IsChinese(rune.Value) || IsPunctuation(rune.Value, category))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                if (!Rune.IsWhiteSpace(rune)) yield return rune.ToString();
            }
            else if (rune.Value is not (0 or 0xfffd) &&
                     category is not (UnicodeCategory.Control or UnicodeCategory.Format))
                current.Append(rune.ToString());
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static bool IsPunctuation(int value, UnicodeCategory category) =>
        value is >= 33 and <= 47 or >= 58 and <= 64 or >= 91 and <= 96 or >= 123 and <= 126 ||
        category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or
            UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or
            UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;

    private static bool IsChinese(int value) =>
        value is >= 0x4e00 and <= 0x9fff or >= 0x3400 and <= 0x4dbf or >= 0x20000 and <= 0x2a6df or
            >= 0x2a700 and <= 0x2b73f or >= 0x2b740 and <= 0x2b81f or >= 0x2b820 and <= 0x2ceaf or
            >= 0xf900 and <= 0xfaff or >= 0x2f800 and <= 0x2fa1f;
}
