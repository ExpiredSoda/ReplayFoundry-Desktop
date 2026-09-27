using System.Text;
using System.Text.Json.Nodes;

namespace ReplayFoundry.Desktop.Platform.Storage;

/// <summary>Lossless edit evidence, not an inferred explanation of the author's intent.</summary>
internal static class EditorialWordingEditTrace
{
    public static JsonObject Create(JsonNode before, JsonNode after, string? parentExampleId)
    {
        var fields = new JsonArray();
        foreach (string field in new[] { "titleBody", "description" })
        {
            string original = before[field]!.GetValue<string>();
            string revised = after[field]!.GetValue<string>();
            if (original == revised) continue;
            fields.Add(new JsonObject
            {
                ["field"] = field, ["before"] = original, ["after"] = revised,
                ["operations"] = Difference(original, revised),
            });
        }
        return new JsonObject
        {
            ["schema"] = "foundry-wording-edits-1", ["algorithm"] = "unicode-lcs-1",
            ["parentExampleId"] = parentExampleId, ["fields"] = fields,
        };
    }

    private static JsonArray Difference(string before, string after)
    {
        // Rune indices keep emoji and supplementary characters intact. No
        // whitespace normalization: removals/additions must reconstruct both sides.
        Rune[] left = before.EnumerateRunes().ToArray(), right = after.EnumerateRunes().ToArray();
        int prefix = 0, suffix = 0;
        while (prefix < Math.Min(left.Length, right.Length) && left[prefix] == right[prefix]) prefix++;
        while (suffix < Math.Min(left.Length, right.Length) - prefix &&
            left[^(suffix + 1)] == right[^(suffix + 1)]) suffix++;
        int leftEnd = left.Length - suffix, rightEnd = right.Length - suffix;
        var lengths = new ushort[leftEnd - prefix + 1, rightEnd - prefix + 1];
        for (int i = leftEnd - 1; i >= prefix; i--)
            for (int j = rightEnd - 1; j >= prefix; j--)
                lengths[i - prefix, j - prefix] = (ushort)(left[i] == right[j]
                    ? lengths[i - prefix + 1, j - prefix + 1] + 1
                    : Math.Max(lengths[i - prefix + 1, j - prefix], lengths[i - prefix, j - prefix + 1]));
        var result = new JsonArray();
        string? kind = null;
        var text = new StringBuilder();
        void Append(string next, Rune rune)
        {
            if (kind != next && text.Length > 0)
            {
                result.Add(new JsonObject { ["op"] = kind, ["text"] = text.ToString() });
                text.Clear();
            }
            kind = next;
            text.Append(rune.ToString());
        }
        for (int i = 0; i < prefix; i++) Append("keep", left[i]);
        int a = prefix, b = prefix;
        while (a < leftEnd || b < rightEnd)
        {
            if (a < leftEnd && b < rightEnd && left[a] == right[b])
            { Append("keep", left[a++]); b++; }
            else if (a < leftEnd && (b == rightEnd || lengths[a - prefix + 1, b - prefix] >= lengths[a - prefix, b - prefix + 1]))
                Append("remove", left[a++]);
            else Append("add", right[b++]);
        }
        for (int i = leftEnd; i < left.Length; i++) Append("keep", left[i]);
        if (text.Length > 0) result.Add(new JsonObject { ["op"] = kind, ["text"] = text.ToString() });
        return result;
    }
}
