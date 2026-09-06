using System.Security.Cryptography;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Projects;

namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

/// <summary>
/// Optimistic concurrency identity for retained request context. The grounded
/// brief intentionally selects bounded claims and cannot detect every caption
/// correction, role change, or timing edit while a rewrite is pending.
/// </summary>
internal static class StudioEditorialContextRevision
{
    internal const string UnknownAuthoredContext = "unknown-authored-context";
    internal static string CreateDurable(ClipEditorialContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Use exactly the persisted contract: all transcript spans and retained
        // facts survive reload, while raw OCR images and unused public-knowledge
        // snapshot passages are intentionally discarded by project storage.
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            StudioProjectDocumentMapper.MapEditorialContext(context))));
    }

    internal static string Create(ClipEditorialContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Deliberately project values instead of serializing the domain graph:
        // retained OCR frames expose byte-span-backed media and must never be
        // traversed by the JSON contract resolver.
        var values = new
        {
            context.CandidateId, context.SourceFullPath, context.SourceLabel,
            context.SourceStart, context.SourceEnd, context.SourceDuration,
            context.DeterministicScore, context.DeterministicReason,
            BriefFingerprint = context.EditorialBrief.Fingerprint,
            Game = new
            {
                context.GameContext.GameName, context.GameContext.GameHashtag, context.GameContext.ContextNotes,
                context.GameContext.Source, context.GameContext.UseOpenGameKnowledge,
                context.GameContext.ConfirmedIdentity,
            },
            Transcripts = context.Transcripts.Select(transcript => new
            {
                transcript.AbsoluteAudioStreamIndex, transcript.Role.Role, transcript.Role.Source,
                transcript.Authority, transcript.Text,
                Spans = transcript.Spans.Select(span => new { span.SourceStart, span.SourceEnd, span.Text }),
            }),
            Evidence = context.Evidence.Select(evidence => new { evidence.Id, evidence.Kind, evidence.Description }),
            Knowledge = context.GameKnowledge is not { } knowledge ? null : new
            {
                knowledge.GameName, Snapshot = knowledge.Snapshot?.SnapshotSha256,
                Matches = knowledge.Matches.Select(match => new
                {
                    match.Passage.Id, match.Passage.SourceId, match.Passage.ContentSha256, match.Passage.Section,
                    match.Strength, match.TemporalRelation, match.Relevance, match.MatchedTerms, match.ClipEvidenceIds,
                }),
                Warnings = knowledge.Warnings.Select(warning => new { warning.Code, warning.Message }),
            },
            context.GameplayRegion,
            VisualText = context.VisualText is not { } text ? null : new
            {
                text.ContentRegion,
                Frames = text.Frames.Select(frame => new
                {
                    frame.Request.Frame.SourcePath, frame.Request.Frame.RequestedTimestamp, frame.Request.Frame.DecodedTimestamp,
                    frame.Request.Frame.Width, frame.Request.Frame.Height,
                    ImageSha256 = Convert.ToHexString(SHA256.HashData(frame.Request.Frame.PngData.Span)),
                    frame.Provider,
                    Lines = frame.Lines.Select(line => new { line.Text, line.Words }),
                }),
                Anchors = text.Anchors.Select(anchor => new
                {
                    anchor.NormalizedText, anchor.DisplayText, anchor.Authority, anchor.SourceKind, anchor.SourceTimestamps,
                }),
                Warnings = text.Warnings.Select(warning => new { warning.Code, warning.Message, warning.SourceTimestamp }),
            },
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));
    }
}
