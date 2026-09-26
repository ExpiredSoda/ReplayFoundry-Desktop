using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static Task AutomaticCommentaryAuthorityIsBoundedAndOptIn()
    {
        const string hats = "What kind of hat am I wearing?";
        const string mines = "I guess a mine is a bomb, right?";
        const string running = "I think we have to run to because I think it turns back on if you take your time.";
        const string description = "A boat drifted across dark water beside a distant light.";
        var visibleAction = new Qwen3VlGroundedMetadataVisualDraft(
            1, 0, 10, "Dark water", false, ["A boat", "A distant light"],
            [description], [], []);
        foreach ((string angle, string title) in new[]
        {
            (hats, "I wondered about that hat"),
            (mines, "I compared a mine with a bomb"),
            (running, "I wondered about running out of time"),
        })
        {
            ClipEditorialMetadataRequest request = AutomaticCommentaryAuthorityRequest(angle);
            string fingerprint = request.Context.EditorialBrief.Fingerprint;
            TestAssert.True(Qwen3VlGroundedMetadataRules.HasSafeAutomaticCommentarySupport(
                    request, title + "\n" + description),
                "The recorded nomination may authorize only its bounded attribution grammar: " + angle);
            TestAssert.Throws<Qwen3VlOutputParseException>(
                () => Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
                    title, description, ["boat"], request, visibleAction,
                    Qwen3VlGroundedMetadataActorAuthority.Unknown,
                    Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
                    creatorAuthorityUsesAudienceFieldsOnly: true),
                "Omitting the new opt-in must preserve historical creator-authority rejection.");
            Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
                title, description, ["boat"], request, visibleAction,
                Qwen3VlGroundedMetadataActorAuthority.Unknown,
                Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
                creatorAuthorityUsesAudienceFieldsOnly: true,
                allowAutomaticCommentaryAttribution: true);
            TestAssert.Equal(fingerprint, request.Context.EditorialBrief.Fingerprint,
                "Checking attribution cannot change the nomination's historical identity.");
            TestAssert.Equal(ClipEditorialTranscriptAuthority.AutomaticUnreviewed,
                request.Context.Transcripts.Single().Authority,
                "Permitting attribution must never relabel automatic speech as reviewed.");
            TestAssert.True(request.Context.EditorialBrief.Claims
                    .Where(static claim => claim.Kind == GroundedGameContextClaimKind.CreatorCommentaryCue)
                    .All(static claim => claim.FieldAuthorizations.Count == 0),
                "The grammar exception must not grant factual audience-field authority.");
        }

        foreach (ClipEditorialMetadataRequest unsupported in new[]
        {
            AutomaticCommentaryAuthorityRequest(null),
            AutomaticCommentaryAuthorityRequest(hats, includeFlag: false),
            AutomaticCommentaryAuthorityRequest(hats, transcriptText: "The boat reached another channel."),
            AutomaticCommentaryAuthorityRequest(hats, role: AudioContentRole.GameDialogue),
            AutomaticCommentaryAuthorityRequest(hats, authority: ClipEditorialTranscriptAuthority.HumanReviewed),
        })
            TestAssert.False(Qwen3VlGroundedMetadataRules.HasSafeAutomaticCommentarySupport(
                    unsupported, "I wondered about that hat"),
                "A stale/missing nomination, flag, creator role or automatic transcript cannot authorize attribution.");

        ClipEditorialMetadataRequest hatRequest = AutomaticCommentaryAuthorityRequest(hats);
        foreach (string unsupported in new[]
        {
            "I wore that hat", "My hat was a bomb", "I compared the hat with an explosive",
            "I wondered about that hat and escaped", "I wondered about that hat. I ran away.",
            "I wondered about that hat or did it", "I wondered what kind of hat am I wearing",
            "I wondered about \"hat\"", "I wondered about 'hat'", "I wondered about “hat”",
            "I wondered about ‘hat’", "I wondered about «hat»", "I wondered about 「hat」",
        })
        {
            TestAssert.False(Qwen3VlGroundedMetadataRules.HasSafeAutomaticCommentarySupport(hatRequest, unsupported),
                "Automatic attribution must not permit body actions, possession, another subject, quotation or coordinated action: " + unsupported);
            TestAssert.Throws<Qwen3VlOutputParseException>(
                () => Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
                    unsupported, description, ["boat"], hatRequest, visibleAction,
                    Qwen3VlGroundedMetadataActorAuthority.Unknown,
                    Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
                    creatorAuthorityUsesAudienceFieldsOnly: true,
                    allowAutomaticCommentaryAttribution: true),
                "Explicit opt-in must retain independent rejection of unsupported creator claims.");
        }

        ClipEditorialMetadataRequest negated = AutomaticCommentaryAuthorityRequest(
            "I did not compare a mine with a bomb, right?");
        TestAssert.False(Qwen3VlGroundedMetadataRules.HasSafeAutomaticCommentarySupport(
                negated, "I compared the mine to the bomb"),
            "Shared topic words cannot turn an explicitly negated comparison into an asserted creator act.");
        TestAssert.False(Qwen3VlGroundedMetadataRules.HasSafeAutomaticCommentarySupport(
                negated, "I wondered whether the mine was a bomb"),
            "A negated nomination permits no affirmative or polar comparison replacement.");
        TestAssert.True(Qwen3VlGroundedMetadataRules.HasSafeAutomaticCommentarySupport(
                negated, "I wondered about the mine"),
            "The narrow neutral topic attribution remains possible without asserting the negated relation.");

        TestAssert.True(Qwen3VlGroundedMetadataRules.HasSafeAutomaticCommentarySupport(
                AutomaticCommentaryAuthorityRequest("Why is that HAT-shaped thing odd?"), "I wondered about that hat"),
            "ASCII case and hyphen token boundaries must agree with the Python contract.");
        TestAssert.True(Qwen3VlGroundedMetadataRules.HasSafeAutomaticCommentarySupport(
                AutomaticCommentaryAuthorityRequest("Why are those hats odd?"), "I wondered about the hat"),
            "The bounded plural morphology should retain the same topic without adding facts.");
        TestAssert.False(Qwen3VlGroundedMetadataRules.HasSafeAutomaticCommentarySupport(
                AutomaticCommentaryAuthorityRequest(running), "I wondered about the reactivation"),
            "Run/running morphology cannot authorize an invented reactivation claim.");

        // Saved text can be abbreviated independently of retained measured spans.
        // These source-clock spans join the same complete nomination, not invented words.
        ClipEditorialMetadataRequest spanRequest = AutomaticCommentaryAuthorityRequest(
            running, transcriptText: "I think we have to run to because I think it turns back on if you take your",
            spans:
            [
                new(TimeSpan.FromSeconds(960), TimeSpan.FromSeconds(965),
                    "I think we have to run to because I think it turns back on if you take your"),
                new(TimeSpan.FromSeconds(965), TimeSpan.FromSeconds(966), "time."),
            ]);
        TestAssert.True(Qwen3VlGroundedMetadataRules.HasSafeAutomaticCommentarySupport(
                spanRequest, "I wondered about running out of time"),
            "The complete nomination may match its retained measured span text when summary text omits the ending.");
        return Task.CompletedTask;
    }

    private static ClipEditorialMetadataRequest AutomaticCommentaryAuthorityRequest(
        string? angle,
        bool includeFlag = true,
        string? transcriptText = null,
        AudioContentRole role = AudioContentRole.CreatorSpeech,
        ClipEditorialTranscriptAuthority authority = ClipEditorialTranscriptAuthority.AutomaticUnreviewed,
        IReadOnlyList<ClipEditorialTranscriptSpan>? spans = null)
    {
        var transcript = new ClipEditorialTranscriptContext(1,
            new AudioContentRoleAssignment(role, AudioContentRoleSource.UserConfirmed),
            transcriptText ?? angle ?? "No automatic nomination was retained.", authority, spans);
        ClipEditorialContext basis = CreateContext([transcript]);
        var brief = new GroundedEditorialBrief(basis.CandidateId, basis.SourceStart, basis.SourceEnd,
            basis.EditorialBrief.Claims, canonicalIdentity: basis.GameContext.AudienceGameName,
            safeCommentaryAngle: angle,
            qualityFlags: includeFlag ? ["AutomaticCommentaryNominatesOnly", "AutomaticCreatorReactionAngleAvailable"] : []);
        var context = new ClipEditorialContext(basis.CandidateId, basis.SourceFullPath, basis.SourceLabel,
            basis.SourceStart, basis.SourceEnd, basis.SourceDuration, basis.DeterministicScore,
            basis.DeterministicReason, [transcript], basis.Evidence, basis.GameContext,
            editorialBrief: brief);
        return new(context, ClipEditorialProfile.Default, 0, ClipEditorialGenerationPreference.AiRequired);
    }
}
