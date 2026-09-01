using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

public enum GroundedGameContextClaimKind
{
    GameIdentity,
    Edition,
    Developer,
    Series,
    MissionOrChapter,
    Location,
    CanonicalEntity,
    NarrativeContext,
    CreatorCommentaryCue,
    DialogueCue,
    StableReadableText,
}

public enum GroundedGameContextClaimAuthority
{
    UserConfirmedGameContext,
    ConfirmedWikidataIdentity,
    LicensedGeneralKnowledge,
    LicensedCurrentEventCandidate,
    ClipLinkedLicensedKnowledge,
    HumanReviewedTranscript,
    AutomaticTranscriptCue,
    StableLocalOcr,
}

public enum GroundedGameContextClaimState
{
    Confirmed,
    Supported,
    Ambiguous,
    Rejected,
}

public enum GroundedEditorialField
{
    Title,
    Description,
    Tags,
}

public enum GroundedCreatorControlRelation
{
    Unestablished,
    CreatorControlled,
    CreatorAffected,
    CreatorEncountered,
}

public enum GroundedEditorialPresentationKind
{
    InteractiveGameplay,
    CinematicSequence,
    InWorldRecording,
    DocumentOrLore,
    MenuOrLoadout,
    ObjectiveOrInterface,
    MixedOrTransition,
    Unclear,
}

public enum GroundedEditorialMomentKind
{
    Action,
    Progress,
    Complication,
    Discovery,
    Exposition,
    Decision,
    Outcome,
    Routine,
    Unclear,
}

public enum ClipEditorialRevisionKind
{
    InitialDraft,
    StructuralReroll,
}

public sealed record GroundedGameContextClaim
{
    public const int MaximumValueLength = 600;
    private readonly ReadOnlyCollection<string> _publicSourceIds;
    private readonly ReadOnlyCollection<string> _localEvidenceIds;
    private readonly ReadOnlyCollection<GroundedEditorialField>
        _fieldAuthorizations;

    public GroundedGameContextClaim(
        string id,
        GroundedGameContextClaimKind kind,
        string value,
        GroundedGameContextClaimAuthority authority,
        GroundedGameContextClaimState state,
        IReadOnlyList<string>? publicSourceIds = null,
        IReadOnlyList<string>? localEvidenceIds = null,
        IReadOnlyList<GroundedEditorialField>? fieldAuthorizations = null)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id.Trim().Length > 160 ||
            string.IsNullOrWhiteSpace(value) ||
            value.Trim().Length > MaximumValueLength ||
            !Enum.IsDefined(kind) ||
            !Enum.IsDefined(authority) ||
            !Enum.IsDefined(state))
        {
            throw new ArgumentException(
                "Grounded editorial claims require bounded typed values.");
        }

        Id = id.Trim();
        Kind = kind;
        Value = value.Trim();
        Authority = authority;
        State = state;
        _publicSourceIds = SnapshotIds(publicSourceIds);
        _localEvidenceIds = SnapshotIds(localEvidenceIds);
        GroundedEditorialField[] fields = (fieldAuthorizations ?? [])
            .Distinct()
            .ToArray();
        if (fields.Any(static field => !Enum.IsDefined(field)) ||
            (state is GroundedGameContextClaimState.Ambiguous or
                GroundedGameContextClaimState.Rejected) && fields.Length > 0)
        {
            throw new ArgumentException(
                "Only confirmed or supported claims can authorize audience fields.",
                nameof(fieldAuthorizations));
        }
        _fieldAuthorizations = Array.AsReadOnly(fields);
    }

    public string Id { get; }

    public GroundedGameContextClaimKind Kind { get; }

    public string Value { get; }

    public GroundedGameContextClaimAuthority Authority { get; }

    public GroundedGameContextClaimState State { get; }

    public IReadOnlyList<string> PublicSourceIds => _publicSourceIds;

    public IReadOnlyList<string> LocalEvidenceIds => _localEvidenceIds;

    public IReadOnlyList<GroundedEditorialField> FieldAuthorizations =>
        _fieldAuthorizations;

    public bool MayShapeAudienceCopy => State is
        GroundedGameContextClaimState.Confirmed or
        GroundedGameContextClaimState.Supported;

    private static ReadOnlyCollection<string> SnapshotIds(
        IEnumerable<string>? values) =>
        Array.AsReadOnly((values ?? [])
            .Select(static item => item?.Trim() ?? string.Empty)
            .Where(static item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(24)
            .ToArray());
}

