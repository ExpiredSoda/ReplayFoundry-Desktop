using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataActionStrengthPolicy
{
    private sealed record Rule(Regex Audience, Regex Support);

    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex DefeatAudience = new(
        @"\b(?:defeated|killed|destroyed|eliminated|vanquished|won)\b",
        Options);
    private static readonly Regex DefeatSupport = new(
        @"\b(?:defeat(?:s|ed|ing)?|kill(?:s|ed|ing)?|destroy(?:s|ed|ing)?|eliminat(?:e|es|ed|ing)|vanquish(?:es|ed|ing)?|win(?:s|ning)?|won|die|dies|died|dying|collaps(?:e|es|ed|ing)|health bar (?:is |was )?(?:empt(?:y|ies|ied|ying)|reach(?:es|ed|ing)? zero))\b",
        Options);
    private static readonly Regex EntryAudience = new(
        @"\b(?:entered|entering|passed through|passing through)\b",
        Options);
    private static readonly Regex EntrySupport = new(
        @"\b(?:enter(?:s|ed|ing)?|pass(?:es|ed|ing)? through|mov(?:e|es|ed|ing) (?:into|through)|walk(?:s|ed|ing)? (?:into|through)|(?:run|runs|ran|running) (?:into|through))\b",
        Options);
    private static readonly Regex ExplosionAudience = new(
        @"\b(?:exploded|detonated|blew up|burst apart)\b",
        Options);
    private static readonly Regex ExplosionSupport = new(
        @"\b(?:explod(?:e|es|ed|ing)|detonat(?:e|es|ed|ing)|(?:blow|blows|blew|blowing) up|(?:burst|bursts|bursting) apart)\b",
        Options);
    private static readonly Regex DisappearanceAudience = new(
        @"\b(?:disappeared|vanished|reappeared|rematerialized)\b",
        Options);
    private static readonly Regex DisappearanceSupport = new(
        @"\b(?:disappear(?:s|ed|ing)?|vanish(?:es|ed|ing)?|reappear(?:s|ed|ing)?|rematerializ(?:e|es|ed|ing))\b",
        Options);
    private static readonly Regex CompletionAudience = new(
        @"\b(?:completed|finished|cleared)\b",
        Options);
    private static readonly Regex CompletionSupport = new(
        @"\b(?:complet(?:e|es|ed|ing)|finish(?:es|ed|ing)?|clear(?:s|ed|ing)?)\b",
        Options);

    private static readonly IReadOnlyList<Rule> Rules =
    [
        new(
            DefeatAudience,
            DefeatSupport),
        new(
            EntryAudience,
            EntrySupport),
        new(
            ExplosionAudience,
            ExplosionSupport),
        new(
            DisappearanceAudience,
            DisappearanceSupport),
        new(
            CompletionAudience,
            CompletionSupport),
    ];

    internal static void Validate(
        string title,
        string description,
        Qwen3VlGroundedMetadataVisualDraft primaryVisualDraft,
        ICollection<string> failures)
    {
        string primaryActions = string.Join(" ", primaryVisualDraft.Actions);
        foreach ((string Field, string Value) audienceField in new[]
                 {
                     ("title", title),
                     ("description", description),
                 })
        {
            foreach (Rule rule in Rules)
            {
                Match offending = rule.Audience.Match(audienceField.Value);
                if (!offending.Success || rule.Support.IsMatch(primaryActions))
                {
                    continue;
                }
                failures.Add(
                    "quality " +
                    $"{ClipEditorialMetadataQualityIssueCode.UnsupportedMentalState}: " +
                    $"{audienceField.Field} strengthens the primary visual " +
                    $"action with unsupported '{offending.Value}'");
                break;
            }
        }
    }

}
