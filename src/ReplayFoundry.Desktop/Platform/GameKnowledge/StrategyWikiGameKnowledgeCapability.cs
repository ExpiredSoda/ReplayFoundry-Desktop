using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

namespace ReplayFoundry.Desktop.Platform.GameKnowledge;

public sealed record GameKnowledgeCapabilityProbe(
    string Provider,
    bool Enabled,
    bool NetworkAccessPermitted,
    string Reason);

public static class StrategyWikiGameKnowledgeCapability
{
    private const string DisabledReason =
        "StrategyWiki retrieval is disabled: Replay Foundry has no documented, approved runtime API contract for it and will not scrape pages.";

    public static GameKnowledgeCapabilityProbe Probe() =>
        new(
            "StrategyWiki",
            Enabled: false,
            NetworkAccessPermitted: false,
            DisabledReason);

    internal static GameKnowledgeComponentState ComponentState(
        DateTimeOffset checkedAtUtc) =>
        new(
            GameKnowledgeComponentKind.StrategyWiki,
            GameKnowledgeComponentCompleteness.Disabled,
            checkedAtUtc);
}
