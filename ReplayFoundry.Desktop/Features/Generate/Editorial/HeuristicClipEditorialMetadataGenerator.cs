using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public sealed partial class HeuristicClipEditorialMetadataGenerator :
    IClipEditorialMetadataGenerator
{
    private const string UnfinishedDescription =
        "Grounded clip details are unfinished. Review the bounded clip " +
        "in Studio and replace this working text with verified visible " +
        "details before publishing.";

    public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
        new(
            "ReplayFoundry grounded heuristics",
            "1.5.0");

    public bool IsAvailable => true;

    public Task<ClipEditorialMetadataDraft> GenerateAsync(
        ClipEditorialMetadataRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ClipEditorialContext context = request.Context;
        ClipEditorialProfile profile = request.Profile;
        (string title, ClipEditorialEvidenceReference? visualEvidence) =
            SelectUnusedGroundedTitle(request);
        string description = BuildDescription(
            profile,
            visualEvidence);
        string[] tags = BuildTags(
            context,
            profile);
        ClipEditorialWarning[] warnings = BuildWarnings(
            context,
            visualEvidence is not null);
        IReadOnlyList<ClipEditorialMetadataQualityIssue> qualityIssues =
            ClipEditorialMetadataQuality.Evaluate(
                title,
                description,
                context);
        ClipEditorialEvidenceReference[] evidence = context.Evidence
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
                    .Select(static value => value.Title)));
    }

    private static string BuildTitle(
        ClipEditorialContext context,
        int attempt,
        ClipEditorialEvidenceReference? visualEvidence)
    {
        string hashtag = context.GameContext.GameHashtag;
        string label = visualEvidence is null
            ? ChooseUnfinishedWorkingLabel(attempt)
            : BuildTitleExcerpt(
                visualEvidence.Description,
                ClipEditorialMetadataDraft.MaximumTitleLength);
        if (string.IsNullOrWhiteSpace(label))
        {
            label = ChooseUnfinishedWorkingLabel(attempt);
        }
        string title = $"{label} {hashtag}";

        return PreserveGameHashtag(
            NormalizeWhitespace(title),
            hashtag,
            ClipEditorialMetadataDraft.MaximumTitleLength);
    }

    private static string ChooseUnfinishedWorkingLabel(int attempt) =>
        (attempt % 3) switch
        {
            0 => "Unfinished — Studio review needed",
            1 => "Unfinished — grounded clip details needed",
            _ => "Unfinished — visible clip details unverified",
        };

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
        ClipEditorialEvidenceReference? visualEvidence)
    {
        string firstLine = visualEvidence is not null
            ? BuildVisualDescription(
                visualEvidence.Description,
                220)
            : UnfinishedDescription;
        string description = RemoveGeneratedHashtags(firstLine);
        if (string.IsNullOrWhiteSpace(description))
        {
            description = UnfinishedDescription;
        }
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

    private static string BuildVisualDescription(
        string text,
        int maximum) =>
        TrimToBoundary(
            NormalizeWhitespace(text),
            maximum);

    private static (string Title, ClipEditorialEvidenceReference? Evidence)
        SelectUnusedGroundedTitle(ClipEditorialMetadataRequest request)
    {
        ClipEditorialEvidenceReference[] qualified = request.Context.Evidence
            .Where(static item =>
                item.Kind == ClipEditorialEvidenceKind.VisualObservation)
            .OrderBy(static item => item.Id, StringComparer.Ordinal)
            .ThenBy(static item => item.Description, StringComparer.Ordinal)
            .ToArray();
        (string Title, ClipEditorialEvidenceReference? Evidence)[] candidates;
        if (qualified.Length == 0)
        {
            candidates = Enumerable.Range(0, 3)
                .Select(index => (
                    BuildTitle(request.Context, index, visualEvidence: null),
                    (ClipEditorialEvidenceReference?)null))
                .ToArray();
        }
        else
        {
            // Keep every observation atomic while ordering the two strongest
            // structural alternatives first: canonical first, canonical last,
            // then the remaining observations from the outside inward.
            var ordered = new List<ClipEditorialEvidenceReference>(
                qualified.Length);
            int left = 0;
            int right = qualified.Length - 1;
            while (left <= right)
            {
                ordered.Add(qualified[left++]);
                if (left <= right)
                {
                    ordered.Add(qualified[right--]);
                }
            }
            candidates = ordered
                .Select(evidence => (
                    BuildTitle(request.Context, request.Attempt, evidence),
                    (ClipEditorialEvidenceReference?)evidence))
                .GroupBy(
                    static candidate => candidate.Item1,
                    StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToArray();
        }

        int start = request.Attempt % candidates.Length;
        for (int offset = 0; offset < candidates.Length; offset++)
        {
            (string title, ClipEditorialEvidenceReference? evidence) =
                candidates[(start + offset) % candidates.Length];
            ClipEditorialTitleDiversityResult diversity =
                ClipEditorialTitleDiversityPolicy.Evaluate(
                    title,
                    request.Context.GameContext.GameHashtag,
                    request.PriorAcceptedTitleExclusions.Select(
                        static prior => prior.Title));
            if (diversity.IsMateriallyDistinct)
            {
                return (title, evidence);
            }
        }

        throw new ClipEditorialMetadataVariationUnavailableException(
            "Replay Foundry exhausted every grounded deterministic title " +
            "available for this exact clip. Edit the current wording or use " +
            "the qualified local-AI reroll for another supported angle.");
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
                    "No qualified visual-semantic observation was " +
                    "available, so no event label was inferred from " +
                    "transcripts, audio roles, deterministic scores, " +
                    "or the game name."));
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
                        ? "The working label is explicitly unfinished " +
                          "because no qualified visual wording was " +
                          "available. Raw automatic transcripts remain " +
                          "editable captions and were not promoted into " +
                          "audience metadata."
                        : "The working label is explicitly unfinished because no qualified visual wording was available."));
        }
        warnings.Add(
            new ClipEditorialWarning(
                ClipEditorialWarningCode.MetadataReviewRequired,
                hasQualifiedVisualEvidence
                    ? "Heuristic-only metadata preserves one qualified " +
                      "visual observation without adding semantic claims, " +
                      "but it remains an editable working label. Review it " +
                      "in Studio or use the qualified local AI reroll."
                    : "Heuristic-only metadata is an explicitly unfinished " +
                      "working label, not publish-ready audience copy. " +
                      "Complete it in Studio or use the qualified local AI " +
                      "reroll."));
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
