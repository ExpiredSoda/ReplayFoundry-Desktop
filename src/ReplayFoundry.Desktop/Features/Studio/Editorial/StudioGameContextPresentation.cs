using System.Globalization;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

internal static class StudioGameContextPresentation
{
    internal static string SaveGuidance(bool hasUnsavedChanges, bool isApproved, bool hasCopyReview) => hasUnsavedChanges
        ? "Add to queue will save these changes too."
        : isApproved
            ? "Reviewed. You can still make changes."
        : hasCopyReview
            ? "Copy needs review. You can still add this clip to the queue."
            : "Ready to use. Review is optional.";

    internal static string BuildContextUsedSummary(
        GenerationOutputAsset? asset)
    {
        GroundedEditorialBrief? brief =
            asset?.EditorialContext?.EditorialBrief;
        if (brief is null)
        {
            return "No details about how this was written are available.";
        }
        GameKnowledgeInfluenceAudit? audit =
            asset?.EditorialMetadata?.GroundingAudit;
        if (audit is null)
        {
            return "This draft was created before writing details were saved. Try another angle to create them now.";
        }

        HashSet<string> used = audit.UsedClaimIds.ToHashSet(
            StringComparer.Ordinal);
        GroundedGameContextClaim[] claims = brief.Claims
            .Where(claim => used.Contains(claim.Id))
            .ToArray();
        var labels = new List<string>();
        AddIdentityLabel(claims, labels);
        AddPublicContextLabel(claims, labels);
        AddTranscriptLabels(claims, labels);
        if (audit.UsedEvidenceIds.Count > 0)
        {
            labels.Add("this clip");
        }
        if (labels.Count == 0)
        {
            labels.Add(
                audit.NeedsReview &&
                !string.IsNullOrWhiteSpace(audit.FallbackReason)
                    ? "a broad local draft"
                    : "this clip only");
        }
        return "Based on: " + string.Join(" · ", labels) + ".";
    }

    internal static string BuildContextAuthoritySummary(
        GenerationOutputAsset? asset)
    {
        GroundedEditorialBrief? brief =
            asset?.EditorialContext?.EditorialBrief;
        return brief is null
            ? "No spoken words, screen text, or public game details were used."
            : $"Used {brief.ConfirmedClaimCount + brief.CorroboratedClaimCount} details " +
              "that matched this video or its public game information. " +
              (brief.CandidateClaimCount == 0
                  ? "Everything used could be checked."
                  : $"Left out {brief.CandidateClaimCount} " +
                    (brief.CandidateClaimCount == 1
                        ? "detail that could not be checked."
                        : "details that could not be checked."));
    }

    internal static string BuildWhyThisTitle(
        GenerationOutputAsset? asset)
    {
        ClipEditorialMetadataDraft? metadata = asset?.EditorialMetadata;
        GroundedEditorialBrief? brief = asset?.EditorialContext?.EditorialBrief;
        GameKnowledgeInfluenceAudit? audit = metadata?.GroundingAudit;
        if (metadata is null || brief is null || audit is null)
        {
            return "No details about this title are available.";
        }

        HashSet<string> usedClaimIds = audit.UsedClaimIds.ToHashSet(
            StringComparer.Ordinal);
        HashSet<string> usedEvidenceIds = audit.UsedEvidenceIds.ToHashSet(
            StringComparer.Ordinal);
        var anchors = new List<string>();
        anchors.AddRange(brief.Claims
            .Where(claim => usedClaimIds.Contains(claim.Id))
            .Take(4)
            .Select(claim =>
                $"{ClaimLabel(claim.Kind)}: {BoundDisplay(claim.Value, 220)}"));
        anchors.AddRange(metadata.Evidence
            .Where(evidence => usedEvidenceIds.Contains(evidence.Id))
            .Take(Math.Max(0, 5 - anchors.Count))
            .Select(evidence =>
                $"this clip: {BoundDisplay(evidence.Description, 220)}"));

        if (anchors.Count == 0 &&
            !string.IsNullOrWhiteSpace(brief.PrimaryGameplayBeat))
        {
            anchors.Add("visible event: " +
                BoundDisplay(brief.PrimaryGameplayBeat, 220));
        }
        if (anchors.Count == 0)
        {
            return audit.NeedsReview
                ? "This wording was not tied to one specific detail. Check it or try another angle."
                : "The title used only details from this clip. No public story detail shaped it.";
        }

        string prefix = metadata.Origin ==
            ClipEditorialMetadataOrigin.AiAssisted
                ? "Local AI based this title on:"
                : "This title draws from:";
        return prefix + Environment.NewLine +
            string.Join(Environment.NewLine,
                anchors.Select(static anchor => "• " + anchor));
    }

