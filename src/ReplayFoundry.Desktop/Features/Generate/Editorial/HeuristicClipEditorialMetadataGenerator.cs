using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public sealed partial class HeuristicClipEditorialMetadataGenerator :
    IClipEditorialMetadataGenerator
{
    public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
        new(
            "ReplayFoundry grounded heuristics",
            "1.8.0");

    public bool IsAvailable => true;

    public Task<ClipEditorialMetadataDraft> GenerateAsync(
        ClipEditorialMetadataRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ClipEditorialContext context = request.Context;
        ClipEditorialProfile profile = request.Profile;
        (string title, HeuristicAudienceCopyCandidate candidate) =
            SelectUnusedAudienceCopy(request);
        string description = BuildDescription(profile, candidate.Description);
        string[] tags = BuildTags(
            context,
            profile);
        ClipEditorialWarning[] warnings = BuildWarnings(
            context,
            candidate.IsGrounded);
        IReadOnlyList<ClipEditorialMetadataQualityIssue> qualityIssues =
            ClipEditorialMetadataQuality.Evaluate(
                title,
                description,
                context);
        ClipEditorialEvidenceReference[] evidence = context.Evidence
            .Concat(candidate.Evidence)
            .Concat(HeuristicAudienceCopyPolicy.StableOcrEvidence(context))
            .Concat(
                context.Transcripts.Select(
                    transcriptContext =>
                        new ClipEditorialEvidenceReference(
                            $"stream-{transcriptContext.AbsoluteAudioStreamIndex}",
                            MapEvidenceKind(transcriptContext.Role.Role),
                            $"{transcriptContext.Authority} {transcriptContext.Role.Role} transcript from absolute audio stream {transcriptContext.AbsoluteAudioStreamIndex}.")))
            .GroupBy(
                static reference => reference.Id,
                StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        var groundingAudit = new GameKnowledgeInfluenceAudit(
            context.EditorialBrief.Fingerprint,
            request.RevisionKind,
            usedClaimIds: [],
            usedEvidenceIds: candidate.IsGrounded
                ? candidate.Evidence.Select(static item => item.Id).ToArray()
                : [],
            needsReview: true,
            fallbackReason:
                "The user selected no-AI heuristics, so this remains a reviewable local working label.",
            resolvedBrief: context.EditorialBrief,
            sourceBindings: []);

        return Task.FromResult(
            new ClipEditorialMetadataDraft(
                title,
                description,
                tags,
                ClipEditorialMetadataOrigin.Heuristic,
                Identity,
                request.Attempt,
                evidence,
                warnings,
                readiness: ClipEditorialMetadataReadiness.WorkingLabel,
                qualityIssues: qualityIssues,
                priorAcceptedTitles: request.PriorAcceptedTitleExclusions
                    .Select(static value => value.Title),
                groundingAudit: groundingAudit));
    }

    private static string BuildTitle(
        ClipEditorialContext context,
        string titleBody)
    {
        string hashtag = context.GameContext.AudienceGameHashtag;
        string label = BuildTitleExcerpt(
            titleBody,
            ClipEditorialMetadataDraft.MaximumTitleLength);
        string title = $"{label} {hashtag}";

        return PreserveGameHashtag(
            NormalizeWhitespace(title),
            hashtag,
            ClipEditorialMetadataDraft.MaximumTitleLength);
    }

    private static string PreserveGameHashtag(
        string title,
        string hashtag,
        int maximumLength)
    {
        string withoutHashtag = Regex.Replace(
            title,
            Regex.Escape(hashtag),
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        withoutHashtag = NormalizeWhitespace(
            RemoveGeneratedHashtags(withoutHashtag));
        int contentLimit = Math.Max(1, maximumLength - hashtag.Length - 1);
        string content = TrimToBoundary(withoutHashtag, contentLimit);
        return $"{content} {hashtag}".Trim();
    }

    private static string BuildDescription(
        ClipEditorialProfile profile,
        string audienceDescription)
    {
        string description = RemoveGeneratedHashtags(
            NormalizeWhitespace(audienceDescription));
        if (!string.IsNullOrWhiteSpace(
                profile.ReusableDescriptionSignature))
        {
            description +=
                Environment.NewLine + Environment.NewLine +
                profile.ReusableDescriptionSignature;
        }

        return TrimToBoundary(
            description,
            ClipEditorialMetadataDraft.MaximumDescriptionLength);
    }

    private static string[] BuildTags(
        ClipEditorialContext context,
        ClipEditorialProfile profile)
    {
        var groundedTags = new List<string>();
        if (context.Transcripts.Any(
                static item =>
                    item.Role.Role is AudioContentRole.CreatorSpeech or
                        AudioContentRole.MixedSpeech))
        {
            groundedTags.Add("commentary");
        }
        if (context.Transcripts.Any(
                static item =>
                    item.Role.Role is AudioContentRole.GameDialogue or
                        AudioContentRole.MixedSpeech))
        {
            groundedTags.Add("game dialogue");
        }

        return ClipEditorialGeneratedTags.Build(
            context,
            profile.DefaultTags,
            groundedTags);
    }

    private static string BuildTitleExcerpt(string text, int maximum)
    {
        string normalized = NormalizeWhitespace(
                RemoveGeneratedHashtags(text))
            .Trim('"', '\'', '“', '”');
        return TrimToBoundary(normalized, maximum)
            .TrimEnd('.', ',', ';', ':', '!', '?');
    }

    private static (string Title, HeuristicAudienceCopyCandidate Candidate)
        SelectUnusedAudienceCopy(ClipEditorialMetadataRequest request)
    {
        (string Title, HeuristicAudienceCopyCandidate Candidate)[] candidates =
            HeuristicAudienceCopyPolicy.Build(request.Context)
                .Select(candidate => (
                    BuildTitle(request.Context, candidate.TitleBody),
                    candidate))
                .GroupBy(
                    static candidate => candidate.Item1,
                    StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToArray();

        int start = request.Attempt % candidates.Length;
        for (int offset = 0; offset < candidates.Length; offset++)
        {
            (string title, HeuristicAudienceCopyCandidate candidate) =
                candidates[(start + offset) % candidates.Length];
            ClipEditorialTitleDiversityResult diversity =
                ClipEditorialTitleDiversityPolicy.Evaluate(
                    title,
                    request.Context.GameContext.AudienceGameHashtag,
                    request.PriorAcceptedTitleExclusions.Select(
                        static prior => prior.Title));
            if (diversity.IsMateriallyDistinct)
            {
                return (title, candidate);
            }
        }

        throw new ClipEditorialMetadataVariationUnavailableException(
            "Replay Foundry has no unused title ideas left for this clip. " +
            "Edit the current wording or use local AI to try a different angle.");
    }

    private static ClipEditorialWarning[] BuildWarnings(
        ClipEditorialContext context,
        bool hasQualifiedVisualEvidence)
    {
        var warnings = new List<ClipEditorialWarning>();
        if (context.Transcripts.Count == 0)
        {
            warnings.Add(
                new ClipEditorialWarning(
                    ClipEditorialWarningCode.TranscriptUnavailable,
                    "No user-selected transcript was available, so the draft does not claim spoken content."));
        }
        if (!hasQualifiedVisualEvidence)
        {
            warnings.Add(
                new ClipEditorialWarning(
                    ClipEditorialWarningCode.VisualObservationUnavailable,
                    "No qualified visual, stable on-screen text, or " +
                    "human-reviewed speech was available, so the safe " +
                    "audience draft stays broad."));
        }
        if (context.Transcripts.Any(
                static transcript =>
                    transcript.Role.Role == AudioContentRole.Unknown))
        {
            warnings.Add(
                new ClipEditorialWarning(
                    ClipEditorialWarningCode.AudioRoleUnknown,
                    "An audio stream has no user-confirmed semantic role and was not used as creator or game-dialogue authority."));
        }
        if (!hasQualifiedVisualEvidence)
        {
            warnings.Add(
                new ClipEditorialWarning(
                    ClipEditorialWarningCode.LimitedGrounding,
                    context.Transcripts.Count > 0
                        ? "The broad audience draft avoids details from " +
                          "unreviewed automatic transcripts. Those words " +
                          "remain editable captions and were not promoted " +
                          "into the title or description."
                        : "The broad audience draft avoids inventing clip " +
                          "details when no audience-authorized local " +
                          "evidence is available."));
        }
        warnings.Add(
            new ClipEditorialWarning(
                ClipEditorialWarningCode.MetadataReviewRequired,
                hasQualifiedVisualEvidence
                    ? "The fail-soft draft uses only audience-authorized " +
                      "local evidence and remains editable in Studio."
                    : "The fail-soft draft remains deliberately broad and " +
                      "editable because specific clip details were not " +
                      "available."));
        if (!context.GameContext.IsUserGrounded)
        {
            warnings.Add(
                new ClipEditorialWarning(
                    ClipEditorialWarningCode.GameContextUnconfirmed,
                    "The game name is a folder-name hint. Confirm it in Generation Setup before treating it as title authority."));
        }

        return warnings.ToArray();
    }

    private static ClipEditorialEvidenceKind MapEvidenceKind(
        AudioContentRole role) =>
        role switch
        {
            AudioContentRole.CreatorSpeech =>
                ClipEditorialEvidenceKind.CreatorTranscript,
            AudioContentRole.GameDialogue =>
                ClipEditorialEvidenceKind.GameDialogueTranscript,
            AudioContentRole.MixedSpeech =>
                ClipEditorialEvidenceKind.MixedTranscript,
            _ => ClipEditorialEvidenceKind.UserContext,
        };

    private static string NormalizeWhitespace(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim();

    private static string RemoveGeneratedHashtags(string value) =>
        value.Replace("#", string.Empty, StringComparison.Ordinal);

    private static string TrimToBoundary(string value, int maximum)
    {
        if (value.Length <= maximum)
        {
            return value;
        }

        int boundary = value.LastIndexOf(' ', maximum - 1);
        if (boundary < maximum / 2)
        {
            boundary = maximum;
        }
        return value[..boundary].TrimEnd(' ', ',', ';', ':', '-', '|');
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
