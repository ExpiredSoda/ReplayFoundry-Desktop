using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public enum GenerationMomentIntent
{
    Any,
    Action,
    Humor,
    Story,
    Discovery,
    Failure,
    Dialogue,
    Clutch,
    Tutorial,
    Reaction,
}

/// <summary>Creator preferences, never assertions about the recording.</summary>
public sealed class GenerationDiscoveryIntent
{
    public static GenerationDiscoveryIntent Default { get; } = new();

    public GenerationDiscoveryIntent(GenerationMomentIntent momentType = GenerationMomentIntent.Any,
        string? spokenTerms = null, string? naturalLanguageQuery = null)
    {
        if (!Enum.IsDefined(momentType))
        {
            throw new ArgumentOutOfRangeException(nameof(momentType));
        }
        if (spokenTerms?.Length > 240)
        {
            throw new ArgumentException("Spoken search terms must fit in 240 characters.", nameof(spokenTerms));
        }
        MomentType = momentType;
        if (naturalLanguageQuery?.Length > 240)
            throw new ArgumentException("A semantic query must fit in 240 characters.", nameof(naturalLanguageQuery));
        NaturalLanguageQuery = naturalLanguageQuery?.Trim() ?? string.Empty;
        SpokenTerms = spokenTerms?.Trim() ?? string.Empty;
        Phrases = Array.AsReadOnly(SpokenTerms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize).Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal).Take(12).ToArray());
    }

    public GenerationMomentIntent MomentType { get; }
    public string SpokenTerms { get; }
    public string NaturalLanguageQuery { get; }
    public IReadOnlyList<string> Phrases { get; }
    public bool IsDefault => MomentType == GenerationMomentIntent.Any && Phrases.Count == 0 && NaturalLanguageQuery.Length == 0;
    public bool UsesSemanticRetrieval => NaturalLanguageQuery.Length > 0 ||
        MomentType is GenerationMomentIntent.Clutch or GenerationMomentIntent.Tutorial or GenerationMomentIntent.Reaction;
    public string SemanticQuery => NaturalLanguageQuery.Length > 0 ? NaturalLanguageQuery : MomentType switch
    {
        GenerationMomentIntent.Clutch => "The player recovers from a losing situation and succeeds against the odds.",
        GenerationMomentIntent.Tutorial => "The creator explains how to use a game mechanic and gives useful instructions.",
        GenerationMomentIntent.Reaction => "The creator reacts with surprise or excitement to something that just happened.",
        _ => string.Empty,
    };

    public int CountMatches(string text)
    {
        string normalized = " " + Normalize(text) + " ";
        return Phrases.Count(phrase => normalized.Contains(" " + phrase + " ", StringComparison.Ordinal));
    }

    private static string Normalize(string text) =>
        Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
}