    internal static string BuildMetadataOrigin(
        GenerationOutputAsset? asset) =>
        asset?.EditorialMetadata switch
        {
            { Origin: ClipEditorialMetadataOrigin.AiAssisted } =>
                "Written with local AI",
            { Origin: ClipEditorialMetadataOrigin.Heuristic } =>
                "Written with the built-in writer",
            { Origin: ClipEditorialMetadataOrigin.UserEdited,
              AiProvenance: not null } =>
                "Edited from local AI",
            { Origin: ClipEditorialMetadataOrigin.UserEdited } =>
                "Your edit",
            _ => "No title and description",
        };

    internal static bool IsGroundingReceiptStale(
        GenerationOutputAsset? asset) =>
        asset?.EditorialContext?.EditorialBrief is { } brief &&
        asset.EditorialMetadata?.GroundingAudit is { } audit &&
        !audit.BriefFingerprint.Equals(
            brief.Fingerprint,
            StringComparison.Ordinal);

    internal static string BuildFreshnessText(
        GameKnowledgeContextReceipt receipt)
    {
        string freshness = receipt.Freshness switch
        {
            GameKnowledgeContextFreshness.NotCached => "Not saved",
            GameKnowledgeContextFreshness.Fresh => "Fresh",
            GameKnowledgeContextFreshness.RefreshScheduled =>
                "Refresh scheduled",
            GameKnowledgeContextFreshness.RefreshDue => "Refresh available",
            GameKnowledgeContextFreshness.Legacy => "Saved earlier",
            _ => "Unknown",
        };
        string retrieved = receipt.RetrievedAtUtc is { } checkedAt
            ? $" · checked {checkedAt.UtcDateTime.ToString(
                "yyyy-MM-dd 'UTC'",
                CultureInfo.InvariantCulture)}"
            : string.Empty;
        string next = receipt.NextRefreshAtUtc is { } refreshAt
            ? $" · next refresh {refreshAt.UtcDateTime.ToString(
                "yyyy-MM-dd 'UTC'",
                CultureInfo.InvariantCulture)}"
            : string.Empty;
        return freshness + retrieved + next;
    }

    internal static string BuildSourceAttributionText(
        GameKnowledgeContextReceipt receipt) =>
        receipt.Sources.Count == 0
            ? "No saved public source details."
            : string.Join(Environment.NewLine,
                receipt.Sources.Take(4).Select(source =>
                    $"{source.Kind}: {source.Title} · revision " +
                    $"{source.RevisionId} · {source.LicenseIdentifier} · " +
                    BoundDisplay(source.Attribution, 180)));

    internal static IReadOnlyList<GroundedGameContextClaim>
        AmbiguousKnowledgeClaims(GenerationOutputAsset? asset) =>
        asset?.EditorialContext?.EditorialBrief.Claims
            .Where(static claim =>
                claim.State == GroundedGameContextClaimState.Ambiguous &&
                claim.Kind is
                    GroundedGameContextClaimKind.MissionOrChapter or
                    GroundedGameContextClaimKind.Location or
                    GroundedGameContextClaimKind.CanonicalEntity or
                    GroundedGameContextClaimKind.NarrativeContext)
            .ToArray() ?? [];

    internal static string ClaimLabel(
        GroundedGameContextClaimKind kind) => kind switch
        {
            GroundedGameContextClaimKind.GameIdentity => "game identity",
            GroundedGameContextClaimKind.Edition => "game edition",
            GroundedGameContextClaimKind.Developer => "developer",
            GroundedGameContextClaimKind.Series => "series",
            GroundedGameContextClaimKind.MissionOrChapter =>
                "mission or chapter",
            GroundedGameContextClaimKind.Location => "location",
            GroundedGameContextClaimKind.CanonicalEntity => "game entity",
            GroundedGameContextClaimKind.CreatorCommentaryCue =>
                "spoken commentary",
            GroundedGameContextClaimKind.DialogueCue => "spoken dialogue",
            GroundedGameContextClaimKind.StableReadableText => "screen text",
            _ => "story context",
        };

    internal static string BoundDisplay(string value, int maximum)
    {
        string normalized = value.Trim();
        return normalized.Length <= maximum
            ? normalized
            : normalized[..(maximum - 1)].TrimEnd() + "…";
    }

    private static void AddIdentityLabel(
        IEnumerable<GroundedGameContextClaim> claims,
        ICollection<string> labels)
    {
        if (claims.Any(static claim => claim.Kind is
                GroundedGameContextClaimKind.GameIdentity or
                GroundedGameContextClaimKind.Edition or
                GroundedGameContextClaimKind.Developer or
                GroundedGameContextClaimKind.Series))
        {
            labels.Add("verified game info");
        }
    }

    private static void AddPublicContextLabel(
        IEnumerable<GroundedGameContextClaim> claims,
        ICollection<string> labels)
    {
        if (claims.Any(static claim => claim.Kind is
                GroundedGameContextClaimKind.MissionOrChapter or
                GroundedGameContextClaimKind.Location or
                GroundedGameContextClaimKind.CanonicalEntity or
                GroundedGameContextClaimKind.NarrativeContext))
        {
            labels.Add("verified game details");
        }
    }

    private static void AddTranscriptLabels(
        IEnumerable<GroundedGameContextClaim> claims,
        ICollection<string> labels)
    {
        if (claims.Any(static claim =>
                claim.Kind ==
                    GroundedGameContextClaimKind.CreatorCommentaryCue))
        {
            labels.Add("spoken commentary");
        }
        if (claims.Any(static claim =>
                claim.Kind == GroundedGameContextClaimKind.DialogueCue))
        {
            labels.Add("spoken dialogue");
        }
    }

    internal static string BuildContextReviewSummary(GenerationOutputAsset? asset) =>
        StudioGameContextPresentation.IsGroundingReceiptStale(asset)
            ? "The clip, captions, or saved game info changed after this was written. Your wording is unchanged; refresh it when you want it to use the update."
            : asset?.EditorialMetadata?.GroundingAudit?.NeedsReview == true
            ? "This title and description are broad. Check them or try another angle; the clip is still ready to use."
            : asset?.EditorialContext?.EditorialBrief?.CandidateClaimCount > 0
                ? "Unconfirmed game details, speech hints, and screen text were left out."
                : "Only verified details were used.";

    internal static string BuildSupportedGameContextClaimsText(GameKnowledgeContextReceipt receipt) =>
        receipt.SupportedClaims.Count == 0
            ? "No verified public game details were used."
            : string.Join(Environment.NewLine,
                receipt.SupportedClaims.Take(4).Select(claim =>
                    $"{claim.Label}: " +
                    $"{StudioGameContextPresentation.BoundDisplay(claim.Value, 180)} " +
                    $"({claim.SourceTitle})"));

    internal static string BuildAmbiguousGameContextSuggestionsText(GenerationOutputAsset? asset) =>
        StudioGameContextPresentation
            .AmbiguousKnowledgeClaims(asset).Count == 0
            ? "No unconfirmed mission, location, or story detail is waiting."
            : string.Join(Environment.NewLine,
                StudioGameContextPresentation
                    .AmbiguousKnowledgeClaims(asset)
                    .Take(4)
                    .Select(claim =>
                        $"Likely {StudioGameContextPresentation.ClaimLabel(claim.Kind)}: " +
                        StudioGameContextPresentation.BoundDisplay(
                            claim.Value,
                            180)));

    internal static string BuildGameContextComponentsText(GameKnowledgeContextReceipt receipt) =>
        receipt.Components.Count == 0
            ? "No saved game-info sections."
            : string.Join(" · ", receipt.Components.Select(
                static component =>
                    $"{component.Kind}: {component.Completeness}"));

    internal static string PackagingGuidance(string title, string description) =>
        ClipAudiencePackagingAssessment.Evaluate(title, description).Summary;
    internal static bool ContextNeedsReview(GenerationOutputAsset? asset) =>
        IsGroundingReceiptStale(asset) ||
        asset?.EditorialMetadata?.GroundingAudit?.NeedsReview == true ||
        asset?.EditorialContext?.EditorialBrief?.CandidateClaimCount > 0;

}
