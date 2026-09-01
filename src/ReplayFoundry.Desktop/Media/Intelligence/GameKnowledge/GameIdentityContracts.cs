using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

public enum GameIdentityAuthority
{
    Wikidata,
}

[Flags]
public enum GameKnowledgeSourcePermission
{
    None = 0,
    WikidataCandidateDiscovery = 1,
    WikidataClaims = 2,
    WikipediaExcerpts = 4,
}

public static class GameKnowledgeSourcePermissions
{
    public const GameKnowledgeSourcePermission WikimediaDefault =
        GameKnowledgeSourcePermission.WikidataCandidateDiscovery |
        GameKnowledgeSourcePermission.WikidataClaims |
        GameKnowledgeSourcePermission.WikipediaExcerpts;

    public static bool Allows(
        this GameKnowledgeSourcePermission granted,
        GameKnowledgeSourcePermission requested) =>
        requested != GameKnowledgeSourcePermission.None &&
        (granted & requested) == requested;
}

public sealed partial record GameIdentityCandidate
{
    public GameIdentityCandidate(
        string wikidataEntityId,
        string canonicalTitle,
        string? edition,
        int? releaseYear,
        string? developer,
        string? series,
        GameIdentityAuthority authority,
        string source,
        string locale)
    {
        WikidataEntityId = RequireQid(wikidataEntityId);
        CanonicalTitle = Required(canonicalTitle, 160, nameof(canonicalTitle));
        Edition = Optional(edition, 120, nameof(edition));
        ReleaseYear = ValidYear(releaseYear, nameof(releaseYear));
        Developer = Optional(developer, 160, nameof(developer));
        Series = Optional(series, 160, nameof(series));
        if (!Enum.IsDefined(authority))
        {
            throw new ArgumentOutOfRangeException(nameof(authority));
        }
        Authority = authority;
        Source = Required(source, 80, nameof(source));
        Locale = RequireLocale(locale);
    }

    public string WikidataEntityId { get; }

    public string CanonicalTitle { get; }

    public string? Edition { get; }

    public int? ReleaseYear { get; }

    public string? Developer { get; }

    public string? Series { get; }

    public GameIdentityAuthority Authority { get; }

    public string Source { get; }

    public string Locale { get; }

    public ConfirmedGameIdentity Confirm(
        DateTimeOffset confirmedAtUtc,
        GameKnowledgeSourcePermission permissions) =>
        new(
            WikidataEntityId,
            CanonicalTitle,
            Edition,
            ReleaseYear,
            Developer,
            Series,
            Authority,
            Source,
            Locale,
            userConfirmed: true,
            confirmedAtUtc,
            permissions);

