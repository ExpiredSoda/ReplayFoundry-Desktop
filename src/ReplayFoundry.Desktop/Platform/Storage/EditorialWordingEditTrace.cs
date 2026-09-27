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
        var lengths = new int[left.Length + 1, right.Length + 1];
        for (int i = left.Length - 1; i >= 0; i--)
            for (int j = right.Length - 1; j >= 0; j--)
                lengths[i, j] = left[i] == right[j] ? lengths[i + 1, j + 1] + 1 :
                    Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
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
        int a = 0, b = 0;
        while (a < left.Length || b < right.Length)
        {
            if (a < left.Length && b < right.Length && left[a] == right[b])
            { Append("keep", left[a++]); b++; }
            else if (a < left.Length && (b == right.Length || lengths[a + 1, b] >= lengths[a, b + 1]))
                Append("remove", left[a++]);
            else Append("add", right[b++]);
        }
        if (text.Length > 0) result.Add(new JsonObject { ["op"] = kind, ["text"] = text.ToString() });
        return result;
    }
}
