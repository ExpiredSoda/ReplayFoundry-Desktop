using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;

namespace ReplayFoundry.PreparationTests;

internal static class HeuristicEditorialMetadataTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Heuristic gaps remain explicitly unfinished",
            MissingVisualEvidenceStaysUnfinished),
        new(
            "Heuristic visual variants preserve atomic grounded wording",
            QualifiedVisualVariantsStayAtomic),
        new(
            "Heuristic visual wording is not parsed into invented fields",
            OpaqueVisualWordingStaysOpaque),
        new(
            "Heuristic rerolls do not fake a missing visual alternative",
            SingleVisualObservationDoesNotFakeVariation),
        new(
            "Heuristic rerolls exhaust grounded titles instead of cycling",
            RerollsNeverCycleToAnAcceptedTitle),
        new(
            "Heuristic metadata keeps one canonical hashtag and grounded broad tags",
            CanonicalHashtagAndBroadTagsStayGrounded),
        new(
            "Profile tags keep comma phrases and legacy hashtag lists",
            ProfileTagParsingPreservesCompatibility),
    ];

    private static async Task MissingVisualEvidenceStaysUnfinished()
    {
        const string automaticTranscript =
            "The secret boss caused an epic hilarious final showdown.";
        ClipEditorialContext context = CreateContext(
            evidence: [],
            transcripts:
            [
                new ClipEditorialTranscriptContext(
                    1,
                    new AudioContentRoleAssignment(
                        AudioContentRole.CreatorSpeech,
                        AudioContentRoleSource.UserConfirmed),
                    automaticTranscript),
            ]);
        var generator = new HeuristicClipEditorialMetadataGenerator();

        var titles = new List<string>();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ClipEditorialMetadataDraft draft = await generator.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    context,
                    ClipEditorialProfile.Default,
                    attempt),
                CancellationToken.None);
            titles.Add(draft.Title);

            TestAssert.True(
                draft.Title.StartsWith(
                    "Unfinished — ",
                    StringComparison.Ordinal),
                "Missing visual evidence must produce an unmistakably unfinished Studio label.");
            TestAssert.True(
                draft.Title.EndsWith(
                    context.GameContext.GameHashtag,
                    StringComparison.Ordinal),
                "Even an unfinished label must retain the canonical game hashtag.");
            TestAssert.False(
                draft.Title.Contains(
                    "Commentary Highlight",
                    StringComparison.OrdinalIgnoreCase) ||
                draft.Title.Contains(
                    "Story Moment",
                    StringComparison.OrdinalIgnoreCase) ||
                draft.Title.Contains(
                    "Gameplay Highlight",
                    StringComparison.OrdinalIgnoreCase) ||
                draft.Title.Contains(
                    "A Moment From",
                    StringComparison.OrdinalIgnoreCase),
                "A missing observation must not be disguised as plausible audience metadata.");
            TestAssert.False(
                draft.Title.Contains(
                    automaticTranscript,
                    StringComparison.OrdinalIgnoreCase) ||
                draft.Description.Contains(
                    automaticTranscript,
                    StringComparison.OrdinalIgnoreCase),
                "Automatic transcript semantics must stay out of heuristic audience fields.");
            TestAssert.Equal(
                ClipEditorialMetadataReadiness.WorkingLabel,
                draft.Readiness,
                "Heuristic metadata must remain a working label.");
            TestAssert.False(
                draft.IsPublishReady,
                "An unfinished heuristic label must not become publish-ready.");
            TestAssert.True(
                draft.Tags.Contains("gaming", StringComparer.OrdinalIgnoreCase) &&
                draft.Tags.Contains("gameplay", StringComparer.OrdinalIgnoreCase) &&
                !draft.Tags.Contains("playthrough", StringComparer.OrdinalIgnoreCase),
                "The game-product boundary supports broad gaming tags, while playthrough still requires typed gameplay composition evidence.");
            TestAssert.True(
                draft.Warnings.Any(static warning =>
                    warning.Code ==
                        ClipEditorialWarningCode.VisualObservationUnavailable) &&
                draft.Warnings.Any(static warning =>
                    warning.Code == ClipEditorialWarningCode.LimitedGrounding &&
                    warning.Message.Contains(
                        "explicitly unfinished",
                        StringComparison.OrdinalIgnoreCase)) &&
                draft.Warnings.Any(static warning =>
                    warning.Code ==
                        ClipEditorialWarningCode.MetadataReviewRequired),
                "The warning set must explain why the label is unfinished and requires Studio review.");
        }

        TestAssert.Equal(
            3,
            titles.Distinct(StringComparer.Ordinal).Count(),
            "Deterministic rerolls may vary the explicit working instruction without inventing clip content.");
    }

    private static async Task QualifiedVisualVariantsStayAtomic()
    {
        const string action =
            "A brass lever moves down beside the open hatch.";
        const string outcome =
            "A green status panel remains lit above the hatch.";
        ClipEditorialContext context = CreateContext(
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual-change-01",
                    ClipEditorialEvidenceKind.VisualObservation,
                    action),
                new ClipEditorialEvidenceReference(
                    "visual-interval-02",
                    ClipEditorialEvidenceKind.VisualObservation,
                    outcome),
            ]);
        var generator = new HeuristicClipEditorialMetadataGenerator();

        ClipEditorialMetadataDraft actionLed = await GenerateAsync(
            generator,
            context,
            attempt: 0);
        ClipEditorialMetadataDraft repeated = await GenerateAsync(
            generator,
            context,
            attempt: 0);
        ClipEditorialMetadataDraft outcomeLed = await GenerateAsync(
            generator,
            context,
            attempt: 1);

        TestAssert.Equal(
            $"A brass lever moves down beside the open hatch {context.GameContext.GameHashtag}",
            actionLed.Title,
            "The action-led title must retain one complete qualified observation.");
        TestAssert.Equal(
            $"A green status panel remains lit above the hatch {context.GameContext.GameHashtag}",
            outcomeLed.Title,
            "The alternate title must retain the complete concrete state/outcome observation.");
        TestAssert.Equal(
            actionLed.Title,
            repeated.Title,
            "The same attempt must select the same atomic observation.");
        TestAssert.True(
            actionLed.Description.StartsWith(action, StringComparison.Ordinal) &&
            outcomeLed.Description.StartsWith(outcome, StringComparison.Ordinal),
            "Descriptions must preserve the selected qualified wording, including its sentence punctuation.");
        foreach (ClipEditorialMetadataDraft draft in
                 new[] { actionLed, outcomeLed })
        {
            TestAssert.Equal(
                ClipEditorialMetadataReadiness.WorkingLabel,
                draft.Readiness,
                "Qualified visual wording still requires Studio approval.");
            TestAssert.False(
                draft.Warnings.Any(static warning =>
                    warning.Code is
                        ClipEditorialWarningCode.VisualObservationUnavailable or
                        ClipEditorialWarningCode.LimitedGrounding),
                "A qualified visual observation must not receive a contradictory missing-grounding warning.");
        }
    }

    private static async Task OpaqueVisualWordingStaysOpaque()
    {
        const string observation =
            "Three pale rings; marker R-7; doorway open.";
        ClipEditorialContext context = CreateContext(
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual-observation",
                    ClipEditorialEvidenceKind.VisualObservation,
                    observation),
            ]);

        ClipEditorialMetadataDraft draft = await GenerateAsync(
            new HeuristicClipEditorialMetadataGenerator(),
            context,
            attempt: 0);

        TestAssert.Equal(
            $"Three pale rings; marker R-7; doorway open {context.GameContext.GameHashtag}",
            draft.Title,
            "Opaque qualified wording must stay atomic instead of being parsed into fabricated grammar.");
        TestAssert.True(
            draft.Description.StartsWith(
                observation,
                StringComparison.Ordinal),
            "The description must retain the qualified wording exactly.");
    }

    private static async Task SingleVisualObservationDoesNotFakeVariation()
    {
        const string observation =
            "A narrow bridge remains visible between two platforms.";
        ClipEditorialContext context = CreateContext(
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual-only",
                    ClipEditorialEvidenceKind.VisualObservation,
                    observation),
            ]);
        var generator = new HeuristicClipEditorialMetadataGenerator();

        ClipEditorialMetadataDraft first = await GenerateAsync(
            generator,
            context,
            attempt: 0);
        ClipEditorialMetadataDraft rerolled = await GenerateAsync(
            generator,
            context,
            attempt: 1);

        TestAssert.Equal(
            first.Title,
            rerolled.Title,
            "One visual observation cannot safely support a fabricated structural alternative.");
        TestAssert.Equal(
            first.Description,
            rerolled.Description,
            "A reroll must not rewrite the only qualified observation.");
    }

    private static async Task RerollsNeverCycleToAnAcceptedTitle()
    {
        ClipEditorialContext context = CreateContext(
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual-01",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Opened the brass hatch beside the green panel."),
                new ClipEditorialEvidenceReference(
                    "visual-02",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Found a ladder behind the open hatch."),
                new ClipEditorialEvidenceReference(
                    "visual-03",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Climbed the ladder onto the platform."),
                new ClipEditorialEvidenceReference(
                    "visual-04",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Opened a brass hatch beside a green panel."),
            ]);
        var generator = new HeuristicClipEditorialMetadataGenerator();
        ClipEditorialMetadataDraft current = await generator.GenerateAsync(
            new ClipEditorialMetadataRequest(
                context,
                ClipEditorialProfile.Default,
                attempt: 0),
            CancellationToken.None);
        var titles = new List<string> { current.Title };

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            current = await generator.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    context,
                    ClipEditorialProfile.Default,
                    attempt,
                    priorAcceptedTitleExclusions:
                        current.CreatePriorTitleExclusions(context)),
                CancellationToken.None);
            titles.Add(current.Title);
        }

        TestAssert.Equal(
            3,
            titles.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "The original and first two rerolls must consume three separate grounded observations.");
        TestAssert.True(
            titles[1].StartsWith(
                "Found a ladder",
                StringComparison.Ordinal),
            "A reroll must skip the cosmetic article-only hatch rewrite and select a materially distinct grounded observation.");
        TestAssert.Throws<ClipEditorialMetadataVariationUnavailableException>(
            () => generator.GenerateAsync(
                    new ClipEditorialMetadataRequest(
                        context,
                        ClipEditorialProfile.Default,
                        attempt: 3,
                        priorAcceptedTitleExclusions:
                            current.CreatePriorTitleExclusions(context)),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "A third reroll must report bounded exhaustion instead of cycling A→B→A.");
    }

    private static async Task CanonicalHashtagAndBroadTagsStayGrounded()
    {
        ClipEditorialContext context = CreateContext(
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual-only",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Opened the #NeutralGame gate beside #Checkpoint."),
            ],
            gameplayRegion: NormalizedRectangle.FullFrame);
        var profile = new ClipEditorialProfile(
            reusableDescriptionSignature: "More clips: #MyChannel",
            defaultTags: ["handpicked"]);

        var generator = new HeuristicClipEditorialMetadataGenerator();
        ClipEditorialMetadataDraft draft = await generator.GenerateAsync(
            new ClipEditorialMetadataRequest(context, profile, 0),
            CancellationToken.None);

        TestAssert.Equal(
            "Opened the NeutralGame gate beside Checkpoint #NeutralGame",
            draft.Title,
            "Generated title wording must remove stray hashes and append the exact canonical game hashtag once.");
        TestAssert.False(
            draft.Description[..draft.Description.IndexOf(
                    "More clips:",
                    StringComparison.Ordinal)]
                .Contains('#'),
            "The generated description body should not create hashtags.");
        TestAssert.True(
            draft.Description.EndsWith(
                "More clips: #MyChannel",
                StringComparison.Ordinal),
            "An explicitly authored reusable signature remains untouched.");
        foreach (string expected in new[]
                 {
                     "Neutral Game",
                     "handpicked",
                     "gaming",
                     "gameplay",
                     "playthrough",
                 })
        {
            TestAssert.True(
                draft.Tags.Contains(expected, StringComparer.OrdinalIgnoreCase),
                $"Expected grounded broad tag '{expected}'.");
        }
        TestAssert.False(
            draft.Tags.Any(static tag => tag.Equals(
                "PC",
                StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("shorts", StringComparison.OrdinalIgnoreCase)),
            "Platform and Shorts tags require retained platform eligibility and must not be inferred from a short Windows-side clip.");
        TestAssert.Equal(
            string.Join(", ", draft.Tags),
            draft.TagsText,
            "The UI and Publish boundary serialize tags canonically with commas.");
        string[] mergedProviderTags = ClipEditorialGeneratedTags.Build(
            context,
            explicitDefaultTags: [],
            additionalGroundedTags: ["NeutralGame", "gate"]);
        TestAssert.False(
            mergedProviderTags.Contains(
                "NeutralGame",
                StringComparer.OrdinalIgnoreCase),
            "The compact hashtag spelling must not duplicate the canonical game-name tag during AI result merging.");
    }

    private static Task ProfileTagParsingPreservesCompatibility()
    {
        var profile = new ClipEditorialProfile(
            defaultTags: ClipEditorialProfileTags.Parse(
                "#Call of Duty, gameplay\n#legacy #hashtags"));

        TestAssert.True(
            profile.DefaultTags.SequenceEqual(
                ["Call of Duty", "gameplay", "legacy", "hashtags"],
                StringComparer.Ordinal),
            "Comma-delimited phrases stay intact while an old all-hashtag whitespace list remains readable.");
        return Task.CompletedTask;
    }

    private static Task<ClipEditorialMetadataDraft> GenerateAsync(
        HeuristicClipEditorialMetadataGenerator generator,
        ClipEditorialContext context,
        int attempt) =>
        generator.GenerateAsync(
            new ClipEditorialMetadataRequest(
                context,
                ClipEditorialProfile.Default,
                attempt),
            CancellationToken.None);

    private static ClipEditorialContext CreateContext(
        IEnumerable<ClipEditorialEvidenceReference> evidence,
        IEnumerable<ClipEditorialTranscriptContext>? transcripts = null,
        NormalizedRectangle? gameplayRegion = null) =>
        new(
            "heuristic-metadata-candidate",
            Path.GetFullPath("NeutralGame/Vertical/source.mkv"),
            "NeutralGame",
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(24),
            TimeSpan.FromMinutes(8),
            72,
            "A deterministic candidate boundary was retained.",
            transcripts,
            evidence,
            new ClipEditorialGameContext(
                "Neutral Game",
                "#NeutralGame",
                contextNotes: null,
                ClipEditorialGameContextSource.UserConfirmed),
            gameplayRegion: gameplayRegion);
}
