using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static Task RecordedCutsNominateCompleteCreatorThoughts()
    {
        // The retained run26 ASR, with source times translated to each cut's clock.
        // These are nomination assertions, not expected AI titles or verified game facts.
        var hats = ThoughtTranscript(
            (1.76, 4.23, "So like I said, the story is going to be pretty obscure."),
            (4.57, 11.64, "So don't expect me or even yourself watching to understand it on the first go."),
            (15.12, 15.81, "Oh, no."), (18.68, 20.02, "What kind of hat am I wearing?"),
            (20.69, 22.19, "You do get hats in this game."), (22.65, 24.64, "So I'm wearing a hat right now."),
            (27.41, 31.03, "I think it was probably a hat that I found somewhere at some point."),
            (32.61, 33.97, "That's not the hat you start with."), (34.10, 35.85, "Actually, I don't think you start with any hats."));
        var mine = ThoughtTranscript(
            (14.43, 25.22, "Believe if you go this way, there's a hat in here. Yeah, this is the first hat"),
            (25.22, 26.10, "you can get in the game"),
            (26.36, 40.13, "Yank, there's a bomb or mine. I guess a mine is a bomb, right? Are mines bombs"),
            (40.52, 41.43, "or bombs mines?"));
        var running = ThoughtTranscript(
            (0.78, 2.80, "And we'll see this here in just a moment."),
            (2.89, 9.43, "But everything else is always bigger than the characters you play."),
            (10.13, 17.12, "Yet they're just like big enough to make you feel like"),
            (17.81, 20.41, "they're used to be normal people in this world."),
            (24.09, 28.00, "So clearly we're going to turn this off here and turn it off."),
            (29.39, 33.71, "I think we have to run to because I think it turns back on if you take your"),
            (33.71, 34.15, "time."), (37.15, 45.20, "No, your turn."));
        TestAssert.Equal("What kind of hat am I wearing?", ThoughtContext(hats).EditorialBrief.SafeCommentaryAngle,
            "A complete hat question should beat filler and speculative incomplete snippets.");
        TestAssert.Equal("I guess a mine is a bomb, right?", ThoughtContext(mine).EditorialBrief.SafeCommentaryAngle,
            "The mine question remains explicitly speculative unreviewed speech.");
        TestAssert.Equal("I think we have to run to because I think it turns back on if you take your time.",
            ThoughtContext(running).EditorialBrief.SafeCommentaryAngle,
            "The run instruction must retain its measured completion rather than ending at 'your'.");
        return Task.CompletedTask;
    }

    private static Task CommentaryNominationCompletesRecordedThought()
    {
        const string first = "I think we have to run to because I think it turns back on if you take your";
        const string complete = first + " time.";
        ClipEditorialContext context = ThoughtContext(ThoughtTranscript(
            (0, 7, first), (7, 8, "time.")));
        TestAssert.Equal(complete, context.EditorialBrief.SafeCommentaryAngle,
            "The recorded continuation must remain attached; no word or punctuation may be invented.");
        TestAssert.Equal(ClipEditorialTranscriptAuthority.AutomaticUnreviewed, context.Transcripts.Single().Authority,
            "A complete nomination is still unreviewed ASR.");
        TestAssert.True(context.EditorialBrief.Claims.All(claim =>
                claim.State == GroundedGameContextClaimState.Ambiguous &&
                claim.Authority == GroundedGameContextClaimAuthority.AutomaticTranscriptCue &&
                claim.FieldAuthorizations.Count == 0),
            "Nomination must authorize no title, description or tag fact.");
        TestAssert.Equal(GroundedCreatorControlRelation.Unestablished, context.EditorialBrief.CreatorControlRelation,
            "Speech role assignment must not become creator control.");
        return Task.CompletedTask;
    }

    private static Task CommentaryNominationDeclinesUncertainFragments()
    {
        foreach (ClipEditorialTranscriptContext transcript in new[]
        {
            ThoughtTranscript((0, 7, "I think we have to run because it turns back on if you take your")),
            ThoughtTranscript((0, 7, "I think we have to run because it turns back on if you take your"), (11, 12, "time.")),
            ThoughtTranscript((0, 2, "because I think it looks like a giant.")),
            ThoughtTranscript((0, 2, "If I think it looks like a bomb.")),
            ThoughtTranscript((0, 2, "When I think it looks like a giant.")),
            ThoughtTranscript((0, 2, "I wonder what it looks like...")),
        })
            TestAssert.Equal<string?>(null, ThoughtContext(transcript).EditorialBrief.SafeCommentaryAngle,
                "Missing ending, long-gap continuation, subordinate fragment and ellipsis must not nominate a complete thought.");
        return Task.CompletedTask;
    }

    private static Task CommentaryNominationPreservesNegationAndFiltersFiller()
    {
        const string premise = "I thought it looked like a bomb.";
        const string correction = "Actually, it wasn't a bomb.";
        ClipEditorialContext corrected = ThoughtContext(ThoughtTranscript(
            (0, 3, premise), (3, 5, correction)));
        TestAssert.Equal(premise + " " + correction, corrected.EditorialBrief.SafeCommentaryAngle,
            "A tempting comparison must not omit the immediately retained correction.");
        foreach (string incompleteCorrection in new[] { "Actually, it was not a bomb because", "Actually, it was not a bomb because." })
            TestAssert.Equal<string?>(null, ThoughtContext(ThoughtTranscript((0, 3, premise), (3, 5, incompleteCorrection)))
                .EditorialBrief.SafeCommentaryAngle,
                "An incomplete retained correction must withhold the preceding premise, not silently disappear.");
        ClipEditorialContext hats = ThoughtContext(ThoughtTranscript(
            (0, 2, "So like I said, you know, I think."),
            (2, 5, "What kind of hat am I wearing?")));
        TestAssert.Equal("What kind of hat am I wearing?", hats.EditorialBrief.SafeCommentaryAngle,
            "Filler-only reaction markers must not outrank a complete creator question.");
        TestAssert.Equal<string?>(null, ThoughtContext(ThoughtTranscript(
            (0, 2, "So like I said, you know, I think."))).EditorialBrief.SafeCommentaryAngle,
            "A transcript made only of filler supplies no concrete editorial angle.");
        return Task.CompletedTask;
    }

    private static Task CommentaryNominationRefreshPreservesHistoricalContext()
    {
        const string fragment = "I think we have to run because it turns back on if you take your";
        ClipEditorialContext fresh = ThoughtContext(ThoughtTranscript((0, 5, fragment), (5, 6, "time.")));
        GroundedEditorialBrief retainedBrief = fresh.EditorialBrief.WithSafeCommentaryAngle(fragment);
        ClipEditorialContext saved = ThoughtContext(fresh.Transcripts.Single(), retainedBrief);
        string savedFingerprint = saved.EditorialBrief.Fingerprint;
        ClipEditorialContext prepared = saved.PrepareForEditorialGeneration();
        TestAssert.Equal(fragment, saved.EditorialBrief.SafeCommentaryAngle,
            "Preparing a rewrite must not change an older authored context.");
        TestAssert.Equal(savedFingerprint, saved.EditorialBrief.Fingerprint,
            "Older copy history must retain its exact context identity.");
        TestAssert.Equal(fragment + " time.", prepared.EditorialBrief.SafeCommentaryAngle,
            "A new request must use the complete thought from the same retained text.");
        TestAssert.True(savedFingerprint != prepared.EditorialBrief.Fingerprint,
            "The new request must identify its changed nomination honestly.");
        TestAssert.False(ReferenceEquals(saved, prepared),
            "An actual nomination change must create a new context without changing the retained instance.");
        TestAssert.True(ReferenceEquals(saved.Transcripts.Single(), prepared.Transcripts.Single()) &&
            saved.EditorialBrief.Claims.SequenceEqual(prepared.EditorialBrief.Claims),
            "Refresh must preserve source timing, transcript authority and all factual claims.");
        TestAssert.True(ReferenceEquals(prepared, prepared.PrepareForEditorialGeneration()),
            "No-op request preparation must preserve the exact retained context instance.");
        ClipEditorialContext incomplete = ThoughtContext(ThoughtTranscript((0, 5, fragment)));
        GroundedEditorialBrief staleAvailability = incomplete.EditorialBrief.WithSafeCommentaryAngle(fragment,
            ["AutomaticCreatorReactionAngleAvailable"]);
        ClipEditorialContext withoutAngle = ThoughtContext(incomplete.Transcripts.Single(), staleAvailability)
            .PrepareForEditorialGeneration();
        TestAssert.Equal<string?>(null, withoutAngle.EditorialBrief.SafeCommentaryAngle,
            "An unavailable continuation must not be invented when refreshing an older nomination.");
        TestAssert.False(withoutAngle.EditorialBrief.QualityFlags.Contains("AutomaticCreatorReactionAngleAvailable", StringComparer.Ordinal),
            "A null nomination must remove stale reaction availability.");
        TestAssert.True(withoutAngle.EditorialBrief.QualityFlags.Contains("AutomaticCommentaryNominatesOnly", StringComparer.Ordinal) &&
            withoutAngle.Transcripts.Single().Authority == ClipEditorialTranscriptAuthority.AutomaticUnreviewed,
            "Removing availability must retain the original trust limitation.");
        GroundedEditorialBrief nullWithStaleFlag = new("commentary-thought", TimeSpan.Zero, TimeSpan.FromSeconds(60),
            incomplete.EditorialBrief.Claims, qualityFlags: ["AutomaticCommentaryNominatesOnly", "AutomaticCreatorReactionAngleAvailable"]);
        TestAssert.False(ThoughtContext(incomplete.Transcripts.Single(), nullWithStaleFlag).PrepareForEditorialGeneration()
            .EditorialBrief.QualityFlags.Contains("AutomaticCreatorReactionAngleAvailable", StringComparer.Ordinal),
            "An already-null nomination must also remove stale availability on request preparation.");
        ClipEditorialTranscriptContext split = ThoughtTranscript((0, 1, "I"), (1, 2, "really"), (2, 3, "have"),
            (3, 4, "no"), (4, 5, "idea"), (5, 6, "what"), (6, 7, "happened?"));
        GroundedEditorialBrief oldNull = ThoughtContext(split).EditorialBrief.WithSafeCommentaryAngle(null);
        ClipEditorialContext restoredNull = ThoughtContext(split, oldNull);
        TestAssert.Equal(oldNull.Fingerprint, restoredNull.EditorialBrief.Fingerprint,
            "Explicitly retained null briefs must be restored verbatim despite a newer nomination policy.");
        TestAssert.Equal("I really have no idea what happened?", restoredNull.PrepareForEditorialGeneration().EditorialBrief.SafeCommentaryAngle,
            "Only a new request may nominate the complete thought missed by the earlier three-span limit.");
        return Task.CompletedTask;
    }

    private static Task BalancedGuidancePreservesCustomProfileAndReviewedSpeech()
    {
        const string custom = "Use understated questions and keep my saved channel voice.";
        var profile = new ClipEditorialProfile(namingGuidance: custom);
        TestAssert.Equal(custom, profile.NamingGuidance, "Balanced defaults must not replace explicitly saved wording guidance.");
        TestAssert.True(ClipEditorialProfile.Default.NamingGuidance!.Contains("Balance", StringComparison.Ordinal) &&
            ClipEditorialProfile.Default.NamingGuidance.Length <= 300,
            "The default must request a balance inside the existing bounded profile contract.");
        var reviewed = new ClipEditorialTranscriptContext(1,
            new AudioContentRoleAssignment(AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
            "My saved interpretation.", ClipEditorialTranscriptAuthority.HumanReviewed);
        ClipEditorialContext context = ThoughtContext(reviewed);
        TestAssert.True(ReferenceEquals(context, context.PrepareForEditorialGeneration()),
            "Automatic nomination refresh must preserve reviewed commentary by identity.");
        var gameSpeech = new ClipEditorialTranscriptContext(1,
            new AudioContentRoleAssignment(AudioContentRole.GameDialogue, AudioContentRoleSource.UserConfirmed),
            "I think it looks like a giant.");
        TestAssert.Equal<string?>(null, ThoughtContext(gameSpeech).EditorialBrief.SafeCommentaryAngle,
            "In-game dialogue cannot acquire a creator angle.");
        return Task.CompletedTask;
    }

    private static ClipEditorialTranscriptContext ThoughtTranscript(params (double Start, double End, string Text)[] spans) => new(
        1, new AudioContentRoleAssignment(AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
        string.Join(' ', spans.Select(span => span.Text)), ClipEditorialTranscriptAuthority.AutomaticUnreviewed,
        spans.Select(span => new ClipEditorialTranscriptSpan(TimeSpan.FromSeconds(span.Start), TimeSpan.FromSeconds(span.End), span.Text)).ToArray());

    private static ClipEditorialContext ThoughtContext(ClipEditorialTranscriptContext transcript, GroundedEditorialBrief? brief = null) => new(
        "commentary-thought", Path.GetFullPath("commentary-thought.mp4"), "Commentary source", TimeSpan.Zero,
        TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), 70, "Retained cut", [transcript], editorialBrief: brief);
}
