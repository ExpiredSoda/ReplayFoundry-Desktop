using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;

public enum GameKnowledgeContextFreshness
{
    NotCached,
    Fresh,
    RefreshScheduled,
    RefreshDue,
    Legacy,
}

public sealed record GameKnowledgeSourceReceipt(
    string Title,
    GameKnowledgeSourceKind Kind,
    GameKnowledgeSourceRole Role,
    Uri PageUri,
    string RevisionId,
    DateTimeOffset RevisionTimestampUtc,
    string LicenseIdentifier,
    Uri LicenseUri,
    string Attribution);

public sealed record GameKnowledgeClaimReceipt(
    string PassageId,
    string Label,
    string Value,
    string SourceTitle);

public sealed record GameKnowledgeComponentReceipt(
    GameKnowledgeComponentKind Kind,
    GameKnowledgeComponentCompleteness Completeness,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset? RetryAfterUtc,
    string? RevisionId,
    string? LicenseIdentifier,
    string? Attribution);

public sealed class GameKnowledgeContextReceipt
{
    public const int MaximumDisplayedClaims = 16;
    private readonly ReadOnlyCollection<GameKnowledgeSourceReceipt> _sources;
    private readonly ReadOnlyCollection<GameKnowledgeClaimReceipt> _claims;
    private readonly ReadOnlyCollection<GameKnowledgeComponentReceipt>
        _components;

    internal GameKnowledgeContextReceipt(
        string canonicalGameTitle,
        string? wikidataEntityId,
        GameKnowledgeContextFreshness freshness,
        DateTimeOffset? retrievedAtUtc,
        DateTimeOffset? nextRefreshAtUtc,
        bool canRefresh,
        bool canRemove,
        IEnumerable<GameKnowledgeSourceReceipt> sources,
        IEnumerable<GameKnowledgeClaimReceipt> claims,
        IEnumerable<GameKnowledgeComponentReceipt> components)
    {
        CanonicalGameTitle = canonicalGameTitle;
        WikidataEntityId = wikidataEntityId;
        Freshness = freshness;
        RetrievedAtUtc = retrievedAtUtc;
        NextRefreshAtUtc = nextRefreshAtUtc;
        CanRefresh = canRefresh;
        CanRemove = canRemove;
        _sources = Array.AsReadOnly(sources.Take(
            GameKnowledgeSnapshot.MaximumSources).ToArray());
        _claims = Array.AsReadOnly(claims.Take(MaximumDisplayedClaims).ToArray());
        _components = Array.AsReadOnly(components.Take(8).ToArray());
    }

    public string CanonicalGameTitle { get; }

    public string? WikidataEntityId { get; }

    public GameKnowledgeContextFreshness Freshness { get; }

    public DateTimeOffset? RetrievedAtUtc { get; }

    public DateTimeOffset? NextRefreshAtUtc { get; }

    public bool CanRefresh { get; }

    public bool CanRemove { get; }

    public IReadOnlyList<GameKnowledgeSourceReceipt> Sources => _sources;

    public IReadOnlyList<GameKnowledgeClaimReceipt> SupportedClaims => _claims;

    public IReadOnlyList<GameKnowledgeComponentReceipt> Components =>
        _components;
}

internal static class GameKnowledgeContextReceiptFactory
{
    public static GameKnowledgeContextReceipt Create(
        ClipEditorialContext context,
        DateTimeOffset nowUtc,
        TimeSpan freshFor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ConfirmedGameIdentity? identity = context.GameContext.ConfirmedIdentity;
        GameKnowledgeSnapshot? snapshot = context.GameKnowledge?.Snapshot;
        bool canRefresh = context.GameContext.UseOpenGameKnowledge &&
            identity?.SourcePermissions.Allows(
                GameKnowledgeSourcePermission.WikidataClaims) == true;
        if (snapshot is null)
        {
            return new GameKnowledgeContextReceipt(
                identity?.CanonicalTitle ?? context.GameContext.GameName,
                identity?.WikidataEntityId,
                GameKnowledgeContextFreshness.NotCached,
                retrievedAtUtc: null,
                nextRefreshAtUtc: null,
                canRefresh,
                canRemove: false,
                sources: [],
                claims: [],
                components: []);
        }

        DateTimeOffset nextRefreshAtUtc = NextRefresh(snapshot, freshFor);
        bool refreshDue = snapshot.Components.Any(component =>
            component.IsRefreshDue(nowUtc, freshFor)) ||
            nowUtc >= snapshot.RetrievedAtUtc + freshFor;
        bool retryScheduled = snapshot.Components.Any(component =>
            component.Completeness is
                GameKnowledgeComponentCompleteness.Missing or
                GameKnowledgeComponentCompleteness.TransientFailure &&
            component.RetryAfterUtc > nowUtc);
        GameKnowledgeContextFreshness freshness = snapshot.IsLegacy
            ? GameKnowledgeContextFreshness.Legacy
            : refreshDue
                ? GameKnowledgeContextFreshness.RefreshDue
                : retryScheduled
                    ? GameKnowledgeContextFreshness.RefreshScheduled
                    : GameKnowledgeContextFreshness.Fresh;
        IReadOnlyDictionary<string, GameKnowledgeSource> sourcesById =
            snapshot.Sources.ToDictionary(
                static source => source.Id,
                StringComparer.Ordinal);
        return new GameKnowledgeContextReceipt(
            identity?.CanonicalTitle ?? snapshot.GameName,
            identity?.WikidataEntityId,
            freshness,
            snapshot.RetrievedAtUtc,
            nextRefreshAtUtc,
            canRefresh,
            canRemove: identity is not null,
            snapshot.Sources.Select(ToReceipt),
            snapshot.Passages
                .Where(passage =>
                    sourcesById[passage.SourceId].Role ==
                        GameKnowledgeSourceRole.StructuredIdentity)
                .Select(passage => new GameKnowledgeClaimReceipt(
                    passage.Id,
                    passage.Section,
                    passage.Text,
                    sourcesById[passage.SourceId].Title)),
            snapshot.Components.Select(ToReceipt));
    }

    private static DateTimeOffset NextRefresh(
        GameKnowledgeSnapshot snapshot,
        TimeSpan freshFor)
    {
        DateTimeOffset result = snapshot.RetrievedAtUtc + freshFor;
        foreach (GameKnowledgeComponentState component in snapshot.Components)
        {
            DateTimeOffset? due = component.Completeness switch
            {
                GameKnowledgeComponentCompleteness.Disabled => null,
                GameKnowledgeComponentCompleteness.Legacy =>
                    component.CheckedAtUtc,
                GameKnowledgeComponentCompleteness.Missing or
                    GameKnowledgeComponentCompleteness.TransientFailure =>
                    component.RetryAfterUtc,
                _ => component.CheckedAtUtc + freshFor,
            };
            if (due is not null && due < result)
            {
                result = due.Value;
            }
        }
        return result;
    }

    private static GameKnowledgeSourceReceipt ToReceipt(
        GameKnowledgeSource source) =>
        new(
            source.Title,
            source.Kind,
            source.Role,
            source.PageUri,
            source.RevisionId,
            source.RevisionTimestampUtc,
            source.LicenseIdentifier,
            source.LicenseUri,
            source.Attribution);

    private static GameKnowledgeComponentReceipt ToReceipt(
        GameKnowledgeComponentState component) =>
        new(
            component.Kind,
            component.Completeness,
            component.CheckedAtUtc,
            component.RetryAfterUtc,
            component.RevisionId,
            component.LicenseIdentifier,
            component.Attribution);
}
