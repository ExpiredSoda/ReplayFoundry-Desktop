using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Publish.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static Task BalancedSchemaAuditPreservesHistoricalContracts()
    {
        foreach (var entry in new[]
        {
            (Output: Qwen3VlGroundedMetadataGenerator.OutputSchema, Tags: true,
                Schema: "grounded-editorial-metadata-json-schema-1.9"),
            (Output: Qwen3VlGroundedMetadataGenerator.PreviousResponsibilitySplitOutputSchema, Tags: true,
                Schema: "grounded-editorial-metadata-json-schema-1.9"),
            (Output: Qwen3VlGroundedMetadataGenerator.PreviousCompactIsolatedFieldAuthoringOutputSchema, Tags: true,
                Schema: "grounded-editorial-metadata-json-schema-1.9"),
            (Output: Qwen3VlGroundedMetadataGenerator.PreviousIsolatedFieldAuthoringOutputSchema, Tags: true,
                Schema: "grounded-editorial-metadata-json-schema-1.9"),
            (Output: Qwen3VlGroundedMetadataGenerator.PreviousSchemaEnforcedBalancedCopyOutputSchema, Tags: true,
                Schema: "grounded-editorial-metadata-json-schema-1.8"),
            (Output: Qwen3VlGroundedMetadataGenerator.BaselineOutputSchema, Tags: false,
                Schema: "grounded-editorial-metadata-json-schema-1.7"),
        })
        {
            foreach (string schema in new[]
            {
                "grounded-editorial-metadata-json-schema-1.7",
                "grounded-editorial-metadata-json-schema-1.8",
                "grounded-editorial-metadata-json-schema-1.9",
            })
            {
                using JsonDocument result = JsonSerializer.SerializeToDocument(new
                {
                    structuredDecodingAudit = new
                    {
                        policyVersion = Qwen3VlEditorialStructuredDecodingPolicy.Version,
                        backendName = Qwen3VlEditorialStructuredDecodingPolicy.BackendName,
                        backendVersion = Qwen3VlEditorialStructuredDecodingPolicy.BackendVersion,
                        schemaVersion = schema,
                        schemaSha256 = new string('a', 64),
                        representation = Qwen3VlEditorialStructuredDecodingPolicy.Representation.ToString(),
                        cudaMaskBackend = Qwen3VlEditorialStructuredDecodingPolicy.CudaMaskBackend,
                        compileElapsedSeconds = 0.25,
                        generatedTokenCount = 70,
                        grammarTerminationState = "EndOfSequence",
                        strictParserAccepted = true,
                        unconstrainedFallbackUsed = false,
                        semanticRepairApplied = false,
                    },
                });
                if (schema == entry.Schema)
                {
                    Qwen3VlGroundedMetadataEvidenceParser.ValidateStructuredDecodingAudit(
                        result.RootElement, 70, entry.Output, entry.Tags);
                }
                else
                {
                    TestAssert.Throws<Qwen3VlOutputParseException>(
                        () => Qwen3VlGroundedMetadataEvidenceParser.ValidateStructuredDecodingAudit(
                            result.RootElement, 70, entry.Output, entry.Tags),
                        "Saved outputs cannot claim a different generation schema.");
                }
            }
        }
        return Task.CompletedTask;
    }

    private static Task CopyObjectiveIsTypedAndIndependentOfGuidance()
    {
        const string customGuidance = "Keep my exact wording: balance is a character name, not a policy.";
        TestAssert.Equal(ClipEditorialCopyObjective.BalancedActionAndCommentary,
            ClipEditorialProfile.Default.CopyObjective, "New session profiles default to balanced copy.");
        var profile = new ClipEditorialProfile(namingGuidance: customGuidance,
            copyObjective: ClipEditorialCopyObjective.FollowVariant);
        TestAssert.Equal(ClipEditorialCopyObjective.FollowVariant, profile.CopyObjective,
            "An explicit variant-following objective must remain available independently of prose.");
        TestAssert.Equal(customGuidance, profile.NamingGuidance,
            "Adding a typed objective must preserve custom naming guidance exactly.");
        var noGuidance = new ClipEditorialProfile(namingGuidance: null);
        TestAssert.Equal(ClipEditorialCopyObjective.BalancedActionAndCommentary, noGuidance.CopyObjective,
            "The default objective does not depend on naming guidance being present.");
        TestAssert.Equal<string?>(null, noGuidance.NamingGuidance,
            "The objective must not fill or rewrite missing naming guidance.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => new ClipEditorialProfile(copyObjective: (ClipEditorialCopyObjective)2),
            "Profiles reject undefined copy objectives.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => new CreatorVoiceSettings("Chat", customGuidance, string.Empty, [],
                (ClipEditorialCopyObjective)2),
            "Creator voice snapshots reject undefined copy objectives.");
        return Task.CompletedTask;
    }

    private static Task CopyObjectiveSurvivesSessionAndRequestClones()
    {
        const string customGuidance = "Keep these custom creator instructions unchanged.";
        var session = new ClipEditorialProfileSession();
        session.Update(new ClipEditorialProfile(namingGuidance: "Original custom instructions.",
            defaultTags: ["creator-tag"],
            voicePerspective: ClipEditorialVoicePerspective.NeutralNoSubject,
            copyObjective: ClipEditorialCopyObjective.FollowVariant));
        TestAssert.Equal(ClipEditorialCopyObjective.FollowVariant,
            session.CurrentCreatorVoice.CopyObjective, "Settings snapshots retain the current typed objective.");
        CreatorVoiceSettings settings = session.UpdateCreatorVoice(
            "Viewers", customGuidance, "Creator signature.", ["creator-tag"]);
        TestAssert.Equal(ClipEditorialCopyObjective.FollowVariant, settings.CopyObjective,
            "Saving creator voice text must retain the typed objective.");
        TestAssert.Equal(customGuidance, settings.NamingGuidance,
            "Saving creator voice text preserves the submitted custom guidance.");

        var studio = new StudioEditorialMetadataService(null, null, session);
        TestAssert.Equal(ClipEditorialCopyObjective.FollowVariant, studio.LoadProfile().CopyObjective,
            "Studio profile snapshots retain the current objective.");
        studio.SaveProfile("Viewers", customGuidance, "Creator signature.");
        TestAssert.Equal(ClipEditorialCopyObjective.FollowVariant, session.Current.CopyObjective,
            "Saving Studio profile text must retain the current objective.");
        TestAssert.Equal(ClipEditorialVoicePerspective.NeutralNoSubject, session.Current.VoicePerspective,
            "Saving text must retain the independent voice perspective.");
        TestAssert.Equal(customGuidance, session.Current.NamingGuidance,
            "Studio must not replace custom guidance with the objective's default prose.");
        var publish = new PublishEditorialMetadataService(
            new GenerationOutputSession(), new RecordingRequestMetadataGenerator(), session);
        TestAssert.Equal(ClipEditorialCopyObjective.FollowVariant, publish.LoadProfile().CopyObjective,
            "Publish profile snapshots retain the current objective.");

        var request = new ClipEditorialMetadataRequest(CreateContext(), session.Current, 0);
        ClipEditorialMetadataRequest[] clones =
        [
            request.WithAttempt(2),
            request.WithVariantIntent(ClipEditorialVariantIntent.SpecificCuriosity),
            request.WithPriorAcceptedTitleExclusions([]),
        ];
        TestAssert.True(clones.All(clone => ReferenceEquals(request.Profile, clone.Profile)),
            "Attempt, variant, and exclusion clones retain the complete immutable profile.");
        return Task.CompletedTask;
    }

    private static async Task StudioRerollPreservesTypedCopyObjective()
    {
        const string customGuidance = "Keep this exact creator-specific writing instruction.";
        (GenerationOutputAsset asset, _) = await CreateAssetAsync();
        var project = new GenerationOutputProject(
            "project-copy-objective", GenerationMode.IndividualClips,
            Path.GetFullPath("copy-objective-output"), 1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset], DateTimeOffset.UtcNow);
        var outputs = new GenerationOutputSession();
        outputs.Publish(project);
        var profile = new ClipEditorialProfileSession();
        profile.Update(new ClipEditorialProfile(
            voicePerspective: ClipEditorialVoicePerspective.NeutralNoSubject,
            copyObjective: ClipEditorialCopyObjective.FollowVariant));
        var generator = new RecordingRequestMetadataGenerator();
        var studio = new StudioEditorialMetadataService(outputs, generator, profile);
        await studio.RerollAsync(project, asset, "Viewers", customGuidance,
            string.Empty, requireAi: false, CancellationToken.None);
        ClipEditorialMetadataRequest submitted = generator.LastRequest ??
            throw new InvalidOperationException("Studio must submit the reroll request.");
        TestAssert.Equal(ClipEditorialCopyObjective.FollowVariant,
            submitted.Profile.CopyObjective,
            "Studio rerolls retain the typed objective when creating a profile from edited text.");
        TestAssert.Equal(ClipEditorialVoicePerspective.NeutralNoSubject,
            submitted.Profile.VoicePerspective,
            "Studio rerolls retain the independent voice perspective.");
        TestAssert.Equal(customGuidance, submitted.Profile.NamingGuidance,
            "Studio rerolls send the exact submitted custom guidance.");
    }

    private static async Task GroundedQwenSerializesTypedCopyObjective()
    {
        const string customGuidance = "Preserve this custom text regardless of the copy objective.";
        foreach (ClipEditorialCopyObjective objective in Enum.GetValues<ClipEditorialCopyObjective>())
        {
            using var fixture = new ModelFreeGroundedExecutorFixture();
            string? inputJson = null;
            var runner = new FailureArtifactProcessRunner(processRequest =>
            {
                inputJson = File.ReadAllText(Path.Combine(processRequest.WorkingDirectory!, "input-batch.json"));
                return new ProcessRunResult(2, string.Empty,
                    "{\"errorCode\":\"UsageOrInputError\",\"message\":\"model-free stop\"}",
                    TimeSpan.FromMilliseconds(20));
            });
            using var generator = new Qwen3VlGroundedMetadataGenerator(
                fixture.Runtime, runner, new SystemQwen3VlBatchWorkspaceFactory());
            ClipEditorialMetadataRequest original = fixture.CreateRequest();
            var request = new ClipEditorialMetadataRequest(original.Context,
                new ClipEditorialProfile(namingGuidance: customGuidance, copyObjective: objective),
                original.Attempt, original.Preference, original.SourceMedia, original.ReviewVideo,
                variantIntent: ClipEditorialVariantIntent.DirectAction);
            TestAssert.True(ReferenceEquals(request.Profile,
                request.WithReviewVideo(original.ReviewVideo!).Profile),
                "Attaching a visual review retains the immutable profile.");
            await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
                () => generator.GenerateAsync(request, CancellationToken.None),
                "The model-free process stops after capturing the request wire.");
            TestAssert.True(inputJson is not null, "The executor must write its grounded request.");
            using JsonDocument document = JsonDocument.Parse(inputJson!);
            JsonElement wireProfile = document.RootElement.GetProperty("requests")[0].GetProperty("profile");
            TestAssert.Equal(objective.ToString(), wireProfile.GetProperty("copyObjective").GetString(),
                "The request wire sends the typed objective as its exact enum name.");
            TestAssert.Equal(customGuidance, wireProfile.GetProperty("namingGuidance").GetString(),
                "Wire serialization preserves custom guidance independently of the objective.");
            TestAssert.Equal("DirectAction", wireProfile.GetProperty("variantIntent").GetString(),
                "The fixed objective does not replace the separate presentation variant.");
        }
    }

    private static Task BalanceReviewCrossesProviderParser()
    {
        // Actual run 31 returned this pair; the desktop rejected the whole result
        // before the existing retry service could inspect either finding.
        using JsonDocument actualShape = JsonDocument.Parse("""
            {"metadataReviewRequired":true,
             "metadataReviewIssues":["UnsupportedCreatorEmbodiment","BalanceNotSatisfied"]}
            """);
        var review = Qwen3VlGroundedMetadataRecoveryParser.ParseMetadataReview(
            actualShape.RootElement, reviewableAudienceCopySupported: true);
        TestAssert.True(review.Required && review.Issues.SequenceEqual(
            new[] { "UnsupportedCreatorEmbodiment", "BalanceNotSatisfied" }),
            "The actual mixed provider findings retain their source codes and order.");
        foreach (string json in new[]
        {
            """{"metadataReviewRequired":true,"metadataReviewIssues":["UnknownRule"]}""",
            """{"metadataReviewRequired":false,"metadataReviewIssues":["BalanceNotSatisfied"]}""",
            """{"metadataReviewRequired":true,"metadataReviewIssues":["BalanceNotSatisfied","BalanceNotSatisfied"]}""",
        })
        {
            using JsonDocument invalid = JsonDocument.Parse(json);
            TestAssert.Throws<Qwen3VlOutputParseException>(
                () => Qwen3VlGroundedMetadataRecoveryParser.ParseMetadataReview(
                    invalid.RootElement, reviewableAudienceCopySupported: true),
                "Recognizing balance must preserve strict unknown-code, flag and duplicate validation.");
        }
        return Task.CompletedTask;
    }

    private static Task BalanceReviewRetainsItsTypedSourceAndExplanation()
    {
        ClipEditorialMetadataQualityIssue issue =
            ClipEditorialMetadataReview.BuildIssues(["BalanceNotSatisfied"]).Single();
        TestAssert.Equal(ClipEditorialMetadataQualityIssueCode.AudienceCopyReview, issue.Code,
            "Unmet balance uses the existing audience-copy review contract.");
        TestAssert.Equal("BalanceNotSatisfied", issue.SourceRuleCode,
            "The stable provider code survives the user-facing review mapping.");
        TestAssert.True(issue.Message.Contains("commentary", StringComparison.OrdinalIgnoreCase) &&
            issue.Message.Contains("visible action", StringComparison.OrdinalIgnoreCase) &&
            issue.Message.Contains("not been marked as reviewed", StringComparison.OrdinalIgnoreCase),
            "The review explains the unmet balance without promoting automatic transcript authority.");
        return Task.CompletedTask;
    }

    private static async Task BalanceReviewAloneDoesNotRepeatProviderInference()
    {
        var ai = new BalanceReviewMetadataGenerator();
        var heuristic = new RecordingFallbackMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(heuristic, ai);
        var events = new List<ClipEditorialRetryDiagnostic>();
        ClipEditorialMetadataDraft draft;
        using (ClipEditorialRetryDiagnostics.Capture(events.Add))
        {
            draft = await service.GenerateAsync(DiagnosticRequest("balance-review"), CancellationToken.None);
        }
        TestAssert.Equal(1, ai.BatchCalls,
            "An unmet packaging preference alone must not repeat provider inference.");
        TestAssert.Equal(0, events.Count, "No automatic retry may be reported when none was submitted.");
        TestAssert.Equal(0, heuristic.Requests.Count, "The review finding must not replace AI authorship.");
        TestAssert.Equal("The Reactor Opened a New Route #ExampleGame", draft.Title,
            "The completed provider draft remains available unchanged.");
        TestAssert.True(draft.IsPublishReady && draft.Origin == ClipEditorialMetadataOrigin.AiAssisted,
            "The advisory remains a usable AI draft.");
        TestAssert.True(draft.QualityIssues.Count == 1 &&
            draft.QualityIssues.Single().SourceRuleCode == "BalanceNotSatisfied" &&
            draft.Warnings.Any(static warning => warning.Code == ClipEditorialWarningCode.MetadataReviewRequired),
            "The balance finding and its visible review warning remain on the retained draft.");
    }

    private sealed class BalanceReviewMetadataGenerator : IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } = new("Balance review fixture", "1.0");
        public bool IsAvailable => true;
        public int BatchCalls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This fixture must use the real batch retry path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = requests.Select(request => new ClipEditorialMetadataDraft(
                "The Reactor Opened a New Route #ExampleGame",
                "I completed one concrete visible action in the selected clip.",
                [request.Context.GameContext.GameName], ClipEditorialMetadataOrigin.AiAssisted,
                Identity, request.Attempt, request.Context.Evidence,
                warnings: [new ClipEditorialWarning(ClipEditorialWarningCode.MetadataReviewRequired,
                    "Review the balance between commentary and visible action.")],
                aiProvenance: CreateTestAiProvenance(Identity),
                qualityIssues: ClipEditorialMetadataReview.BuildIssues(["BalanceNotSatisfied"]))).ToArray();
            return Task.FromResult(drafts);
        }
    }
}
