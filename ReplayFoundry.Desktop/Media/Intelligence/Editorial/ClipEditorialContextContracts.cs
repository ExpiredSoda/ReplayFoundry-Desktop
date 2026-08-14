using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

public sealed class ClipEditorialTranscriptContext
{
    public const int MaximumTextLength = 4_000;

    public ClipEditorialTranscriptContext(
        int absoluteAudioStreamIndex,
        AudioContentRoleAssignment role,
        string text,
        ClipEditorialTranscriptAuthority authority =
            ClipEditorialTranscriptAuthority.AutomaticUnreviewed)
    {
        if (absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(absoluteAudioStreamIndex));
        }

        ArgumentNullException.ThrowIfNull(role);
        if (!Enum.IsDefined(authority))
        {
            throw new ArgumentOutOfRangeException(nameof(authority));
        }
        if (string.IsNullOrWhiteSpace(text) ||
            text.Length > MaximumTextLength)
        {
            throw new ArgumentException(
                $"Editorial transcript context must contain at most {MaximumTextLength} characters of lexical text.",
                nameof(text));
        }

        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        Role = role;
        Text = text.Trim();
        Authority = authority;
    }

    public int AbsoluteAudioStreamIndex { get; }

    public AudioContentRoleAssignment Role { get; }

    public string Text { get; }

    public ClipEditorialTranscriptAuthority Authority { get; }

    public bool MaySupportVerbatimAudienceCopy => Authority is
        ClipEditorialTranscriptAuthority.UserCorrected or
        ClipEditorialTranscriptAuthority.HumanReviewed;
}

public enum ClipEditorialTranscriptAuthority
{
    AutomaticUnreviewed,
    UserCorrected,
    HumanReviewed,
}

public enum ClipEditorialVoicePerspective
{
    CreatorFirstPerson,
    NeutralNoSubject,
}

public enum ClipEditorialVariantIntent
{
    DirectAction,
    SpecificCuriosity,
    OutcomeFocused,
    ConcreteDetail,
    CommentaryLed,
}

public enum ClipEditorialGameContextSource
{
    SourcePathHint,
    ReusedUserMemory,
    UserConfirmed,
}

public sealed class ClipEditorialGameContext
{
    public const string ContextNotesEvidenceId = "user-game-context-notes";

    public ClipEditorialGameContext(
        string gameName,
        string gameHashtag,
        string? contextNotes,
        ClipEditorialGameContextSource source,
        bool useOpenGameKnowledge = false)
    {
        if (string.IsNullOrWhiteSpace(gameName) ||
            gameName.Trim().Length > 120 ||
            string.IsNullOrWhiteSpace(gameHashtag) ||
            !gameHashtag.StartsWith('#') ||
            gameHashtag.Length is < 2 or > 121 ||
            gameHashtag.Skip(1).Any(static value => !char.IsLetterOrDigit(value)) ||
            !Enum.IsDefined(source))
        {
            throw new ArgumentException(
                "Editorial game context requires a bounded name, canonical hashtag, and typed source.");
        }
        string? notes = string.IsNullOrWhiteSpace(contextNotes)
            ? null
            : contextNotes.Trim();
        if (notes?.Length > 1_500)
        {
            throw new ArgumentException(
                "Editorial game context notes cannot exceed 1,500 characters.",
                nameof(contextNotes));
        }

        GameName = gameName.Trim();
        GameHashtag = gameHashtag.Trim();
        ContextNotes = notes;
        Source = source;
        UseOpenGameKnowledge = useOpenGameKnowledge;
    }

    public string GameName { get; }

    public string GameHashtag { get; }

    public string? ContextNotes { get; }

    public ClipEditorialGameContextSource Source { get; }

    public bool UseOpenGameKnowledge { get; }

    public bool IsUserGrounded => Source is
        ClipEditorialGameContextSource.UserConfirmed or
        ClipEditorialGameContextSource.ReusedUserMemory;

    internal ClipEditorialEvidenceReference? CreateContextNotesEvidence() =>
        IsUserGrounded && ContextNotes is not null
            ? new ClipEditorialEvidenceReference(
                ContextNotesEvidenceId,
                ClipEditorialEvidenceKind.UserGameContext,
                $"Locally retained game-context notes ({Source}): {ContextNotes}")
            : null;
}

public sealed class ClipEditorialProfile
{
    public const string DefaultNamingGuidance =
        "Write concise creator-ready short-form copy. Lead with the most " +
        "specific supported action or object. Avoid camera-view, " +
        "player/character, and scene-inventory boilerplate when the visible " +
        "action can stand on its own. Keep the description to one or two " +
        "natural sentences, not a shot-by-shot recap.";