    internal static string Required(
        string value,
        int maximum,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum
            ? throw new ArgumentException(
                $"{parameterName} must contain at most {maximum} characters.",
                parameterName)
            : value.Trim();

    internal static string? Optional(
        string? value,
        int maximum,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return Required(value, maximum, parameterName);
    }

    internal static string RequireQid(string value) =>
        value is not null && QidPattern().IsMatch(value.Trim())
            ? value.Trim()
            : throw new ArgumentException(
                "A game identity requires a canonical Wikidata QID.",
                nameof(value));

    internal static string RequireLocale(string value) =>
        value is not null && LocalePattern().IsMatch(value.Trim())
            ? value.Trim().ToLowerInvariant()
            : throw new ArgumentException(
                "A game identity locale must be a bounded BCP 47 language tag.",
                nameof(value));

    internal static int? ValidYear(int? value, string parameterName) =>
        value is null or >= 1950 and <= 2200
            ? value
            : throw new ArgumentOutOfRangeException(parameterName);

    [GeneratedRegex(@"^Q[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex QidPattern();

    [GeneratedRegex(
        @"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8}){0,2}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LocalePattern();
}

public sealed record ConfirmedGameIdentity
{
    public ConfirmedGameIdentity(
        string wikidataEntityId,
        string canonicalTitle,
        string? edition,
        int? releaseYear,
        string? developer,
        string? series,
        GameIdentityAuthority authority,
        string source,
        string locale,
        bool userConfirmed,
        DateTimeOffset confirmedAtUtc,
        GameKnowledgeSourcePermission sourcePermissions)
    {
        if (!userConfirmed)
        {
            throw new ArgumentException(
                "A confirmed game identity must record an explicit user confirmation.",
                nameof(userConfirmed));
        }
        if (confirmedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Game identity confirmation timestamps must be UTC.",
                nameof(confirmedAtUtc));
        }
        if (!Enum.IsDefined(authority) ||
            sourcePermissions == GameKnowledgeSourcePermission.None ||
            (sourcePermissions & ~GameKnowledgeSourcePermissions.WikimediaDefault) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePermissions));
        }

        WikidataEntityId = GameIdentityCandidate.RequireQid(wikidataEntityId);
        CanonicalTitle = GameIdentityCandidate.Required(
            canonicalTitle,
            160,
            nameof(canonicalTitle));
        Edition = GameIdentityCandidate.Optional(edition, 120, nameof(edition));
        ReleaseYear = GameIdentityCandidate.ValidYear(
            releaseYear,
            nameof(releaseYear));
        Developer = GameIdentityCandidate.Optional(
            developer,
            160,
            nameof(developer));
        Series = GameIdentityCandidate.Optional(series, 160, nameof(series));
        Authority = authority;
        Source = GameIdentityCandidate.Required(source, 80, nameof(source));
        Locale = GameIdentityCandidate.RequireLocale(locale);
        UserConfirmed = userConfirmed;
        ConfirmedAtUtc = confirmedAtUtc;
        SourcePermissions = sourcePermissions;
    }

    public string WikidataEntityId { get; }

    public string CanonicalTitle { get; }

    public string? Edition { get; }

    public int? ReleaseYear { get; }

    public string? Developer { get; }

    public string? Series { get; }

    public GameIdentityAuthority Authority { get; }

    public string Source { get; }

    public string Locale { get; }

    public bool UserConfirmed { get; }

    public DateTimeOffset ConfirmedAtUtc { get; }

    public GameKnowledgeSourcePermission SourcePermissions { get; }

    public bool Allows(GameKnowledgeSourcePermission permission) =>
        SourcePermissions.Allows(permission);
}

public sealed record GameIdentityDiscoveryRequest
{
    public GameIdentityDiscoveryRequest(
        string userConfirmedTitle,
        string locale,
        GameKnowledgeSourcePermission sourcePermissions)
    {
        UserConfirmedTitle = GameIdentityCandidate.Required(
            userConfirmedTitle,
            120,
            nameof(userConfirmedTitle));
        Locale = GameIdentityCandidate.RequireLocale(locale);
        if (!sourcePermissions.Allows(
                GameKnowledgeSourcePermission.WikidataCandidateDiscovery))
        {
            throw new ArgumentException(
                "Online game discovery requires explicit Wikidata discovery permission.",
                nameof(sourcePermissions));
        }
        SourcePermissions = sourcePermissions;
    }

    public string UserConfirmedTitle { get; }

    public string Locale { get; }

    public GameKnowledgeSourcePermission SourcePermissions { get; }
}

public sealed class GameIdentityCandidateSet
{
    public const int MaximumCandidates = 5;
    private readonly ReadOnlyCollection<GameIdentityCandidate> _candidates;

    public GameIdentityCandidateSet(
        GameIdentityDiscoveryRequest request,
        IEnumerable<GameIdentityCandidate> candidates)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        ArgumentNullException.ThrowIfNull(candidates);
        GameIdentityCandidate[] snapshot = candidates.ToArray();
        if (snapshot.Length > MaximumCandidates ||
            snapshot.Any(static value => value is null) ||
            snapshot.Any(value => !value.Locale.Equals(
                request.Locale,
                StringComparison.OrdinalIgnoreCase)) ||
            snapshot.Select(static value => value.WikidataEntityId)
                .Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Game identity candidates must be bounded, locale-matched, non-null, and QID-unique.",
                nameof(candidates));
        }
        _candidates = Array.AsReadOnly(snapshot);
    }

    public GameIdentityDiscoveryRequest Request { get; }

    public IReadOnlyList<GameIdentityCandidate> Candidates => _candidates;
}

public sealed record GameKnowledgeCacheKey
{
    public GameKnowledgeCacheKey(
        ConfirmedGameIdentity identity,
        GameKnowledgeProviderIdentity provider,
        string schemaVersion,
        string policyVersion)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        SchemaVersion = GameIdentityCandidate.Required(
            schemaVersion,
            100,
            nameof(schemaVersion));
        PolicyVersion = GameIdentityCandidate.Required(
            policyVersion,
            80,
            nameof(policyVersion));
    }

    public ConfirmedGameIdentity Identity { get; }

    public GameKnowledgeProviderIdentity Provider { get; }

    public string SchemaVersion { get; }

    public string PolicyVersion { get; }

    public string CanonicalValue => string.Join(
        "|",
        Identity.WikidataEntityId,
        Identity.Locale,
        Provider.Name,
        Provider.Version,
        SchemaVersion,
        PolicyVersion).ToUpperInvariant();
}

public sealed record GameKnowledgeRefreshRequest
{
    public GameKnowledgeRefreshRequest(
        ConfirmedGameIdentity identity,
        GameKnowledgeSnapshot? existingSnapshot = null,
        bool forceRefresh = false)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        if (!identity.UserConfirmed)
        {
            throw new ArgumentException(
                "Game-knowledge refresh requires explicit user-confirmed identity.",
                nameof(identity));
        }
        if (existingSnapshot?.ConfirmedIdentity is not null &&
            (!existingSnapshot.ConfirmedIdentity.WikidataEntityId.Equals(
                 identity.WikidataEntityId,
                 StringComparison.Ordinal) ||
             !existingSnapshot.ConfirmedIdentity.Locale.Equals(
                 identity.Locale,
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "A refresh snapshot must match the confirmed QID and locale.",
                nameof(existingSnapshot));
        }
        ExistingSnapshot = existingSnapshot;
        ForceRefresh = forceRefresh;
    }

    public ConfirmedGameIdentity Identity { get; }

    public GameKnowledgeSnapshot? ExistingSnapshot { get; }

    public bool ForceRefresh { get; }
}
