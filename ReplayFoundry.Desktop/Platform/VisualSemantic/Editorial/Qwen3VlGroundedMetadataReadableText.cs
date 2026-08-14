using System.Text;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataReadableText
{
    internal static IReadOnlyList<string> FindStable(
        IReadOnlyList<Qwen3VlGroundedMetadataVisualDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        var firstValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var orderedKeys = new List<string>();
        var draftOrdinals = new Dictionary<string, HashSet<int>>(
            StringComparer.Ordinal);
        foreach (Qwen3VlGroundedMetadataVisualDraft draft in drafts)
        {
            foreach (string value in draft.ReadableText)
            {
                string normalized = string.Join(
                        ' ',
                        value.Normalize(NormalizationForm.FormKC).Split(
                            (char[]?)null,
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries))
                    .Trim();
                if (normalized.Length < 4 || !normalized.Any(char.IsLetter))
                {
                    continue;
                }
                string key = normalized.ToLowerInvariant();
                if (firstValues.TryAdd(key, normalized))
                {
                    orderedKeys.Add(key);
                }
                if (!draftOrdinals.TryGetValue(key, out HashSet<int>? ordinals))
                {
                    ordinals = [];
                    draftOrdinals.Add(key, ordinals);
                }
                ordinals.Add(draft.Ordinal);
            }
        }
        return orderedKeys
            .Where(key => draftOrdinals[key].Count >= 2)
            .Select(key => firstValues[key])
            .Take(4)
            .ToArray();
    }
}
