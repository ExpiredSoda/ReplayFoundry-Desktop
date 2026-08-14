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
        @"\b(?:defeated|killed|destroyed|eliminated|vanquished|won|died|collapsed|health bar (?:emptied|reached zero))\b",
        Options);
    private static readonly Regex EntryAudience = new(
        @"\b(?:entered|entering|passed through|passing through)\b",
        Options);
    private static readonly Regex EntrySupport = new(
        @"\b(?:entered|entering|passed through|passing through|moved (?:into|through)|walked (?:into|through)|ran (?:into|through))\b",
        Options);
    private static readonly Regex ExplosionAudience = new(
        @"\b(?:exploded|detonated|blew up|burst apart)\b",
        Options);
    private static readonly Regex DisappearanceAudience = new(
        @"\b(?:disappeared|vanished|reappeared|rematerialized)\b",
        Options);
    private static readonly Regex CompletionAudience = new(
        @"\b(?:completed|finished|cleared)\b",
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
            ExplosionAudience),
        new(
            DisappearanceAudience,
            DisappearanceAudience),
        new(
            CompletionAudience,
            CompletionAudience),
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
