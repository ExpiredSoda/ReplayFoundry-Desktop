using System.Globalization;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataPayload
{
    internal static object? CreateGameKnowledge(
        ClipEditorialMetadataRequest request)
    {
        ClipEditorialContext context = request.Context;
        ClipGameKnowledgeContext? knowledge = context.GameKnowledge;
        GameKnowledgeSnapshot? snapshot = knowledge?.Snapshot;
        if (snapshot is null || knowledge!.Matches.Count == 0)
        {
            return null;
        }
        HashSet<string> availableEvidenceIds =
            CreateAvailableClipEvidenceIds(request);
        var matches = knowledge.Matches
            .Select(match => new
            {
                Match = match,
                ClipEvidenceIds = SelectMatchClipEvidenceIds(
                    match,
                    request,
                    availableEvidenceIds),
            })
            .Where(static item =>
                item.Match.Strength !=
                    GameKnowledgeMatchStrength.ClipLinked ||
                item.ClipEvidenceIds.Length > 0)
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }
        var sourceIds = matches
            .Select(static value => value.Match.Passage.SourceId)
            .ToHashSet(StringComparer.Ordinal);
        return new
        {
            policyVersion =
                DeterministicGameKnowledgeRetriever.PolicyVersion,
            snapshotSha256 = snapshot.SnapshotSha256,
            provider = new
            {
                snapshot.Provider.Name,
                snapshot.Provider.Version,
            },
            sources = snapshot.Sources
                .Where(source => sourceIds.Contains(source.Id))
                .Select(source => new
                {
                    source.Id,
                    kind = source.Kind.ToString(),
                    role = source.Role.ToString(),
                    source.Title,
                    pageUri = source.PageUri.AbsoluteUri,
                    source.RevisionId,
                    revisionTimestampUtc = source.RevisionTimestampUtc
                        .ToString("O", CultureInfo.InvariantCulture),
                    source.LicenseIdentifier,
                    licenseUri = source.LicenseUri.AbsoluteUri,
                    source.Attribution,
                    source.ContentSha256,
                }).ToArray(),
            matches = matches.Select(item => new
            {
                id = item.Match.Passage.Id,
                item.Match.Passage.SourceId,
                item.Match.Passage.Section,
                item.Match.Passage.Text,
                item.Match.Passage.ContentSha256,
                strength = item.Match.Strength.ToString(),
                temporalRelation = item.Match.TemporalRelation.ToString(),
                item.Match.Relevance,
                item.Match.MatchedTerms,
                item.ClipEvidenceIds,
            }).ToArray(),
        };
    }

    private static string[] SelectMatchClipEvidenceIds(
        GameKnowledgeMatch match,
        ClipEditorialMetadataRequest request,
        IReadOnlySet<string> availableEvidenceIds) =>
        match.Strength switch
        {
            GameKnowledgeMatchStrength.CandidateForVisualGrounding =>
                [ReviewEvidenceId(request.ReviewVideo!)],
            GameKnowledgeMatchStrength.ClipLinked => match.ClipEvidenceIds
                .Where(availableEvidenceIds.Contains)
                .ToArray(),
            _ => [],
        };

    internal static object[] CreateEvidence(
        ClipEditorialMetadataRequest request,
        VisualSemanticInputManifest reviewVideo)
    {
        var result = request.Context.Evidence
            .Take(23)
            .Select(evidence => (object)new
            {
                id = evidence.Id,
                kind = evidence.Kind.ToString(),
                description = evidence.Description,
            }).ToList();
        result.Add(new
        {
            id = ReviewEvidenceId(reviewVideo),
            kind = ClipEditorialEvidenceKind.VisualObservation.ToString(),
            description =
                "The verified bounded review video supplied to the local visual model.",
        });
        foreach (VisualTextAnchor anchor in
                 SelectVisualTextEvidenceAnchors(request))
        {
            result.Add(new
            {
                id = anchor.EvidenceId,
                kind = ClipEditorialEvidenceKind.VisualObservation.ToString(),
                description =
                    $"Stable local Gameplay OCR across {anchor.OccurrenceCount} sampled frames: {anchor.DisplayText}",
            });
        }
        return result.ToArray();
    }

    private static HashSet<string> CreateAvailableClipEvidenceIds(
        ClipEditorialMetadataRequest request)
    {
        var result = request.Context.Evidence
            .Take(23)
            .Select(static evidence => evidence.Id)
            .ToHashSet(StringComparer.Ordinal);
        result.Add(ReviewEvidenceId(request.ReviewVideo!));
        result.UnionWith(SelectVisualTextEvidenceAnchors(request)
            .Select(static anchor => anchor.EvidenceId));
        result.UnionWith(request.Context.Transcripts.Select(
            static transcript =>
                $"stream-{transcript.AbsoluteAudioStreamIndex}"));
        return result;
    }

    private static VisualTextAnchor[] SelectVisualTextEvidenceAnchors(
        ClipEditorialMetadataRequest request)
    {
        ClipVisualTextContext? visualText = request.Context.VisualText;
        int remaining = Math.Max(
            0,
            23 - Math.Min(23, request.Context.Evidence.Count));
        if (visualText is null || remaining == 0)
        {
            return [];
        }

        HashSet<string> linkedIds = request.Context.GameKnowledge?.Matches
            .Where(static match => match.Strength ==
                GameKnowledgeMatchStrength.ClipLinked)
            .SelectMany(static match => match.ClipEvidenceIds)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        return visualText.GroundingAnchors
            .OrderByDescending(anchor => linkedIds.Contains(anchor.EvidenceId))
            .Take(remaining)
            .ToArray();
    }

    internal static object? CreateVisualText(
        ClipEditorialMetadataRequest request)
    {
        var visualText = request.Context.VisualText;
        if (visualText is null)
        {
            return null;
        }

        var provider = visualText.Frames.FirstOrDefault()?.Provider;
        return new
        {
            samplingPolicyVersion =
                ClipVisualTextContext.SamplingPolicyVersion,
            stabilityPolicyVersion =
                ClipVisualTextContext.StabilityPolicyVersion,
            provider = provider is null
                ? null
                : new
                {
                    provider.Name,
                    provider.Version,
                    provider.Backend,
                    provider.RuntimeVersion,
                    provider.LanguageTag,
                },
            sampledFrameCount = visualText.Frames.Count,
            groundingAnchors = visualText.GroundingAnchors.Select(anchor => new
            {
                text = anchor.DisplayText,
                sourceKind = anchor.SourceKind.ToString(),
                occurrenceCount = anchor.OccurrenceCount,
                sourceTimestampsSeconds = anchor.SourceTimestamps
                    .Select(static value => value.TotalSeconds)
                    .ToArray(),
            }).ToArray(),
            diagnosticAnchors = visualText.Anchors
                .Where(static anchor => !anchor.MayGroundAudienceCopy)
                .Take(12)
                .Select(anchor => new
                {
                    text = anchor.DisplayText,
                    sourceKind = anchor.SourceKind.ToString(),
                    occurrenceCount = anchor.OccurrenceCount,
                    sourceTimestampsSeconds = anchor.SourceTimestamps
                        .Select(static value => value.TotalSeconds)
                        .ToArray(),
                }).ToArray(),
        };
    }

    internal static string ReviewEvidenceId(
        VisualSemanticInputManifest reviewVideo) =>
        $"bounded-review-{reviewVideo.ReviewVideoSha256[..16].ToLowerInvariant()}";
}
