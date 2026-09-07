using System.Text;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.ClipGoals;

internal static class DiscoverySearchText
{
    public static string Format(string meaning, string exactWords) => string.Join(" ",
        new[] { Escape(meaning) }.Concat(exactWords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(phrase => "\"" + Escape(phrase) + "\""))).Trim();

    public static bool TryParse(string text, out string meaning, out string exactWords, out string? error)
    {
        var description = new StringBuilder();
        var phrase = new StringBuilder();
        var phrases = new List<string>();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\\' && i + 1 < text.Length && text[i + 1] is '\\' or '"')
                (quoted ? phrase : description).Append(text[++i]);
            else if (ch == '"')
            {
                if (quoted) { phrases.Add(phrase.ToString().Trim()); phrase.Clear(); }
                else description.Append(' ');
                quoted = !quoted;
            }
            else (quoted ? phrase : description).Append(ch);
        }
        meaning = description.ToString().Trim();
        exactWords = string.Join(", ", phrases.Where(value => value.Length > 0));
        error = quoted ? "Add a closing quote after the words you want to hear."
            : meaning.Length > 240 ? "Shorten your description to 240 characters."
            : exactWords.Length > 240 ? "Shorten the quoted phrases to 240 characters in total." : null;
        return error is null;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