public sealed class GroundedEditorialBrief
{
    public const string PolicyVersion = "grounded-editorial-brief-1.0";
    public const string BroadCopyGoal =
        "Describe one broad playthrough beat across the whole clip. Prefer the " +
        "supported objective, complication, outcome, or next step over a list " +
        "of visible objects or a frame-by-frame recap.";
    public const int MaximumClaimCount = 48;
    private readonly ReadOnlyCollection<GroundedGameContextClaim> _claims;

    public GroundedEditorialBrief(
        string candidateId,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        IReadOnlyList<GroundedGameContextClaim> claims,
        string? canonicalIdentity = null,
        string? primaryGameplayBeat = null,
        string? leadIn = null,
        string? visibleFollowThrough = null,
        string? safeCommentaryAngle = null,
        GroundedCreatorControlRelation creatorControlRelation =
            GroundedCreatorControlRelation.Unestablished,
        IReadOnlyList<string>? qualityFlags = null,
        GroundedEditorialPresentationKind presentationKind =
            GroundedEditorialPresentationKind.Unclear,
        GroundedEditorialMomentKind momentKind =
            GroundedEditorialMomentKind.Unclear)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            sourceStart < TimeSpan.Zero ||
            sourceEnd <= sourceStart)
        {
            throw new ArgumentException(
                "A grounded editorial brief requires an exact candidate cut.");
        }
        ArgumentNullException.ThrowIfNull(claims);
        GroundedGameContextClaim[] snapshot = claims.ToArray();
        if (snapshot.Length > MaximumClaimCount ||
            snapshot.Any(static value => value is null) ||
            snapshot.Select(static value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Grounded editorial brief claims must be bounded, non-null, and ID-unique.",
                nameof(claims));
        }

        CandidateId = candidateId.Trim();
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
        _claims = Array.AsReadOnly(snapshot);
        CanonicalIdentity = Optional(canonicalIdentity, 160);
        PrimaryGameplayBeat = Optional(primaryGameplayBeat, 600);
        LeadIn = Optional(leadIn, 600);
        VisibleFollowThrough = Optional(visibleFollowThrough, 600);
        SafeCommentaryAngle = Optional(safeCommentaryAngle, 600);
        if (!Enum.IsDefined(creatorControlRelation))
        {
            throw new ArgumentOutOfRangeException(nameof(creatorControlRelation));
        }
        CreatorControlRelation = creatorControlRelation;
        if (!Enum.IsDefined(presentationKind) || !Enum.IsDefined(momentKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(presentationKind),
                "Grounded editorial framing values must be defined.");
        }
        PresentationKind = presentationKind;
        MomentKind = momentKind;
        QualityFlags = SnapshotQualityFlags(qualityFlags);
        Fingerprint = ComputeFingerprint(snapshot);
    }

    public string CandidateId { get; }

    public TimeSpan SourceStart { get; }

    public TimeSpan SourceEnd { get; }

    public IReadOnlyList<GroundedGameContextClaim> SupportedClaims =>
        _claims.Where(static claim => claim.MayShapeAudienceCopy).ToArray();

    public IReadOnlyList<GroundedGameContextClaim> Claims => _claims;

    public string? CanonicalIdentity { get; }

    public string? PrimaryGameplayBeat { get; }

    public string? LeadIn { get; }

    public string? VisibleFollowThrough { get; }

    public string? SafeCommentaryAngle { get; }

    public GroundedCreatorControlRelation CreatorControlRelation { get; }

    public GroundedEditorialPresentationKind PresentationKind { get; }

    public GroundedEditorialMomentKind MomentKind { get; }

    public IReadOnlyList<string> QualityFlags { get; }

    public IReadOnlyList<GroundedEditorialSourceBinding> SourceBindings =>
        _claims.Select(static claim => new GroundedEditorialSourceBinding(
            claim.Id,
            claim.PublicSourceIds,
            claim.LocalEvidenceIds,
            claim.FieldAuthorizations)).ToArray();

    public string Fingerprint { get; }

    public int ConfirmedClaimCount => _claims.Count(static claim =>
        claim.State == GroundedGameContextClaimState.Confirmed);

    public int CorroboratedClaimCount => _claims.Count(static claim =>
        claim.State == GroundedGameContextClaimState.Supported);

    public int CandidateClaimCount => _claims.Count(static claim =>
        claim.State == GroundedGameContextClaimState.Ambiguous);

    public GroundedEditorialBrief WithResolvedGameplay(
        string? primaryGameplayBeat,
        string? leadIn,
        string? visibleFollowThrough,
        GroundedCreatorControlRelation creatorControlRelation,
        IEnumerable<string>? additionalQualityFlags = null,
        GroundedEditorialPresentationKind presentationKind =
            GroundedEditorialPresentationKind.Unclear,
        GroundedEditorialMomentKind momentKind =
            GroundedEditorialMomentKind.Unclear) =>
        new(
            CandidateId,
            SourceStart,
            SourceEnd,
            Claims,
            CanonicalIdentity,
            primaryGameplayBeat,
            leadIn,
            visibleFollowThrough,
            SafeCommentaryAngle,
            creatorControlRelation,
            QualityFlags.Concat(additionalQualityFlags ?? []).ToArray(),
            presentationKind,
            momentKind);

    internal GroundedEditorialBrief WithSafeCommentaryAngle(
        string safeCommentaryAngle,
        IEnumerable<string>? additionalQualityFlags = null) =>
        new(
            CandidateId,
            SourceStart,
            SourceEnd,
            Claims,
            CanonicalIdentity,
            PrimaryGameplayBeat,
            LeadIn,
            VisibleFollowThrough,
            safeCommentaryAngle,
            CreatorControlRelation,
            QualityFlags.Concat(additionalQualityFlags ?? []).ToArray(),
            PresentationKind,
            MomentKind);

    private string ComputeFingerprint(
        IReadOnlyList<GroundedGameContextClaim> claims)
    {
        var canonical = new StringBuilder()
            .Append(PolicyVersion).Append('|')
            .Append(CandidateId).Append('|')
            .Append(SourceStart.Ticks).Append('|')
            .Append(SourceEnd.Ticks).Append('|')
            .Append(CanonicalIdentity).Append('|')
            .Append(PrimaryGameplayBeat).Append('|')
            .Append(LeadIn).Append('|')
            .Append(VisibleFollowThrough).Append('|')
            .Append(SafeCommentaryAngle).Append('|')
            .Append(CreatorControlRelation).Append('|')
            .Append(PresentationKind).Append('|')
            .Append(MomentKind).Append('|')
            .AppendJoin(',', QualityFlags).Append('\n');
        foreach (GroundedGameContextClaim claim in claims)
        {
            canonical.Append(claim.Id).Append('|')
                .Append(claim.Kind).Append('|')
                .Append(claim.Authority).Append('|')
                .Append(claim.State).Append('|')
                .Append(claim.Value).Append('|')
                .AppendJoin(',', claim.PublicSourceIds).Append('|')
                .AppendJoin(',', claim.LocalEvidenceIds).Append('|')
                .AppendJoin(',', claim.FieldAuthorizations)
                .Append('\n');
        }
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static string? Optional(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximum
                ? value.Trim()
                : value.Trim()[..maximum].TrimEnd();

    private static IReadOnlyList<string> SnapshotQualityFlags(
        IEnumerable<string>? values) =>
        Array.AsReadOnly((values ?? [])
            .Select(static value => value?.Trim() ?? string.Empty)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToArray());
}

public sealed record GroundedEditorialSourceBinding(
    string ClaimId,
    IReadOnlyList<string> PublicSourceIds,
    IReadOnlyList<string> LocalEvidenceIds,
    IReadOnlyList<GroundedEditorialField> FieldAuthorizations);

public sealed class GameKnowledgeInfluenceAudit
{
    private readonly ReadOnlyCollection<string> _usedClaimIds;
    private readonly ReadOnlyCollection<string> _usedEvidenceIds;
    private readonly ReadOnlyCollection<GroundedEditorialSourceBinding>
        _sourceBindings;

    public GameKnowledgeInfluenceAudit(
        string briefFingerprint,
        ClipEditorialRevisionKind revisionKind,
        IReadOnlyList<string>? usedClaimIds = null,
        IReadOnlyList<string>? usedEvidenceIds = null,
        bool needsReview = false,
        string? fallbackReason = null,
        GroundedEditorialBrief? resolvedBrief = null,
        IReadOnlyList<GroundedEditorialSourceBinding>? sourceBindings = null)
    {
        if (briefFingerprint is null ||
            briefFingerprint.Length != 64 ||
            briefFingerprint.Any(static value => !Uri.IsHexDigit(value)) ||
            !Enum.IsDefined(revisionKind))
        {
            throw new ArgumentException(
                "A grounding audit requires a brief fingerprint and revision kind.");
        }
        _usedClaimIds = SnapshotIds(usedClaimIds);
        _usedEvidenceIds = SnapshotIds(usedEvidenceIds);
        GroundedEditorialSourceBinding[] bindings =
            sourceBindings?.ToArray() ?? [];
        if (bindings.Any(static value => value is null) ||
            bindings.Select(static value => value.ClaimId)
                .Distinct(StringComparer.Ordinal).Count() != bindings.Length)
        {
            throw new ArgumentException(
                "Influence-audit source bindings must be non-null and claim-unique.",
                nameof(sourceBindings));
        }
        _sourceBindings = Array.AsReadOnly(bindings);
        BriefFingerprint = briefFingerprint.ToLowerInvariant();
        RevisionKind = revisionKind;
        NeedsReview = needsReview;
        ResolvedBrief = resolvedBrief;
        FallbackReason = string.IsNullOrWhiteSpace(fallbackReason)
            ? null
            : fallbackReason.Trim().Length <= 320
                ? fallbackReason.Trim()
                : fallbackReason.Trim()[..320];
    }

    public string BriefFingerprint { get; }

    public ClipEditorialRevisionKind RevisionKind { get; }

    public IReadOnlyList<string> UsedClaimIds => _usedClaimIds;

    public IReadOnlyList<string> UsedEvidenceIds => _usedEvidenceIds;

    public IReadOnlyList<string> UsedLocalEvidenceIds => _usedEvidenceIds;

    public IReadOnlyList<string> UsedPublicSourceIds => _sourceBindings
        .SelectMany(static binding => binding.PublicSourceIds)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<GroundedEditorialSourceBinding> SourceBindings =>
        _sourceBindings;

    public GroundedEditorialBrief? ResolvedBrief { get; }

    public bool NeedsReview { get; }

    public string? FallbackReason { get; }

    private static ReadOnlyCollection<string> SnapshotIds(
        IEnumerable<string>? values) =>
        Array.AsReadOnly((values ?? [])
            .Select(static value => value?.Trim() ?? string.Empty)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(64)
            .ToArray());
}

internal static class GroundedEditorialBriefBuilder
{
    private enum LocalEvidenceModality
    {
        QualifiedVisual,
        StableOcr,
        ReviewedCommentary,
        AutomaticCommentary,
    }

    internal static GroundedEditorialBrief Build(
        string candidateId,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        ClipEditorialGameContext game,
        ClipGameKnowledgeContext? knowledge,
        IReadOnlyList<ClipEditorialTranscriptContext> transcripts,
        IReadOnlyList<ClipEditorialEvidenceReference> evidence,
        ClipVisualTextContext? visualText)
    {
        var claims = new List<GroundedGameContextClaim>();
        AddGameClaims(
            claims,
            game,
            knowledge?.Snapshot?.ConfirmedIdentity ?? game.ConfirmedIdentity);
        AddKnowledgeClaims(
            claims,
            knowledge,
            transcripts,
            evidence,
            visualText);
        AddTranscriptClaims(claims, transcripts);
        AddVisualTextClaims(claims, visualText);
        string? canonicalIdentity = claims.FirstOrDefault(static claim =>
            claim.Kind == GroundedGameContextClaimKind.GameIdentity &&
            claim.State == GroundedGameContextClaimState.Confirmed)?.Value;
        string? primaryBeat = evidence.FirstOrDefault(static item =>
            item.Kind == ClipEditorialEvidenceKind.VisualObservation)?
            .Description;
        string? safeCommentaryAngle = ResolveSafeCommentaryAngle(
            claims,
            transcripts);
        var qualityFlags = new List<string>();
        if (canonicalIdentity is null) qualityFlags.Add("IdentityUnconfirmed");
        if (primaryBeat is null) qualityFlags.Add("PrimaryBeatPendingVisualReview");
        if (claims.Any(static claim =>
                claim.State == GroundedGameContextClaimState.Ambiguous))
        {
            qualityFlags.Add("AmbiguousContextWithheld");
        }
        bool hasAutomaticCreatorCommentary = transcripts.Any(
            static transcript =>
                transcript.Role.Role == AudioContentRole.CreatorSpeech &&
                transcript.Authority ==
                    ClipEditorialTranscriptAuthority.AutomaticUnreviewed);
        if (hasAutomaticCreatorCommentary)
        {
            qualityFlags.Add("AutomaticCommentaryNominatesOnly");
        }
        if (safeCommentaryAngle is not null &&
            hasAutomaticCreatorCommentary &&
            !claims.Any(static claim =>
                claim.Kind ==
                    GroundedGameContextClaimKind.CreatorCommentaryCue &&
                claim.State == GroundedGameContextClaimState.Confirmed &&
                claim.Value.Length > 0))
        {
            qualityFlags.Add("AutomaticCreatorReactionAngleAvailable");
        }
        qualityFlags.Add("CreatorControlUnestablished");
        return new GroundedEditorialBrief(
            candidateId,
            sourceStart,
            sourceEnd,
            claims.Take(GroundedEditorialBrief.MaximumClaimCount).ToArray(),
            canonicalIdentity,
            primaryBeat,
            leadIn: null,
            visibleFollowThrough: null,
            safeCommentaryAngle,
            GroundedCreatorControlRelation.Unestablished,
            qualityFlags.ToArray());
    }

    internal static GroundedEditorialBrief EnrichCreatorReactionAngle(
        GroundedEditorialBrief brief,
        IReadOnlyList<ClipEditorialTranscriptContext> transcripts)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(transcripts);
        if (brief.SafeCommentaryAngle is not null)
        {
            return brief;
        }

        string? safeCommentaryAngle = ResolveSafeCommentaryAngle(
            brief.Claims,
            transcripts);
        if (safeCommentaryAngle is null)
        {
            return brief;
        }

        bool isAutomatic = !brief.Claims.Any(static claim =>
            claim.Kind == GroundedGameContextClaimKind.CreatorCommentaryCue &&
            claim.State == GroundedGameContextClaimState.Confirmed);
        return brief.WithSafeCommentaryAngle(
            safeCommentaryAngle,
            isAutomatic
                ?
                [
                    "AutomaticCommentaryNominatesOnly",
                    "AutomaticCreatorReactionAngleAvailable",
                ]
                : []);
    }

    private static string? ResolveSafeCommentaryAngle(
        IReadOnlyList<GroundedGameContextClaim> claims,
        IReadOnlyList<ClipEditorialTranscriptContext> transcripts)
    {
        string? reviewed = claims.FirstOrDefault(static claim =>
            claim.Kind == GroundedGameContextClaimKind.CreatorCommentaryCue &&
            claim.State == GroundedGameContextClaimState.Confirmed)?.Value;
        if (reviewed is not null)
        {
            return reviewed;
        }

        // A known CreatorSpeech role is explicitly assigned by the user or a
        // human reviewer. Automatic ASR still has no factual field authority,
        // but its most reaction-shaped passage can safely nominate an
        // attributed analogy, question, or joke instead of being discarded in
        // favour of a literal frame inventory.
        return transcripts
            .Where(static transcript =>
                transcript.Role.Role == AudioContentRole.CreatorSpeech &&
                (transcript.Role.Source is
                    AudioContentRoleSource.UserConfirmed or
                    AudioContentRoleSource.ImportedHumanReview) &&
                transcript.Authority ==
                    ClipEditorialTranscriptAuthority.AutomaticUnreviewed)
            .SelectMany(AutomaticCommentaryCandidates)
            .Select(static value => new
            {
                Value = value,
                Score = AutomaticCommentaryScore(value),
            })
            .Where(static value => value.Score >= 4)
            .OrderByDescending(static value => value.Score)
            .ThenBy(static value => value.Value.Length)
            .Select(static value => value.Value)
            .FirstOrDefault();
    }

    private static IEnumerable<string> AutomaticCommentaryCandidates(
        ClipEditorialTranscriptContext transcript)
    {
        string[] parts = (transcript.Spans.Count > 0
                ? transcript.Spans.Select(static span => span.Text)
                : [transcript.Text])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();
        for (int start = 0; start < parts.Length; start++)
        {
            for (int count = 1;
                 count <= Math.Min(3, parts.Length - start);
                 count++)
            {
                string candidate = string.Join(
                    ' ',
                    parts.Skip(start).Take(count));
                if (candidate.Length <= 320)
                {
                    yield return candidate;
                }
            }
        }
    }

    private static int AutomaticCommentaryScore(string value)
    {
        string lexical = " " + new string(
            value.ToLowerInvariant()
                .Select(static character => char.IsLetterOrDigit(character) ||
                        character is '\'' or '’'
                    ? character
                    : ' ')
                .ToArray()) + " ";
        while (lexical.Contains("  ", StringComparison.Ordinal))
        {
            lexical = lexical.Replace("  ", " ", StringComparison.Ordinal);
        }
        int score = value.Contains('?') ? 3 : 0;
        score += ContainsAnyLexical(
            lexical,
            " i ", " me ", " my ", " we ", " us ", " our ") ? 2 : 0;
        score += ContainsAnyLexical(
            lexical,
            " like ", " reminds ", " reminded ", " influence ",
            " influences ", " vibe ", " vibes ") ? 4 : 0;
        score += ContainsAnyLexical(
            lexical,
            " guess ", " think ", " thought ", " wonder ", " wondered ",
            " why ", " how ", " weird ", " strange ", " interesting ",
            " conspiratorial ") ? 3 : 0;
        score += ContainsAnyLexical(
            lexical,
            " joke ", " funny ", " love ", " loved ", " nice ", " good ",
            " allowed ", " clearance ", " all right ", " okay ",
            " you know ") ? 2 : 0;
        score += value.Split(
                [' ', '\t', '\r', '\n', ',', '.', ':', ';', '!', '?', '-', '—'],
                StringSplitOptions.RemoveEmptyEntries)
            .Count(static token => token.Length is >= 2 and <= 8 &&
                token.Any(char.IsLetter) &&
                token.Where(char.IsLetter).All(char.IsUpper));
        int wordCount = lexical.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries).Length;
        score -= Math.Max(0, (wordCount - 18 + 3) / 4);
        score -= ContainsAnyLexical(
            lexical,
            " section ", " supervisor ", " reporting ", " mandatory ",
            " department ", " filing ", " exemption ", " deadline ") ? 4 : 0;
        return score;
    }

    private static bool ContainsAnyLexical(
        string lexical,
        params string[] markers) =>
        markers.Any(marker => lexical.Contains(marker, StringComparison.Ordinal));

    private static void AddGameClaims(
        ICollection<GroundedGameContextClaim> claims,
        ClipEditorialGameContext game,
        ConfirmedGameIdentity? identity)
    {
        if (!game.IsUserGrounded) return;
        GroundedGameContextClaimAuthority authority = identity is null
            ? GroundedGameContextClaimAuthority.UserConfirmedGameContext
            : GroundedGameContextClaimAuthority.ConfirmedWikidataIdentity;
        string[] publicSources = identity is null
            ? []
            : [identity.WikidataEntityId];
        claims.Add(ConfirmedIdentityClaim(
            "game-identity",
            GroundedGameContextClaimKind.GameIdentity,
            identity?.CanonicalTitle ?? game.GameName,
            authority,
            publicSources));
        AddOptionalIdentityClaim(claims, "game-edition",
            GroundedGameContextClaimKind.Edition, identity?.Edition, authority,
            publicSources);
        AddOptionalIdentityClaim(claims, "game-developer",
            GroundedGameContextClaimKind.Developer, identity?.Developer, authority,
            publicSources);
        AddOptionalIdentityClaim(claims, "game-series",
            GroundedGameContextClaimKind.Series, identity?.Series, authority,
            publicSources);
    }

    private static void AddOptionalIdentityClaim(
        ICollection<GroundedGameContextClaim> claims,
        string id,
        GroundedGameContextClaimKind kind,
        string? value,
        GroundedGameContextClaimAuthority authority,
        IReadOnlyList<string> publicSourceIds)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            claims.Add(ConfirmedIdentityClaim(
                id,
                kind,
                value,
                authority,
                publicSourceIds));
        }
    }

    private static GroundedGameContextClaim ConfirmedIdentityClaim(
        string id,
        GroundedGameContextClaimKind kind,
        string value,
        GroundedGameContextClaimAuthority authority,
        IReadOnlyList<string> publicSourceIds) =>
        new(
            id,
            kind,
            value,
            authority,
            GroundedGameContextClaimState.Confirmed,
            publicSourceIds,
            ["game-context"],
            [
                GroundedEditorialField.Title,
                GroundedEditorialField.Description,
                GroundedEditorialField.Tags,
            ]);

    private static void AddKnowledgeClaims(
        ICollection<GroundedGameContextClaim> claims,
        ClipGameKnowledgeContext? knowledge,
        IReadOnlyList<ClipEditorialTranscriptContext> transcripts,
        IReadOnlyList<ClipEditorialEvidenceReference> evidence,
        ClipVisualTextContext? visualText)
    {
        foreach (GameKnowledgeMatch match in knowledge?.Matches ?? [])
        {
            GroundedGameContextClaimKind kind = ClassifyKnowledge(
                match.Passage.Section);
            HashSet<LocalEvidenceModality> modalities = ResolveModalities(
                match.ClipEvidenceIds,
                transcripts,
                evidence,
                visualText);
            GameKnowledgeSource? source = knowledge?.Snapshot?.Sources
                .Single(value => value.Id.Equals(
                    match.Passage.SourceId,
                    StringComparison.Ordinal));
            bool hasStructuredIdentity = source is not null &&
                (source.Kind == GameKnowledgeSourceKind.Wikidata ||
                 source.Role == GameKnowledgeSourceRole.StructuredIdentity);
            bool hasLicensedMissionSource =
                source?.Kind == GameKnowledgeSourceKind.StrategyWiki;
            bool visualOrOcr = modalities.Contains(
                    LocalEvidenceModality.QualifiedVisual) ||
                modalities.Contains(LocalEvidenceModality.StableOcr);
            bool exactMissionSupported =
                kind == GroundedGameContextClaimKind.MissionOrChapter &&
                hasLicensedMissionSource &&
                modalities.Contains(LocalEvidenceModality.QualifiedVisual) &&
                (modalities.Contains(LocalEvidenceModality.StableOcr) ||
                 modalities.Contains(
                     LocalEvidenceModality.ReviewedCommentary));
            bool entityOrLocationSupported =
                (kind is GroundedGameContextClaimKind.CanonicalEntity or
                    GroundedGameContextClaimKind.Location) &&
                hasStructuredIdentity &&
                visualOrOcr &&
                modalities.Count(modality => modality is not
                    LocalEvidenceModality.AutomaticCommentary) >= 2;
            bool narrativeSupported =
                kind == GroundedGameContextClaimKind.NarrativeContext &&
                visualOrOcr &&
                modalities.Count(modality => modality is not
                    LocalEvidenceModality.AutomaticCommentary) >= 2;
            GroundedGameContextClaimState state =
                exactMissionSupported ||
                entityOrLocationSupported ||
                narrativeSupported
                    ? GroundedGameContextClaimState.Supported
                    : GroundedGameContextClaimState.Ambiguous;
            GroundedGameContextClaimAuthority authority = match.Strength switch
            {
                GameKnowledgeMatchStrength.GeneralContext =>
                    GroundedGameContextClaimAuthority.LicensedGeneralKnowledge,
                GameKnowledgeMatchStrength.ClipLinked =>
                    GroundedGameContextClaimAuthority.ClipLinkedLicensedKnowledge,
                _ => GroundedGameContextClaimAuthority
                    .LicensedCurrentEventCandidate,
            };
            claims.Add(new GroundedGameContextClaim(
                match.Passage.Id,
                kind,
                Bound(match.Passage.Text),
                authority,
                state,
                [match.Passage.SourceId],
                match.ClipEvidenceIds,
                state == GroundedGameContextClaimState.Supported
                    ? [
                        GroundedEditorialField.Title,
                        GroundedEditorialField.Description,
                        GroundedEditorialField.Tags,
                    ]
                    : []));
        }
    }

    private static GroundedGameContextClaimKind ClassifyKnowledge(
        string section)
    {
        string value = section.ToLowerInvariant();
        if (value.Contains("mission", StringComparison.Ordinal) ||
            value.Contains("chapter", StringComparison.Ordinal) ||
            value.Contains("level", StringComparison.Ordinal))
        {
            return GroundedGameContextClaimKind.MissionOrChapter;
        }
        if (value.Contains("location", StringComparison.Ordinal) ||
            value.Contains("setting", StringComparison.Ordinal))
        {
            return GroundedGameContextClaimKind.Location;
        }
        if (value.Contains("character", StringComparison.Ordinal) ||
            value.Contains("cast", StringComparison.Ordinal))
        {
            return GroundedGameContextClaimKind.CanonicalEntity;
        }
        return GroundedGameContextClaimKind.NarrativeContext;
    }

    private static void AddTranscriptClaims(
        ICollection<GroundedGameContextClaim> claims,
        IReadOnlyList<ClipEditorialTranscriptContext> transcripts)
    {
        int retainedCount = 0;
        foreach (ClipEditorialTranscriptContext transcript in transcripts)
        {
            bool reviewed = transcript.MaySupportVerbatimAudienceCopy;
            GroundedGameContextClaimKind kind = transcript.Role.Role ==
                    AudioContentRole.CreatorSpeech
                ? GroundedGameContextClaimKind.CreatorCommentaryCue
                : GroundedGameContextClaimKind.DialogueCue;
            IEnumerable<string> spanTexts = transcript.Spans.Count > 0
                ? transcript.Spans.Select(static span => span.Text)
                : [transcript.Text];
            int index = 0;
            foreach (string spanText in spanTexts)
            {
                if (retainedCount++ >= 12) return;
                index++;
                claims.Add(new GroundedGameContextClaim(
                    $"transcript-{transcript.AbsoluteAudioStreamIndex}-{index}",
                    kind,
                    Bound(spanText),
                    reviewed
                        ? GroundedGameContextClaimAuthority.HumanReviewedTranscript
                        : GroundedGameContextClaimAuthority.AutomaticTranscriptCue,
                    reviewed
                        ? GroundedGameContextClaimState.Confirmed
                        : GroundedGameContextClaimState.Ambiguous,
                    publicSourceIds: [],
                    localEvidenceIds:
                        [$"stream-{transcript.AbsoluteAudioStreamIndex}"],
                    fieldAuthorizations: reviewed
                        ? [
                            GroundedEditorialField.Title,
                            GroundedEditorialField.Description,
                        ]
                        : []));
            }
        }
    }

    private static void AddVisualTextClaims(
        ICollection<GroundedGameContextClaim> claims,
        ClipVisualTextContext? visualText)
    {
        if (visualText is null) return;
        int index = 0;
        foreach (VisualTextAnchor anchor in visualText.GroundingAnchors.Take(8))
        {
            index++;
            claims.Add(new GroundedGameContextClaim(
                $"stable-ocr-{index}",
                GroundedGameContextClaimKind.StableReadableText,
                Bound(anchor.DisplayText),
                GroundedGameContextClaimAuthority.StableLocalOcr,
                GroundedGameContextClaimState.Supported,
                publicSourceIds: [],
                localEvidenceIds: [anchor.EvidenceId],
                fieldAuthorizations:
                [
                    GroundedEditorialField.Title,
                    GroundedEditorialField.Description,
                ]));
        }
    }

    private static HashSet<LocalEvidenceModality> ResolveModalities(
        IReadOnlyList<string> localEvidenceIds,
        IReadOnlyList<ClipEditorialTranscriptContext> transcripts,
        IReadOnlyList<ClipEditorialEvidenceReference> evidence,
        ClipVisualTextContext? visualText)
    {
        var result = new HashSet<LocalEvidenceModality>();
        var ids = localEvidenceIds.ToHashSet(StringComparer.Ordinal);
        if (visualText?.GroundingAnchors.Any(anchor =>
                ids.Contains(anchor.EvidenceId)) == true)
        {
            result.Add(LocalEvidenceModality.StableOcr);
        }
        if (evidence.Any(item =>
                ids.Contains(item.Id) &&
                item.Kind == ClipEditorialEvidenceKind.VisualObservation))
        {
            result.Add(LocalEvidenceModality.QualifiedVisual);
        }
        foreach (ClipEditorialTranscriptContext transcript in transcripts)
        {
            if (!ids.Contains(
                    $"stream-{transcript.AbsoluteAudioStreamIndex}"))
            {
                continue;
            }
            result.Add(transcript.MaySupportVerbatimAudienceCopy
                ? LocalEvidenceModality.ReviewedCommentary
                : LocalEvidenceModality.AutomaticCommentary);
        }
        return result;
    }

    private static string Bound(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= GroundedGameContextClaim.MaximumValueLength
            ? trimmed
            : trimmed[..GroundedGameContextClaim.MaximumValueLength].TrimEnd();
    }
}