    private readonly ReadOnlyCollection<string> _defaultTags;

    public ClipEditorialProfile(
        string audienceAddress = "Chat",
        string? namingGuidance = DefaultNamingGuidance,
        string? reusableDescriptionSignature = null,
        IEnumerable<string>? defaultTags = null,
        ClipEditorialVoicePerspective voicePerspective =
            ClipEditorialVoicePerspective.CreatorFirstPerson)
    {
        if (!Enum.IsDefined(voicePerspective))
        {
            throw new ArgumentOutOfRangeException(nameof(voicePerspective));
        }
        AudienceAddress = RequiredBounded(
            audienceAddress,
            40,
            nameof(audienceAddress));
        NamingGuidance = OptionalBounded(
            namingGuidance,
            300,
            nameof(namingGuidance));
        ReusableDescriptionSignature = OptionalBounded(
            reusableDescriptionSignature,
            1_500,
            nameof(reusableDescriptionSignature));
        VoicePerspective = voicePerspective;

        string[] tags = (defaultTags ?? [])
            .Select(NormalizeTag)
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        _defaultTags = Array.AsReadOnly(tags);
    }

    public string AudienceAddress { get; }

    public string? NamingGuidance { get; }

    public string? ReusableDescriptionSignature { get; }

    public IReadOnlyList<string> DefaultTags => _defaultTags;

    public ClipEditorialVoicePerspective VoicePerspective { get; }

    public static ClipEditorialProfile Default { get; } = new();

    internal static string NormalizeTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim().TrimStart('#');
        return trimmed.Length <= 60
            ? trimmed
            : trimmed[..60].TrimEnd();
    }

    private static string RequiredBounded(
        string value,
        int maximum,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum
            ? throw new ArgumentException(
                $"{parameterName} must contain between 1 and {maximum} characters.",
                parameterName)
            : value.Trim();

    private static string? OptionalBounded(
        string? value,
        int maximum,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maximum
            ? trimmed
            : throw new ArgumentException(
                $"{parameterName} cannot exceed {maximum} characters.",
                parameterName);
    }
}

public sealed class ClipEditorialContext
{
    private readonly ReadOnlyCollection<ClipEditorialTranscriptContext>
        _transcripts;
    private readonly ReadOnlyCollection<ClipEditorialEvidenceReference>
        _evidence;

