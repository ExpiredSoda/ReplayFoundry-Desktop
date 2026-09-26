using System.Text;
using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataVisualEventSelection
{
    private static readonly HashSet<string> DanglingActionEndings =
        new(StringComparer.Ordinal)
        {
            "a", "an", "and", "as", "at", "by", "for", "from", "in",
            "into", "of", "on", "or", "the", "to", "with",
        };

    internal static Qwen3VlGroundedMetadataVisualEventSelectionOutcome
        SelectPrimaryVisualDraft(
            IReadOnlyList<Qwen3VlGroundedMetadataVisualEventAssessment> assessments,
            IReadOnlyList<Qwen3VlGroundedMetadataVisualDraft>? drafts = null,
            bool allowGroundedPrimaryWithoutDistinctSupport = false)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        if (drafts is not null && drafts.Count != assessments.Count)
        {
            throw new ArgumentException(
                "Visual-draft quality parity requires one draft per assessment.",
                nameof(drafts));
        }
        Qwen3VlGroundedMetadataVisualEventAssessment[] eligible = assessments
            .Where(static value => value.HasDistinctEventSupport)
            .ToArray();
        bool selectedWithoutDistinctSupport = eligible.Length == 0;
        if (selectedWithoutDistinctSupport &&
            !allowGroundedPrimaryWithoutDistinctSupport)
        {
            return new(
                Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode
                    .NoDistinctPrimaryEvent,
                null);
        }
        IReadOnlyList<Qwen3VlGroundedMetadataVisualEventAssessment> candidates =
            selectedWithoutDistinctSupport ? assessments : eligible;
        int primaryOrdinal = candidates
            .Select(value => new
            {
                Assessment = value,
                QualityPenalty = drafts is null
                    ? 0
                    : VisualActionQualityPenalty(drafts[value.Ordinal - 1]),
            })
            .OrderByDescending(static value =>
                value.Assessment.Score - 2 * value.QualityPenalty)
            .ThenBy(static value => value.QualityPenalty)
            .ThenByDescending(static value => value.Assessment.DistinctAction)
            .ThenByDescending(static value => value.Assessment.VisibleOutcome)
            .ThenByDescending(static value => value.Assessment.ObjectInteraction)
            .ThenByDescending(static value =>
                value.Assessment.ReadableInterfaceChange)
            .ThenBy(static value =>
                value.Assessment.RoutineOnly || value.Assessment.Uncertain)
            .ThenByDescending(static value => value.Assessment.Ordinal)
            .First()
            .Assessment.Ordinal;
        return new(
            selectedWithoutDistinctSupport
                ? Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode
                    .SelectedGroundedPrimaryEvent
                : Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode
                    .SelectedDistinctPrimaryEvent,
            primaryOrdinal);
    }

    private static int VisualActionQualityPenalty(
        Qwen3VlGroundedMetadataVisualDraft draft)
    {
        int penalty = 0;
        foreach (string rawAction in draft.Actions)
        {
            string action = string.Join(' ', rawAction
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            string[] words = Regex.Matches(
                    action.ToLowerInvariant(),
                    @"[\p{L}\p{N}_'’]+")
                .Select(static match => match.Value)
                .ToArray();
            int structuralSingleQuotes = Regex.Replace(
                    action,
                    @"(?<=\w)'(?=\w)",
                    string.Empty)
                .Count(static character => character == '\'');
            if (ContainsNonLatinLetter(action))
            {
                penalty += 2;
            }
            if (string.IsNullOrEmpty(action) ||
                action.Length > 0 && ",:;/-–—".Contains(action[^1]) ||
                words.Length > 0 && DanglingActionEndings.Contains(words[^1]) ||
                structuralSingleQuotes % 2 == 1 ||
                action.Count(static character => character == '"') % 2 == 1)
            {
                penalty++;
            }
        }
        return penalty;
    }

    private static bool ContainsNonLatinLetter(string value)
    {
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
            {
                continue;
            }
            int scalar = rune.Value;
            if (scalar is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or
                >= 0x00C0 and <= 0x024F or >= 0x1E00 and <= 0x1EFF)
            {
                continue;
            }
            return true;
        }
        return false;
    }
}
