using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;

namespace ReplayFoundry.PreparationTests;

internal static class HeuristicEditorialMetadataTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Editorial requests default to AI-required metadata",
            MetadataRequestsDefaultToAiRequired),
        new(
            "Heuristic gaps remain usable and broad",
            MissingVisualEvidenceStaysUsable),
        new(
            "Heuristic fallback keeps raw evidence out of audience copy",
            RawEvidenceStaysOutOfAudienceCopy),
        new(
            "Heuristic fallback never leaks internal review placeholders",
            InternalVisualBoilerplateNeverReachesAudienceCopy),
        new(
            "Editorial batch rejects internal AI workflow copy without fallback",
            InternalAiCopyRejectsBatch),
        new(
            "Internal-process boundary distinguishes workflow leaks from audience copy",
            InternalProcessBoundaryStaysNarrow),
        new(
            "Heuristic fallback offers three natural broad structures",
            ThreeBroadVariantsStayNatural),
        new(
            "Heuristic rerolls exhaust grounded titles instead of cycling",
            RerollsNeverCycleToAnAcceptedTitle),
        new(
            "Heuristic metadata keeps one canonical hashtag and grounded broad tags",
            CanonicalHashtagAndBroadTagsStayGrounded),
        new(
            "Heuristic fallback remains usable without confirmed game context",
            UnconfirmedGameStillProducesAudienceCopy),
        new(
            "Audience boundary rejects raw evidence-reporting copy",
            AudienceBoundaryRejectsRawEvidenceCopy),
        new(
            "Profile tags keep comma phrases and legacy hashtag lists",
            ProfileTagParsingPreservesCompatibility),
    ];

    private static Task MetadataRequestsDefaultToAiRequired()
    {
        var request = new ClipEditorialMetadataRequest(
            CreateContext(evidence: []),
            ClipEditorialProfile.Default,
            attempt: 0);

        TestAssert.Equal(
            ClipEditorialGenerationPreference.AiRequired,
            request.Preference,
            "Omitting a metadata preference must require AI instead of silently opting into heuristic copy.");
        return Task.CompletedTask;
    }

    private static async Task MissingVisualEvidenceStaysUsable()
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
                    attempt,
                    ClipEditorialGenerationPreference.HeuristicOnly),
                CancellationToken.None);
            titles.Add(draft.Title);

            TestAssert.False(
                ContainsInternalPlaceholder(draft.Title) ||
                ContainsInternalPlaceholder(draft.Description),
                "Fail-soft audience copy must never expose internal review instructions or unfinished placeholders.");
            TestAssert.True(
                draft.Title.EndsWith(
                    context.GameContext.GameHashtag,
                    StringComparison.Ordinal),
                "A broad fail-soft title must retain the canonical game hashtag.");
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
                "A missing observation must remain broad instead of claiming a specific unsupported event.");
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
            TestAssert.True(
                draft.IsPublishReady,
                "A structurally valid heuristic label can remain reviewable without blocking the workflow.");
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
                    warning.Code == ClipEditorialWarningCode.LimitedGrounding) &&
                draft.Warnings.Any(static warning =>
                    warning.Code ==
                        ClipEditorialWarningCode.MetadataReviewRequired),
                "The warning set must explain that the broad label has limited grounding and remains reviewable.");
        }

        TestAssert.Equal(
            3,
            titles.Distinct(StringComparer.Ordinal).Count(),
            "Deterministic rerolls may vary safe broad audience structure without inventing clip content.");
    }

    private static async Task RawEvidenceStaysOutOfAudienceCopy()
    {
        const string rawOcr = "FEDERAL BUREAU OF CONTROL";
        const string rawVisual =
            "A brass lever moves down beside the open hatch.";
        const string rawTranscript =
            "I found the secret route behind the maintenance door.";
        ClipVisualTextContext visualText = CreateVisualText(
            new VisualTextAnchor(
                "federal bureau of control",
                rawOcr,
                VisualTextAnchorAuthority.RepeatedAcrossFrames,
                [TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(66)]),
            new VisualTextAnchor(
                "prohibited items reminder",
                "Prohibited Items Reminder",
                VisualTextAnchorAuthority.RepeatedAcrossFrames,
                [TimeSpan.FromSeconds(62), TimeSpan.FromSeconds(67)]));
        ClipEditorialContext context = CreateContext(
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual-change-01",
                    ClipEditorialEvidenceKind.VisualObservation,
                    rawVisual),
            ],
            transcripts:
            [
                new ClipEditorialTranscriptContext(
                    1,
                    new AudioContentRoleAssignment(
                        AudioContentRole.CreatorSpeech,
                        AudioContentRoleSource.UserConfirmed),
                    rawTranscript,
                    ClipEditorialTranscriptAuthority.HumanReviewed),
            ],
            visualText: visualText);

        var generator = new HeuristicClipEditorialMetadataGenerator();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ClipEditorialMetadataDraft draft = await GenerateAsync(
                generator,
                context,
                attempt);
            string audienceCopy = $"{draft.Title}\n{draft.Description}";

            TestAssert.False(
                audienceCopy.Contains(
                    "On-screen text",
                    StringComparison.OrdinalIgnoreCase) ||
                audienceCopy.Contains(rawOcr, StringComparison.Ordinal) ||
                audienceCopy.Contains(
                    "Prohibited Items Reminder",
                    StringComparison.Ordinal) ||
                audienceCopy.Contains(rawVisual, StringComparison.Ordinal) ||
                audienceCopy.Contains(rawTranscript, StringComparison.Ordinal),
                "OCR, visual observations, and transcript strings are evidence inputs and must never be copied directly into heuristic audience prose.");
            TestAssert.True(
                draft.Evidence.Any(reference => reference.Id.StartsWith(
                    "visual-text-",
                    StringComparison.Ordinal)) &&
                draft.Evidence.Any(reference => reference.Id ==
                    "visual-change-01") &&
                draft.Evidence.Any(reference => reference.Id == "stream-1"),
                "Raw local signals must remain traceable in evidence even though their text is withheld from audience prose.");
        }
    }

    private static async Task InternalVisualBoilerplateNeverReachesAudienceCopy()
    {
        ClipEditorialContext context = CreateContext(
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual-placeholder",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Visual evidence point supports the observation."),
            ]);

        ClipEditorialMetadataDraft draft = await GenerateAsync(
            new HeuristicClipEditorialMetadataGenerator(),
            context,
            attempt: 0);

        TestAssert.False(
            ContainsInternalPlaceholder(draft.Title) ||
            ContainsInternalPlaceholder(draft.Description) ||
            draft.Title.Contains(
                "supports the observation",
                StringComparison.OrdinalIgnoreCase),
            "Provider scaffolding and Studio instructions must never become audience-facing title or description text.");
        TestAssert.True(
            draft.Warnings.Any(static warning => warning.Code ==
                ClipEditorialWarningCode.LimitedGrounding),
            "With no authorized specifics, fail-soft generation should retain usable broad audience copy with an explicit grounding warning.");
    }

    private static async Task InternalAiCopyRejectsBatch()
    {
        ClipEditorialContext firstContext = CreateContext(
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual-first",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Opened the sealed door beside the control panel."),
            ],
            candidateId: "candidate-first");
        ClipEditorialContext secondContext = CreateContext(
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual-second",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Crossed the narrow bridge above the lower platform."),
            ],
            candidateId: "candidate-second");
        var provider = new InternalWorkflowBatchGenerator();
        var heuristic = new CountingHeuristicMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            provider);
        ClipEditorialMetadataRequest[] requests =
        [
            new(
                firstContext,
                ClipEditorialProfile.Default,
                attempt: 0,
                ClipEditorialGenerationPreference.AiRequired),
            new(
                secondContext,
                ClipEditorialProfile.Default,
                attempt: 0,
                ClipEditorialGenerationPreference.AiRequired),
        ];

        ClipEditorialAiGenerationException exception =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateBatchAsync(
                    requests,
                    CancellationToken.None),
                "Internal AI workflow copy must reject the AI batch.");

        TestAssert.Equal(ClipEditorialAiFailureKind.UnsafeOutput,
            exception.FailureKind,
            "Internal workflow copy failure kind.");
        TestAssert.Equal("candidate-second", exception.CandidateId,
            "The unsafe row retains candidate identity.");
        TestAssert.Equal(1, provider.BatchCalls,
            "The unsafe batch runs once.");
        TestAssert.Equal(0, heuristic.Calls,
            "Unsafe AI output must never invoke heuristic metadata.");
    }

    private static Task InternalProcessBoundaryStaysNarrow()
    {
        (string Title, string Description)[] audienceCopy =
        [
            (
                "The Unfinished Ritual #NeutralGame",
                "The ritual remained unfinished when the lights went out."),
            (
                "One More Clue for the Review #NeutralGame",
                "The review needed another pass before the hidden route made sense."),
            (
                "The Studio Review Changed Everything #NeutralGame",
                "My studio review focused on the clues behind the sealed door."),
            (
                "The Working Copy Had the Answer #NeutralGame",
                "The working copy revealed the symbol I had missed."),
        ];
        foreach ((string title, string description) in audienceCopy)
        {
            TestAssert.False(
                HeuristicAudienceCopyPolicy.ContainsInternalProcessText(
                    title,
                    description),
                $"Ordinary audience language must not be classified as an internal process leak: {title} / {description}");
        }

        (string Title, string Description)[] processLeaks =
        [
            (
                "Visual Evidence Supports the Observation #ExampleGame",
                "The red hallway ended at a locked door."),
            (
                "Qualified Visual Evidence #NeutralGame",
                "An evidence point was retained for the selected interval."),
            (
                "A Deterministic Candidate #NeutralGame",
                "The deterministic score retained this model output."),
            (
                "Needs Another Pass #NeutralGame",
                "Review the bounded clip in Studio and replace this working text."),
            (
                "Grounding Validation Failed #NeutralGame",
                "The provider response entered the internal workflow."),
        ];
        foreach ((string title, string description) in processLeaks)
        {
            TestAssert.True(
                HeuristicAudienceCopyPolicy.ContainsInternalProcessText(
                    title,
                    description),
                $"Unmistakable model or Replay Foundry bookkeeping must remain blocked: {title} / {description}");
        }

        return Task.CompletedTask;
    }

    private static async Task ThreeBroadVariantsStayNatural()
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
        var packages = new HashSet<string>(StringComparer.Ordinal);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            ClipEditorialMetadataDraft draft = await GenerateAsync(
                generator,
                context,
                attempt);

            packages.Add($"{draft.Title}\n{draft.Description}");
            TestAssert.False(
                draft.Title.Contains(observation, StringComparison.Ordinal) ||
                draft.Description.Contains(observation, StringComparison.Ordinal),
                "A visual observation must inform evidence, not become raw audience prose.");
            TestAssert.Equal(
                ClipEditorialMetadataReadiness.WorkingLabel,
                draft.Readiness,
                "Broad heuristic variants remain reviewable working copy.");
        }
        TestAssert.Equal(
            3,
            packages.Count,
            "Each bounded reroll should use a distinct natural audience structure.");
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
                attempt: 0,
                preference: ClipEditorialGenerationPreference.HeuristicOnly),
            CancellationToken.None);
        var titles = new List<string> { current.Title };

        bool exhausted = false;
        for (int attempt = 1; attempt < 64; attempt++)
        {
            try
            {
                current = await generator.GenerateAsync(
                    new ClipEditorialMetadataRequest(
                        context,
                        ClipEditorialProfile.Default,
                        attempt,
                        ClipEditorialGenerationPreference.HeuristicOnly,
                        priorAcceptedTitleExclusions:
                            current.CreatePriorTitleExclusions(context)),
                    CancellationToken.None);
                TestAssert.False(
                    titles.Contains(current.Title, StringComparer.OrdinalIgnoreCase),
                    "A reroll must not cycle to a previously accepted title. " +
                    $"Actual sequence: {string.Join(" | ", titles)} | {current.Title}");
                titles.Add(current.Title);
            }
            catch (ClipEditorialMetadataVariationUnavailableException)
            {
                exhausted = true;
                break;
            }
        }

        TestAssert.True(
            exhausted && titles.Count ==
                ClipEditorialPriorTitleExclusion.MaximumRetainedTitles,
            "Rerolls must exhaust the bounded catalog without cycling accepted titles or outliving the retained-title history.");
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
            new ClipEditorialMetadataRequest(
                context,
                profile,
                0,
                ClipEditorialGenerationPreference.HeuristicOnly),
            CancellationToken.None);

        TestAssert.True(
            draft.Title.EndsWith("#NeutralGame", StringComparison.Ordinal) &&
            draft.Title.Count(character => character == '#') == 1,
            "Heuristic wording must append the exact canonical game hashtag once without copying evidence hashtags.");
        TestAssert.False(
            draft.Title.Contains("Checkpoint", StringComparison.Ordinal) ||
            draft.Description.Contains("Checkpoint", StringComparison.Ordinal),
            "Hashtags and words found in raw visual evidence must not leak into audience prose.");
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

    private static async Task UnconfirmedGameStillProducesAudienceCopy()
    {
        var unconfirmedGameContext = new ClipEditorialGameContext(
            "Recording Folder Name",
            "#RecordingFolderName",
            contextNotes: null,
            ClipEditorialGameContextSource.SourcePathHint);
        ClipEditorialContext context = CreateContext(
            evidence: [],
            gameContext: unconfirmedGameContext);

        ClipEditorialMetadataDraft draft = await GenerateAsync(
            new HeuristicClipEditorialMetadataGenerator(),
            context,
            attempt: 0);

        TestAssert.True(
            !string.IsNullOrWhiteSpace(draft.Title) &&
            !string.IsNullOrWhiteSpace(draft.Description),
            "Fail-soft generation must still return usable working copy when the game is unconfirmed.");
        TestAssert.True(
            draft.Title.EndsWith(
                ClipEditorialGameContext.UnconfirmedGameHashtag,
                StringComparison.Ordinal),
            "An unconfirmed folder hint must use the neutral gameplay hashtag rather than block generation.");
        TestAssert.False(
            draft.Title.Contains(
                "RecordingFolderName",
                StringComparison.OrdinalIgnoreCase) ||
            draft.Description.Contains(
                "Recording Folder Name",
                StringComparison.OrdinalIgnoreCase),
            "Unconfirmed path-derived game text must remain internal evidence only.");
    }

    private static Task AudienceBoundaryRejectsRawEvidenceCopy()
    {
        ClipEditorialContext context = CreateContext(
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual-action",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "The sealed door opened beside the control panel."),
            ],
            visualText: CreateVisualText(new VisualTextAnchor(
                "safe room intercom",
                "Speak with the voice on the Safe Room Intercom",
                VisualTextAnchorAuthority.RepeatedAcrossFrames,
                [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12)])));

        TestAssert.True(
            HeuristicAudienceCopyPolicy.RequiresAudienceCopyFallback(
                "Speak with the voice on the Safe Room Intercom #NeutralGame",
                "The objective remained available.",
                context),
            "A stable OCR line cannot become the complete title merely because the canonical hashtag was appended.");
        TestAssert.True(
            HeuristicAudienceCopyPolicy.RequiresAudienceCopyFallback(
                "The Intercom Became the Next Objective #NeutralGame",
                "On-screen text reads Speak with the voice on the Safe Room Intercom.",
                context),
            "Evidence-reporting description framing must be replaced before reaching Studio.");
        TestAssert.True(
            HeuristicAudienceCopyPolicy.RequiresAudienceCopyFallback(
                "The sealed door opened beside the control panel #NeutralGame",
                "The route continued beyond it.",
                context),
            "A raw visual observation cannot become the entire title without editorial shaping.");
        TestAssert.True(
            HeuristicAudienceCopyPolicy.RequiresAudienceCopyFallback(
                "A Closer Look at This Moment #NeutralGame",
                "A focused gameplay moment kept together as one complete sequence.",
                context),
            "The retired generic fallback must remain blocked at the final audience boundary.");
        TestAssert.False(
            HeuristicAudienceCopyPolicy.RequiresAudienceCopyFallback(
                "The Intercom Became the Next Objective #NeutralGame",
                "The new objective redirected the next part of the route.",
                context),
            "Natural event-led copy that uses evidence semantically must remain accepted.");
        return Task.CompletedTask;
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
                attempt,
                ClipEditorialGenerationPreference.HeuristicOnly),
            CancellationToken.None);

    private static bool ContainsInternalPlaceholder(string value) =>
        value.Contains("unfinished", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("review needed", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("working text", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("bounded clip", StringComparison.OrdinalIgnoreCase);

    private static ClipVisualTextContext CreateVisualText(
        params VisualTextAnchor[] anchors) =>
        new(
            "heuristic-metadata-candidate",
            Path.GetFullPath("NeutralGame/Vertical/source.mkv"),
            NormalizedRectangle.FullFrame,
            frames: [],
            anchors);

    private static ClipEditorialContext CreateContext(
        IEnumerable<ClipEditorialEvidenceReference> evidence,
        IEnumerable<ClipEditorialTranscriptContext>? transcripts = null,
        NormalizedRectangle? gameplayRegion = null,
        ClipVisualTextContext? visualText = null,
        string candidateId = "heuristic-metadata-candidate",
        ClipEditorialGameContext? gameContext = null) =>
        new(
            candidateId,
            Path.GetFullPath("NeutralGame/Vertical/source.mkv"),
            "NeutralGame",
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(24),
            TimeSpan.FromMinutes(8),
            72,
            "A deterministic candidate boundary was retained.",
            transcripts,
            evidence,
            gameContext ?? new ClipEditorialGameContext(
                    "Neutral Game",
                    "#NeutralGame",
                    contextNotes: null,
                    ClipEditorialGameContextSource.UserConfirmed),
            gameplayRegion: gameplayRegion,
            visualText: visualText);

    private sealed class InternalWorkflowBatchGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Test editorial provider", "1.0");

        public bool IsAvailable => true;

        public int BatchCalls { get; private set; }

        public ClipEditorialMetadataDraft? ValidDraft { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateInternalDraft(request));
        }

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            ValidDraft = new ClipEditorialMetadataDraft(
                "Opened the Sealed Door #NeutralGame",
                "The sealed door opens beside the illuminated control panel.",
                ["gaming"],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                requests[0].Attempt,
                aiProvenance: CreateAiProvenance(Identity));
            IReadOnlyList<ClipEditorialMetadataDraft> result =
            [
                ValidDraft,
                CreateInternalDraft(requests[1]),
            ];
            return Task.FromResult(result);
        }

        private ClipEditorialMetadataDraft CreateInternalDraft(
            ClipEditorialMetadataRequest request) =>
            new(
                "Unfinished — Studio review needed #NeutralGame",
                "Grounded clip details are unfinished. Review the bounded clip in Studio and replace this working text.",
                ["gaming"],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                aiProvenance: CreateAiProvenance(Identity));
    }

    private sealed class CountingHeuristicMetadataGenerator :
        IClipEditorialMetadataGenerator
    {
        private readonly HeuristicClipEditorialMetadataGenerator _inner = new();

        public ClipEditorialMetadataGeneratorIdentity Identity => _inner.Identity;

        public bool IsAvailable => true;

        public int Calls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return _inner.GenerateAsync(request, cancellationToken);
        }
    }

    private static ClipEditorialAiProvenance CreateAiProvenance(
        ClipEditorialMetadataGeneratorIdentity identity) =>
        new(
            identity.Name,
            identity.Version,
            "test-runtime-1.0",
            "replayfoundry/test-editorial-model",
            "test-revision",
            new string('a', 64),
            "test-editorial-prompt",
            "1.0",
            new string('b', 64),
            TimeSpan.Zero,
            peakAllocatedGpuBytes: null);
}