    public ClipEditorialContext(
        string candidateId,
        string sourceFullPath,
        string sourceLabel,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        TimeSpan sourceDuration,
        double deterministicScore,
        string deterministicReason,
        IEnumerable<ClipEditorialTranscriptContext>? transcripts = null,
        IEnumerable<ClipEditorialEvidenceReference>? evidence = null,
        ClipEditorialGameContext? gameContext = null,
        ClipGameKnowledgeContext? gameKnowledge = null,
        NormalizedRectangle? gameplayRegion = null,
        ClipVisualTextContext? visualText = null)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            string.IsNullOrWhiteSpace(sourceLabel) ||
            string.IsNullOrWhiteSpace(deterministicReason) ||
            string.IsNullOrWhiteSpace(sourceFullPath) ||
            !Path.IsPathFullyQualified(sourceFullPath))
        {
            throw new ArgumentException(
                "Editorial context requires candidate, source, label, and deterministic reason values.");
        }
        if (sourceStart < TimeSpan.Zero ||
            sourceEnd <= sourceStart ||
            sourceEnd > sourceDuration ||
            deterministicScore is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStart));
        }

        CandidateId = candidateId.Trim();
        SourceFullPath = Path.GetFullPath(sourceFullPath);
        SourceLabel = sourceLabel.Trim();
        GameContext = gameContext ?? new ClipEditorialGameContext(
            SourceLabel,
            "#" + string.Concat(SourceLabel.Where(char.IsLetterOrDigit)),
            contextNotes: null,
            ClipEditorialGameContextSource.SourcePathHint);
        ClipEditorialTranscriptContext[] transcriptSnapshot =
            transcripts?.ToArray() ?? [];
        var evidenceSnapshot = new List<ClipEditorialEvidenceReference>(
            evidence?.ToArray() ?? []);
        ClipEditorialEvidenceReference? contextNotesEvidence =
            GameContext.CreateContextNotesEvidence();
        if (contextNotesEvidence is not null)
        {
            ClipEditorialEvidenceReference? existing = evidenceSnapshot
                .SingleOrDefault(value => value.Id.Equals(
                    ClipEditorialGameContext.ContextNotesEvidenceId,
                    StringComparison.Ordinal));
            if (existing is null)
            {
                evidenceSnapshot.Add(contextNotesEvidence);
            }
            else if (existing.Kind != contextNotesEvidence.Kind ||
                     !existing.Description.Equals(
                         contextNotesEvidence.Description,
                         StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The reserved game-context note evidence must match the retained local note.",
                    nameof(evidence));
            }
        }
        if (transcriptSnapshot.Any(static value => value is null) ||
            evidenceSnapshot.Any(static value => value is null) ||
            transcriptSnapshot
                .Select(static value => value.AbsoluteAudioStreamIndex)
                .Distinct()
                .Count() != transcriptSnapshot.Length ||
            evidenceSnapshot
                .Select(static value => value.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() != evidenceSnapshot.Count)
        {
            throw new ArgumentException(
                "Editorial transcript streams and evidence references must be non-null and unique.");
        }

        if (gameKnowledge is not null &&
            !gameKnowledge.GameName.Equals(
                GameContext.GameName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Editorial game knowledge must match the confirmed game context.",
                nameof(gameKnowledge));
        }
        GameKnowledge = gameKnowledge;
        GameplayRegion = gameplayRegion;
        if (visualText is not null &&
            (!visualText.CandidateId.Equals(
                candidateId,
                StringComparison.Ordinal) ||
             !visualText.SourceFullPath.Equals(
                 sourceFullPath,
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Editorial visual text must match the candidate and source.",
                nameof(visualText));
        }
        VisualText = visualText;
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
        SourceDuration = sourceDuration;
        DeterministicScore = deterministicScore;
        DeterministicReason = deterministicReason.Trim();
        _transcripts = Array.AsReadOnly(transcriptSnapshot);
        _evidence = Array.AsReadOnly(evidenceSnapshot.ToArray());
    }

    public string CandidateId { get; }

    public string SourceFullPath { get; }

    public string SourceLabel { get; }

    public ClipEditorialGameContext GameContext { get; }

    public ClipGameKnowledgeContext? GameKnowledge { get; }

    public NormalizedRectangle? GameplayRegion { get; }

    public ClipVisualTextContext? VisualText { get; }

    public TimeSpan SourceStart { get; }

    public TimeSpan SourceEnd { get; }

    public TimeSpan SourceDuration { get; }

    public TimeSpan Duration => SourceEnd - SourceStart;

    public double DeterministicScore { get; }

    public string DeterministicReason { get; }

    public IReadOnlyList<ClipEditorialTranscriptContext> Transcripts =>
        _transcripts;

    public IReadOnlyList<ClipEditorialEvidenceReference> Evidence =>
        _evidence;

    public ClipEditorialContext WithTranscripts(
        IEnumerable<ClipEditorialTranscriptContext> transcripts) =>
        new(
            CandidateId,
            SourceFullPath,
            SourceLabel,
            SourceStart,
            SourceEnd,
            SourceDuration,
            DeterministicScore,
            DeterministicReason,
            transcripts,
            Evidence,
            GameContext,
            GameKnowledge,
            GameplayRegion,
            VisualText);

    public ClipEditorialContext WithSourceRange(
        TimeSpan sourceStart,
        TimeSpan sourceEnd) =>
        new(
            CandidateId,
            SourceFullPath,
            SourceLabel,
            sourceStart,
            sourceEnd,
            SourceDuration,
            DeterministicScore,
            DeterministicReason,
            Transcripts,
            Evidence,
            GameContext,
            GameKnowledge,
            GameplayRegion,
            VisualText);

    public ClipEditorialContext WithGameKnowledge(
        ClipGameKnowledgeContext? gameKnowledge) =>
        new(
            CandidateId,
            SourceFullPath,
            SourceLabel,
            SourceStart,
            SourceEnd,
            SourceDuration,
            DeterministicScore,
            DeterministicReason,
            Transcripts,
            Evidence,
            GameContext,
            gameKnowledge,
            GameplayRegion,
            VisualText);

    public ClipEditorialContext WithVisualText(
        ClipVisualTextContext? visualText) =>
        new(
            CandidateId,
            SourceFullPath,
            SourceLabel,
            SourceStart,
            SourceEnd,
            SourceDuration,
            DeterministicScore,
            DeterministicReason,
            Transcripts,
            Evidence,
            GameContext,
            GameKnowledge,
            GameplayRegion,
            visualText);
}
