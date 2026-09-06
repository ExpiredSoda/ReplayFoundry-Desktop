using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Features.Studio.Rendering;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReplayFoundry.PreparationTests;

internal static partial class StudioProjectPersistenceTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Studio project persistence round-trips authoritative edits and recovery", RoundTripsProject),
        new("Studio persistence keeps extended cuts and rejects substituted source bindings", SourceBindingsSurvivePersistence),
        new("Hidden Moment acceptance retains valid captions and rejects substituted bindings", HiddenMomentAcceptanceRequiresSourceBindings),
        new("Studio schema 1.1 restores transcript contexts that predate timed spans", LoadsLegacyTranscriptWithoutSpans),
        new("Studio project persistence reads 1.0 metadata without reroll history", ReadsLegacyProjectWithoutHistory),
        new("Studio schema 1.2 persists only selected attributable public context", PersistsOnlySelectedGameKnowledge),
        new("Earlier copy remains restorable after transient context is removed by real project storage", CopyHistorySurvivesPersistedContextProjection),
        new("Authored context freshness and unknown legacy provenance survive project storage", AuthoredContextFreshnessSurvivesStorage),
        new("Studio schema 1.1 loads bounded context without re-saving its cache", LoadsLegacyGameKnowledgeBoundedly),
        new("Studio hidden moments default missing provider history to required AI", MissingHiddenMomentPreferenceDefaultsToAi),
        new("Studio legacy hidden moments migrate automatic heuristics to required AI", LegacyAutomaticHeuristicPreferenceMigratesToAi),
        new("Studio schema 1.2 rejects unbounded selected public context", RejectsUnboundedSelectedGameKnowledge),
        new("Studio persistence rejects oversized documents before parsing", RejectsOversizedDocument),
        new("Studio project persistence classifies changed and missing sources", ClassifiesFreshness),
        new("Studio project persistence recovers the previous atomic save", RecoversBackup),
        new("Recent projects reopen durable Studio state after restart", ReopensRecentProject),
        new("Studio persistence debounce cannot discard the latest revision", DebounceKeepsLatest),
        new("Studio persistence flush retains a latest save already queued behind storage", FlushRetainsClaimedLatestSave),
        new("Studio persistence skips a stale save already queued behind I/O", SkipsStaleQueuedSave),
        new("Studio persistence shutdown does not deadlock the UI synchronization context", StopDoesNotDeadlockUiContext),
        new("Studio persistence exposes actionable save failures", ExposesSaveFailure),
        new("Studio render recovery preserves interrupted queue entries", PreservesInterruptedQueue),
    ];

    private static Task MissingHiddenMomentPreferenceDefaultsToAi()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project =
            fixture.CreateProjectWithHiddenMoment();
        fixture.Store.Save(project, revision: 1, recovery: null);
        string path = Path.Combine(
            fixture.Store.ResolveProjectDirectory(project.Id),
            "studio-project.json");

        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!
            .AsObject();
        JsonObject hidden = root["hiddenMoments"]![0]!.AsObject();
        hidden.Remove("editorialPreference");
        File.WriteAllText(path, root.ToJsonString());

        StudioProjectLoadResult missing = fixture.Store.Load(project.Id);
        TestAssert.Equal(
            StudioProjectLoadOutcome.Loaded,
            missing.Outcome,
            "Removing only the legacy provider field must leave the Studio document loadable.");
        TestAssert.Equal(
            ClipEditorialGenerationPreference.AiRequired,
            missing.Project!.HiddenMoments.Single().EditorialPreference,
            "A legacy or truncated hidden moment must not deserialize an omitted provider choice as heuristic-only.");

        hidden["editorialPreference"] = null;
        File.WriteAllText(path, root.ToJsonString());
        StudioProjectLoadResult explicitNull = fixture.Store.Load(project.Id);
        TestAssert.Equal(
            StudioProjectLoadOutcome.Loaded,
            explicitNull.Outcome,
            "A null legacy provider field must leave the Studio document loadable.");
        TestAssert.Equal(
            ClipEditorialGenerationPreference.AiRequired,
            explicitNull.Project!.HiddenMoments.Single().EditorialPreference,
            "A null legacy hidden-moment provider choice must also fail safe to required AI.");
        return Task.CompletedTask;
    }

    private static Task LegacyAutomaticHeuristicPreferenceMigratesToAi()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project =
            fixture.CreateProjectWithHiddenMoment();
        fixture.Store.Save(project, revision: 1, recovery: null);
        string path = Path.Combine(
            fixture.Store.ResolveProjectDirectory(project.Id),
            "studio-project.json");
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!
            .AsObject();
        JsonObject hidden = root["hiddenMoments"]![0]!.AsObject();
        hidden["editorialPreference"] = nameof(
            ClipEditorialGenerationPreference.HeuristicOnly);
        File.WriteAllText(path, root.ToJsonString());

        StudioProjectLoadResult current = fixture.Store.Load(project.Id);
        TestAssert.Equal(
            StudioProjectLoadOutcome.Loaded,
            current.Outcome,
            "A current project with an explicit local-heuristic choice must remain loadable.");
        TestAssert.Equal(
            ClipEditorialGenerationPreference.HeuristicOnly,
            current.Project!.HiddenMoments.Single().EditorialPreference,
            "Schema 1.2 must preserve a user's explicit no-AI choice.");

        foreach (string legacySchema in new[]
                 {
                     StudioProjectDocument.PreviousSchemaVersion,
                     StudioProjectDocument.LegacySchemaVersion,
                 })
        {
            root["schemaVersion"] = legacySchema;
            hidden["editorialPreference"] = nameof(
                ClipEditorialGenerationPreference.HeuristicOnly);
            File.WriteAllText(path, root.ToJsonString());

            StudioProjectLoadResult migrated = fixture.Store.Load(project.Id);
            TestAssert.Equal(
                StudioProjectLoadOutcome.Loaded,
                migrated.Outcome,
                $"Legacy project {legacySchema} must remain loadable after provider migration.");
            TestAssert.Equal(
                ClipEditorialGenerationPreference.AiRequired,
                migrated.Project!.HiddenMoments.Single().EditorialPreference,
                $"Legacy project {legacySchema} must not retain an automatically inferred heuristic preference as if the user chose it.");
        }

        return Task.CompletedTask;
    }

    private static Task RoundTripsProject()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        var recovery = new StudioProjectRecoveryState(
            project.PrimaryAsset.Id,
            [
                new StudioRenderQueueEntryDocument(
                    project.PrimaryAsset.Id,
                    StudioPersistedRenderState.Ready),
            ],
            TimeSpan.FromSeconds(17));
        fixture.Store.Save(project, revision: 1, recovery);

        StudioProjectLoadResult loaded = fixture.Store.Load(project.Id);
        TestAssert.Equal(
            StudioProjectLoadOutcome.Loaded,
            loaded.Outcome,
            "A valid project should load without analysis work.");
        TestAssert.True(loaded.Project is not null, "The project should rehydrate.");
        GenerationOutputAsset asset = loaded.Project!.PrimaryAsset;
        TestAssert.Equal(TimeSpan.FromSeconds(12), asset.SourceStart,
            "The edited source start should persist.");
        TestAssert.Equal(TimeSpan.FromSeconds(28), asset.SourceEnd,
            "The edited source end should persist.");
        TestAssert.Equal(TimeSpan.FromSeconds(10), asset.OriginalSourceStart,
            "The original candidate start should remain distinct from the edit.");
        TestAssert.Equal(StudioVideoEffectPreset.Noir, asset.Appearance.VideoEffect,
            "The selected visual effect should persist.");
        TestAssert.Equal(GenerationCaptionStylePreset.KaraokeSweep,
            asset.Captions!.RequestedStyle,
            "The caption presentation should persist.");
        StudioCaptionLook captionLook = loaded.Project.CaptionLook ??
            throw new InvalidOperationException(
                "A captioned Studio project must restore its established caption look.");
        TestAssert.Equal(
            asset.Appearance.CaptionWordLimit,
            captionLook.CaptionWordLimit,
            "The project caption phrase size must remain available to clips accepted after reopening.");
        TestAssert.Equal(
            asset.Appearance.CaptionVerticalPositionPercent,
            captionLook.CaptionVerticalPositionPercent,
            "The project caption position must remain available to clips accepted after reopening.");
        TestAssert.Equal(
            asset.Appearance.CaptionMaximumWidthPercent,
            captionLook.CaptionMaximumWidthPercent,
            "The project caption width must remain available to clips accepted after reopening.");
        TestAssert.Equal(
            asset.Appearance.CaptionFontScalePercent,
            captionLook.CaptionFontScalePercent,
            "The project caption size must remain available to clips accepted after reopening.");
        TestAssert.Equal("A grounded title", asset.EditorialMetadata!.Title,
            "Editorial metadata should persist.");
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.UserEditedDraft,
            asset.EditorialMetadata.Readiness,
            "An unreviewed user-edited draft must survive a durable Studio round trip.");
        TestAssert.Equal("An earlier grounded title #TestGame",
            asset.EditorialMetadata.PriorAcceptedTitles.Single(),
            "Bounded reroll exclusions must survive a durable Studio round trip.");
        TestAssert.Equal("UnstableReadableTextReuse",
            asset.EditorialMetadata.QualityIssues.Single().SourceRuleCode,
            "Typed provider review provenance must survive a durable Studio round trip.");
        TestAssert.Equal(StudioPersistedRenderState.Ready,
            loaded.Document!.Recovery!.RenderQueue[0].State,
            "The render queue should remain separate recoverable state.");
        return Task.CompletedTask;
    }

    private static Task SourceBindingsSurvivePersistence()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project =
            fixture.CreateProjectWithHiddenMoment(includeBindings: true);
        GenerationOutputAsset original = project.PrimaryAsset;
        GenerationOutputAsset extended = original.WithStudioEdits(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(35),
            original.Appearance);
        project = project.ReplaceAsset(extended);
        fixture.Store.Save(project, revision: 1);
        string path = Path.Combine(
            fixture.Store.ResolveProjectDirectory(project.Id),
            "studio-project.json");
        string validJson = File.ReadAllText(path);

        StudioProjectLoadResult valid = fixture.Store.Load(project.Id);
        TestAssert.Equal(
            StudioProjectLoadOutcome.Loaded,
            valid.Outcome,
            "A Studio cut may extend beyond its retained caption window when every retained object still belongs to the same source.");
        GenerationOutputAsset restored = valid.Project!.PrimaryAsset;
        TestAssert.Equal(
            TimeSpan.FromSeconds(5),
            restored.SourceStart,
            "The extended start must survive source-binding validation.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(35),
            restored.SourceEnd,
            "The extended end must survive source-binding validation.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(10),
            restored.Captions!.SourceWindowStart,
            "Source binding must not rewrite the independent retained caption window.");

        void ExpectCorrupt(
            Action<JsonObject> mutate,
            string message)
        {
            JsonObject document = JsonNode.Parse(validJson)!.AsObject();
            mutate(document);
            File.WriteAllText(path, document.ToJsonString());
            StudioProjectLoadResult loaded = fixture.Store.Load(project.Id);
            TestAssert.Equal(
                StudioProjectLoadOutcome.Corrupt,
                loaded.Outcome,
                message);
        }

        ExpectCorrupt(
            document => document["assets"]![0]!["captions"]!
                ["candidateId"] = "another-candidate",
            "A restored caption track cannot move to another candidate.");
        ExpectCorrupt(
            document => document["assets"]![0]!["captions"]!
                ["sourceSelection"]!["sourceFullPath"] =
                    Path.Combine(fixture.Root, "another-source.mkv"),
            "A restored caption selection cannot move to another source path.");
        ExpectCorrupt(
            document => document["assets"]![0]!["captions"]!
                ["sourceSelection"]!["absoluteAudioStreamIndex"] = 999,
            "A restored caption selection must identify an inspected audio stream.");
        ExpectCorrupt(
            document => document["assets"]![0]!["captions"]!
                ["sourceDuration"] = TimeSpan.FromMinutes(3).ToString("c"),
            "A restored caption track must retain the source media duration.");
        ExpectCorrupt(
            document => document["hiddenMoments"]![0]!
                ["captionSourceSelection"]!
                ["absoluteAudioStreamIndex"] = 999,
            "A restored Hidden Moment caption request must identify an inspected audio stream.");
        ExpectCorrupt(
            document => document["hiddenMoments"]![0]!["sourceStart"] =
                TimeSpan.FromSeconds(41).ToString("c"),
            "A restored Hidden Moment editorial context must retain its exact hidden range.");
        return Task.CompletedTask;
    }

    private static Task HiddenMomentAcceptanceRequiresSourceBindings()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project =
            fixture.CreateProjectWithHiddenMoment();
        GenerationHiddenMoment hidden = project.HiddenMoments.Single();
        int audioStreamIndex = hidden.SourceMedia.AudioStreams[0].Index;
        var validSelection = new GenerationCaptionSourceSelection(
            hidden.SourceFullPath,
            audioStreamIndex,
            CaptionAudioContentRole.CreatorCommentary);

        GenerationCandidateCaptionTrack Track(
            string candidateId,
            GenerationCaptionSourceSelection selection,
            TimeSpan sourceDuration)
        {
            const string neighborhoodId = "caption-hidden-persistence-1";
            var segment = new AudioTranscriptionSegment(
                "hidden-segment-1",
                neighborhoodId,
                "I found another route",
                TimeSpan.FromSeconds(6),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(41),
                TimeSpan.FromSeconds(43));
            return GenerationCandidateCaptionTrack.RestoreStudioHandoff(
                candidateId,
                neighborhoodId,
                selection,
                GenerationCaptionStylePreset.KaraokeSweep,
                TimeSpan.FromSeconds(35),
                TimeSpan.FromSeconds(25),
                sourceDuration,
                [segment],
                isUserEdited: false,
                GenerationCaptionSuppressionReason.None);
        }

        GenerationCandidateCaptionTrack validTrack = Track(
            hidden.Id,
            validSelection,
            hidden.SourceMedia.Duration);
        GenerationOutputProject accepted = project.AcceptHiddenMoment(
            hidden.Id,
            validTrack);
        GenerationOutputAsset acceptedAsset = accepted.Assets[^1];
        TestAssert.Equal(
            hidden.Id,
            acceptedAsset.Captions!.CandidateId,
            "A source-bound caption track should survive Hidden Moment acceptance.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(35),
            acceptedAsset.Captions.SourceWindowStart,
            "Acceptance must preserve a retained caption window broader than the accepted cut.");

        TestAssert.Throws<ArgumentException>(
            () => project.AcceptHiddenMoment(
                hidden.Id,
                Track(
                    "another-candidate",
                    validSelection,
                    hidden.SourceMedia.Duration)),
            "Hidden Moment acceptance must reject captions from another candidate.");
        TestAssert.Throws<ArgumentException>(
            () => project.AcceptHiddenMoment(
                hidden.Id,
                Track(
                    hidden.Id,
                    new GenerationCaptionSourceSelection(
                        Path.Combine(fixture.Root, "another-source.mkv"),
                        audioStreamIndex,
                        CaptionAudioContentRole.CreatorCommentary),
                    hidden.SourceMedia.Duration)),
            "Hidden Moment acceptance must reject captions from another source path.");
        TestAssert.Throws<ArgumentException>(
            () => project.AcceptHiddenMoment(
                hidden.Id,
                Track(
                    hidden.Id,
                    new GenerationCaptionSourceSelection(
                        hidden.SourceFullPath,
                        999,
                        CaptionAudioContentRole.CreatorCommentary),
                    hidden.SourceMedia.Duration)),
            "Hidden Moment acceptance must reject an uninspected audio stream.");
        TestAssert.Throws<ArgumentException>(
            () => project.AcceptHiddenMoment(
                hidden.Id,
                Track(
                    hidden.Id,
                    validSelection,
                    hidden.SourceMedia.Duration + TimeSpan.FromSeconds(1))),
            "Hidden Moment acceptance must reject captions carrying another source duration.");

        var foreignContext = new ClipEditorialContext(
            hidden.Id,
            Path.Combine(fixture.Root, "another-source.mkv"),
            "Another source",
            hidden.SourceStart,
            hidden.SourceEnd,
            hidden.SourceMedia.Duration,
            hidden.FinalScore,
            hidden.Explanation);
        TestAssert.Throws<ArgumentException>(
            () => project.AcceptHiddenMoment(
                hidden.Id,
                editorialContext: foreignContext,
                editorialMetadata: project.PrimaryAsset.EditorialMetadata),
            "Hidden Moment acceptance must reject editorial context from another source.");
        var shiftedContext = new ClipEditorialContext(
            hidden.Id,
            hidden.SourceFullPath,
            "Shifted range",
            hidden.SourceStart + TimeSpan.FromSeconds(1),
            hidden.SourceEnd,
            hidden.SourceMedia.Duration,
            hidden.FinalScore,
            hidden.Explanation);
        TestAssert.Throws<ArgumentException>(
            () => project.AcceptHiddenMoment(
                hidden.Id,
                editorialContext: shiftedContext,
                editorialMetadata: project.PrimaryAsset.EditorialMetadata),
            "Hidden Moment acceptance must reject editorial context from another range.");
        return Task.CompletedTask;
    }

    private static Task ReadsLegacyProjectWithoutHistory()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        fixture.Store.Save(project, revision: 1);
        string path = Path.Combine(
            fixture.Store.ResolveProjectDirectory(project.Id),
            "studio-project.json");
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["schemaVersion"] = StudioProjectDocument.PreviousSchemaVersion;
        foreach (JsonNode? asset in root["assets"]!.AsArray())
        {
            asset!["editorialMetadata"]!.AsObject().Remove(
                "priorAcceptedTitles");
        }
        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        StudioProjectLoadResult loaded = fixture.Store.Load(project.Id);

        TestAssert.Equal(StudioProjectLoadOutcome.Loaded, loaded.Outcome,
            "A valid 1.0 Studio project must remain readable after the additive history field.");
        TestAssert.Equal(0,
            loaded.Project!.PrimaryAsset.EditorialMetadata!
                .PriorAcceptedTitles.Count,
            "Legacy projects must not invent prior accepted titles.");
        return Task.CompletedTask;
    }

    private static Task LoadsLegacyTranscriptWithoutSpans()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        fixture.Store.Save(project, revision: 1);
        string path = Path.Combine(
            fixture.Store.ResolveProjectDirectory(project.Id),
            "studio-project.json");
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["schemaVersion"] = StudioProjectDocument.LegacySchemaVersion;
        foreach (JsonNode? asset in root["assets"]!.AsArray())
        {
            foreach (JsonNode? transcript in asset!["editorialContext"]![
                         "transcripts"]!.AsArray())
            {
                transcript!.AsObject().Remove("spans");
            }
        }
        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        StudioProjectLoadResult loaded = fixture.Store.Load(project.Id);

        TestAssert.Equal(
            StudioProjectLoadOutcome.Loaded,
            loaded.Outcome,
            "A saved Studio draft from before timed transcript spans must remain openable.");
        TestAssert.Equal(
            0,
            loaded.Project!.PrimaryAsset.EditorialContext!.Transcripts.Single()
                .Spans.Count,
            "A legacy transcript without timed spans must restore as bounded untimed context.");
        return Task.CompletedTask;
    }

    private static Task CopyHistorySurvivesPersistedContextProjection()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProjectWithGameKnowledge();
        GenerationOutputAsset asset = project.PrimaryAsset;
        ClipEditorialContext context = asset.EditorialContext!;
        ClipVisualTextContext retainedText = context.VisualText!;
        var frame = new VideoPreviewFrame(
            asset.SourceFullPath,
            asset.SourceDuration,
            asset.SourceMedia.PrimaryVideoStream.Index,
            TimeSpan.FromSeconds(12),
            decodedTimestamp: null,
            1280,
            720,
            CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop,
            [1],
            new VideoPreviewFrameManifest("fake", "1", "ffmpeg", "1",
                Path.GetFullPath("ffmpeg.exe"), DateTimeOffset.UnixEpoch, TimeSpan.Zero));
        var observation = new VisualTextFrameObservation(
            new VisualTextFrameRequest(frame),
            new VisualTextProviderIdentity("fake", "1", "CPU", "1", "en-US"),
            [new VisualTextLine("Hidden Route",
            [
                new VisualTextWord("Hidden", new VisualTextBoundingBox(.1, .1, .1, .1)),
                new VisualTextWord("Route", new VisualTextBoundingBox(.2, .1, .1, .1)),
            ])],
            TimeSpan.Zero);
        context = context.WithVisualText(new ClipVisualTextContext(
            context.CandidateId, context.SourceFullPath, retainedText.ContentRegion,
            [observation], retainedText.Anchors, retainedText.Warnings));
        ClipEditorialMetadataDraft original = asset.EditorialMetadata!;
        string durableRevision = StudioEditorialContextRevision.CreateDurable(context);
        var draft = original.WithUserEdits("An alternate grounded title", original.Description, original.Tags)
            .RememberPreviousCopy(original, durableRevision);
        project = project.ReplaceAsset(asset.WithCurrentCutEditorialMetadata(context, draft));

        fixture.Store.Save(project, revision: 1);
        StudioProjectLoadResult loaded = fixture.Store.Load(project.Id);
        TestAssert.Equal(StudioProjectLoadOutcome.Loaded, loaded.Outcome,
            "A project with copy history and source-backed editorial evidence must reopen normally.");
        GenerationOutputProject restoredProject = loaded.Project!;
        GenerationOutputAsset restoredAsset = restoredProject.PrimaryAsset;
        ClipEditorialContext restored = restoredAsset.CreateCurrentCutEditorialContext();
        TestAssert.Equal(1, context.VisualText!.Frames.Count,
            "The original context must contain transient raw OCR observations.");
        TestAssert.Equal(0, restored.VisualText!.Frames.Count,
            "Raw OCR images must remain outside the durable Studio document.");
        TestAssert.Equal(10, context.GameKnowledge!.Snapshot!.Passages.Count,
            "The original context must contain unused reusable knowledge passages.");
        TestAssert.Equal(4, restored.GameKnowledge!.Snapshot!.Passages.Count,
            "Only selected attributable knowledge survives Studio storage.");
        TestAssert.Equal(durableRevision, StudioEditorialContextRevision.CreateDurable(restored),
            "Copy identity must use the same retained transcript, game, evidence, crop, OCR anchors, and grounded claims after reopening.");
        TestAssert.False(StudioEditorialContextRevision.Create(context).Equals(
            StudioEditorialContextRevision.Create(restored), StringComparison.Ordinal),
            "The pending-provider guard must still distinguish discarded raw OCR and full live knowledge snapshots.");
        TestAssert.Equal(durableRevision, restoredAsset.EditorialMetadata!.CopyVersions.Single().ContextFingerprint,
            "The stored copy version must keep its exact durable context identity.");
        var session = new GenerationOutputSession();
        session.Publish(restoredProject);
        using var editor = new StudioEditorialMetadataViewModel(session, generator: null, new ClipEditorialProfileSession());
        editor.Bind(restoredProject, restoredAsset);
        TestAssert.True(editor.RestoreCopyCommand.CanExecute(null),
            "Reloading an unchanged cut must preserve the ability to restore its earlier wording.");
        editor.RestoreCopyCommand.Execute(null);
        TestAssert.Equal(original.Title, editor.Title,
            "Restoring the reopened version must place its original wording in the editable draft.");
        return Task.CompletedTask;
    }

    private static Task PersistsOnlySelectedGameKnowledge()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProjectWithGameKnowledge();
        fixture.Store.Save(project, revision: 1);
        string path = Path.Combine(
            fixture.Store.ResolveProjectDirectory(project.Id),
            "studio-project.json");
        string json = File.ReadAllText(path);
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonObject context = root["assets"]![0]!["editorialContext"]!
            .AsObject();

        TestAssert.Equal(
            StudioProjectDocument.CurrentSchemaVersion,
            root["schemaVersion"]!.GetValue<string>(),
            "Current Studio schema.");
        TestAssert.False(
            context.ContainsKey("gameKnowledge"),
            "Schema 1.2 must never serialize the former whole ClipGameKnowledgeContext graph.");
        JsonObject selected = context["selectedGameKnowledge"]!.AsObject();
        TestAssert.Equal(
            4,
            selected["passages"]!.AsArray().Count,
            "Only passages selected for the clip receipt are persisted.");
        TestAssert.True(
            json.Contains("revision 777", StringComparison.Ordinal) &&
            json.Contains("CC-BY-SA-4.0", StringComparison.Ordinal) &&
            !json.Contains("Unselected route segment 9", StringComparison.Ordinal),
            "Selected excerpts retain revision, attribution, and license without copying unselected cache passages.");

        StudioProjectLoadResult loaded = fixture.Store.Load(project.Id);
        TestAssert.Equal(
            4,
            loaded.Project!.PrimaryAsset.EditorialContext!.GameKnowledge!
                .Snapshot!.Passages.Count,
            "The restored Studio receipt remains bounded to its four selected excerpts.");
        return Task.CompletedTask;
    }

    private static Task LoadsLegacyGameKnowledgeBoundedly()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProjectWithGameKnowledge();
        fixture.Store.Save(project, revision: 1);
        string path = Path.Combine(
            fixture.Store.ResolveProjectDirectory(project.Id),
            "studio-project.json");
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["schemaVersion"] = StudioProjectDocument.LegacySchemaVersion;
        JsonObject context = root["assets"]![0]!["editorialContext"]!
            .AsObject();
        context.Remove("selectedGameKnowledge");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
        options.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
        context["gameKnowledge"] = JsonSerializer.SerializeToNode(
            project.PrimaryAsset.EditorialContext!.GameKnowledge,
            options);
        File.WriteAllText(path, root.ToJsonString(options));

        StudioProjectLoadResult legacy = fixture.Store.Load(project.Id);
        TestAssert.Equal(
            StudioProjectLoadOutcome.Loaded,
            legacy.Outcome,
            "A valid schema 1.1 project remains readable.");
        TestAssert.Equal(
            4,
            legacy.Project!.PrimaryAsset.EditorialContext!.GameKnowledge!
                .Snapshot!.Passages.Count,
            "Legacy full-cache context is bounded to the selected clip passages during load.");

        fixture.Store.Save(legacy.Project, revision: 2);
        string resaved = File.ReadAllText(path);
        TestAssert.True(
            resaved.Contains(
                StudioProjectDocument.CurrentSchemaVersion,
                StringComparison.Ordinal) &&
            !resaved.Contains("Unselected route segment 9", StringComparison.Ordinal),
            "A legacy load is re-saved only as the bounded current receipt, never as its former full cache.");
        TestAssert.False(
            JsonNode.Parse(resaved)!["assets"]![0]!["editorialContext"]!
                .AsObject().ContainsKey("gameKnowledge"),
            "Re-saving legacy context removes the former whole-cache field.");
        return Task.CompletedTask;
    }

    private static Task RejectsUnboundedSelectedGameKnowledge()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProjectWithGameKnowledge();
        fixture.Store.Save(project, revision: 1);
        string path = Path.Combine(
            fixture.Store.ResolveProjectDirectory(project.Id),
            "studio-project.json");
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonArray terms = root["assets"]![0]!["editorialContext"]!
            ["selectedGameKnowledge"]!["matches"]![0]!["matchedTerms"]!
            .AsArray();
        terms.Add(new string('x', 161));
        File.WriteAllText(path, root.ToJsonString());

        StudioProjectLoadResult loaded = fixture.Store.Load(project.Id);

        TestAssert.Equal(
            StudioProjectLoadOutcome.Corrupt,
            loaded.Outcome,
            "Current selected-context strings must remain bounded before domain restoration.");
        return Task.CompletedTask;
    }

    private static Task RejectsOversizedDocument()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        fixture.Store.Save(project, revision: 1);
        string path = Path.Combine(
            fixture.Store.ResolveProjectDirectory(project.Id),
            "studio-project.json");
        using (var stream = new FileStream(
                   path,
                   FileMode.Open,
                   FileAccess.Write,
                   FileShare.None))
        {
            stream.SetLength(64L * 1024 * 1024 + 1);
        }

        StudioProjectLoadResult loaded = fixture.Store.Load(project.Id);

        TestAssert.Equal(
            StudioProjectLoadOutcome.Corrupt,
            loaded.Outcome,
            "Oversized project documents must be rejected before JSON allocation.");
        return Task.CompletedTask;
    }

    private static Task ClassifiesFreshness()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        fixture.Store.Save(project, revision: 1);

        File.AppendAllText(fixture.SourcePath, "changed");
        StudioProjectLoadResult changed = fixture.Store.Load(project.Id);
        TestAssert.Equal(StudioProjectLoadOutcome.ChangedSource, changed.Outcome,
            "Length or UTC write-time changes must prevent an ordinary reopen.");
        TestAssert.True(changed.HasRecoverableProject,
            "Changed sources must not discard the user's Studio edits.");

        File.Delete(fixture.SourcePath);
        StudioProjectLoadResult missing = fixture.Store.Load(project.Id);
        TestAssert.Equal(StudioProjectLoadOutcome.MissingSource, missing.Outcome,
            "Missing sources must receive a typed recovery outcome.");
        TestAssert.True(missing.HasRecoverableProject,
            "Missing sources must preserve the durable project.");
        return Task.CompletedTask;
    }

    private static Task RecoversBackup()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject first = fixture.CreateProject();
        fixture.Store.Save(first, revision: 1);
        GenerationOutputAsset editedAsset = first.PrimaryAsset.WithStudioEdits(
            TimeSpan.FromSeconds(14),
            TimeSpan.FromSeconds(27),
            first.PrimaryAsset.Appearance);
        GenerationOutputProject edited = first.ReplaceAsset(editedAsset);
        fixture.Store.Save(edited, revision: 2);

        string primary = Path.Combine(
            fixture.Store.ResolveProjectDirectory(first.Id),
            "studio-project.json");
        File.WriteAllText(primary, "{not-json");
        StudioProjectLoadResult recovered = fixture.Store.Load(first.Id);
        TestAssert.Equal(
            StudioProjectLoadOutcome.RecoveredPreviousSave,
            recovered.Outcome,
            "A corrupt primary should recover the last atomically retained save.");
        TestAssert.Equal(TimeSpan.FromSeconds(12),
            recovered.Project!.PrimaryAsset.SourceStart,
            "Recovery should use the previous complete revision, never a partial replacement.");
        TestAssert.Equal("{not-json", File.ReadAllText(primary),
            "Recovery must preserve the corrupt primary for diagnostics.");
        return Task.CompletedTask;
    }

    private static Task ReopensRecentProject()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        fixture.Store.Save(project, revision: 1);
        string recentPath = Path.Combine(fixture.Root, "recent.json");

        var firstSession = new GenerationOutputSession();
        using (var firstCatalog = new ReplayFoundry.Desktop.Features.Generate.RecentProjects.RecentGenerationProjectCatalog(
                   firstSession,
                   new ReplayFoundry.Desktop.Features.Generate.RecentProjects.JsonRecentGenerationProjectStore(recentPath),
                   fixture.Store))
        {
            firstSession.Publish(project);
        }

        var restartedSession = new GenerationOutputSession();
        using var restarted = new ReplayFoundry.Desktop.Features.Generate.RecentProjects.RecentGenerationProjectCatalog(
            restartedSession,
            new ReplayFoundry.Desktop.Features.Generate.RecentProjects.JsonRecentGenerationProjectStore(recentPath),
            fixture.Store);
        TestAssert.True(
            restarted.TryGetStudioProject(project.Id, out GenerationOutputProject? restored),
            "A new process-level catalog should resolve the durable project.");
        TestAssert.Equal(project.CandidateSetFingerprint,
            restored!.CandidateSetFingerprint,
            "Reopen should use retained Studio state without reconstructing Generate analysis.");
        fixture.Store.Delete(project.Id);
        using var afterDelete = new ReplayFoundry.Desktop.Features.Generate.RecentProjects.RecentGenerationProjectCatalog(
            new GenerationOutputSession(),
            new ReplayFoundry.Desktop.Features.Generate.RecentProjects.JsonRecentGenerationProjectStore(recentPath),
            fixture.Store);
        TestAssert.False(
            afterDelete.Projects.Single().IsStudioReady,
            "A persisted historical ready flag must not outlive its durable project document.");
        return Task.CompletedTask;
    }

    private static async Task DebounceKeepsLatest()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject first = fixture.CreateProject();
        GenerationOutputAsset latestAsset = first.PrimaryAsset.WithStudioEdits(
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(26),
            first.PrimaryAsset.Appearance);
        GenerationOutputProject latest = first.ReplaceAsset(latestAsset);
        var session = new GenerationOutputSession();
        var store = new RecordingProjectStore();
        using var coordinator = new StudioProjectPersistenceCoordinator(
            session,
            store,
            TimeSpan.FromMilliseconds(30));

        coordinator.ScheduleSave(first);
        await Task.Delay(20);
        coordinator.ScheduleSave(latest);
        await coordinator.FlushAsync();

        TestAssert.True(store.Saved.Count > 0,
            "The newest pending save should be flushed.");
        TestAssert.Equal(TimeSpan.FromSeconds(15),
            store.Saved[^1].Project.PrimaryAsset.SourceStart,
            "An obsolete debounce generation must never consume and drop the newer project.");
        TestAssert.True(store.Saved.Select(static value => value.Revision)
                .SequenceEqual(store.Saved.Select(static value => value.Revision).Order()),
            "Persisted revisions should remain monotonic.");
    }

    private static async Task SkipsStaleQueuedSave()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject first = fixture.CreateProject();
        GenerationOutputProject latest = first.ReplaceAsset(
            first.PrimaryAsset.WithStudioEdits(
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(26),
                first.PrimaryAsset.Appearance));
        GenerationOutputProject superseded = first.ReplaceAsset(
            first.PrimaryAsset.WithStudioEdits(
                TimeSpan.FromSeconds(14),
                TimeSpan.FromSeconds(26),
                first.PrimaryAsset.Appearance));
        var store = new BlockingLoadProjectStore();
        using var coordinator = new StudioProjectPersistenceCoordinator(
            new GenerationOutputSession(),
            store,
            TimeSpan.Zero);

        Task<StudioProjectRecoveryState?> blockingRead =
            coordinator.GetRecoveryAsync(first.Id);
        TestAssert.True(store.LoadStarted.Wait(TimeSpan.FromSeconds(2)),
            "The test read should own the serialized persistence writer.");
        coordinator.ScheduleSave(first);
        await Task.Delay(50);
        coordinator.ScheduleSave(superseded);
        await Task.Delay(50);
        coordinator.ScheduleSave(latest);
        Task stop = coordinator.StopAsync();
        store.AllowLoad.Set();
        await Task.WhenAll(blockingRead, stop);

        TestAssert.Equal(1, store.Saved.Count,
            "A stale debounce already queued behind storage I/O must be discarded at commit time.");
        TestAssert.Equal(TimeSpan.FromSeconds(15),
            store.Saved[0].Project.PrimaryAsset.SourceStart,
            "Only the newest project state may reach durable storage.");
        await Task.Delay(50);
        TestAssert.Equal(1, store.Saved.Count,
            "StopAsync must await every superseded save before persistence resources are released.");
    }

    private static async Task FlushRetainsClaimedLatestSave()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject first = fixture.CreateProject();
        GenerationOutputProject latest = first.ReplaceAsset(
            first.PrimaryAsset.WithStudioEdits(
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(26),
                first.PrimaryAsset.Appearance));
        var store = new BlockingFirstSaveProjectStore();
        using var coordinator = new StudioProjectPersistenceCoordinator(
            new GenerationOutputSession(),
            store,
            TimeSpan.Zero);

        coordinator.ScheduleSave(first);
        TestAssert.True(store.FirstSaveStarted.Wait(TimeSpan.FromSeconds(2)),
            "The first save should own the serialized writer.");
        coordinator.ScheduleSave(latest);
        await Task.Delay(50);
        Task flush = coordinator.FlushAsync();
        store.AllowFirstSave.Set();
        await flush;

        TestAssert.Equal(2, store.Saved.Count,
            "Flush must drain a latest project already claimed by its debounce worker.");
        TestAssert.Equal(TimeSpan.FromSeconds(15),
            store.Saved[^1].Project.PrimaryAsset.SourceStart,
            "The claimed latest state must commit after the older in-flight write.");
    }

    private static async Task StopDoesNotDeadlockUiContext()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject first = fixture.CreateProject();
        GenerationOutputProject latest = first.ReplaceAsset(
            first.PrimaryAsset.WithStudioEdits(
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(26),
                first.PrimaryAsset.Appearance));
        var session = new GenerationOutputSession();
        var store = new RecordingProjectStore();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new NonPumpingSingleThreadSynchronizationContext());
            try
            {
                var coordinator = new StudioProjectPersistenceCoordinator(
                    session,
                    store,
                    TimeSpan.FromSeconds(30));
                coordinator.ScheduleSave(first);
                coordinator.ScheduleSave(latest);
                coordinator.StopAsync().GetAwaiter().GetResult();
                coordinator.Dispose();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Studio persistence UI-context regression",
        };
        thread.Start();

        Task completed = await Task.WhenAny(
            completion.Task,
            Task.Delay(TimeSpan.FromSeconds(5)));
        TestAssert.True(ReferenceEquals(completed, completion.Task),
            "Studio shutdown must not wait for a continuation posted back to the blocked UI thread.");
        await completion.Task;
        TestAssert.Equal(1, store.Saved.Count,
            "Shutdown should flush the newest pending project exactly once.");
        TestAssert.Equal(TimeSpan.FromSeconds(15),
            store.Saved[0].Project.PrimaryAsset.SourceStart,
            "UI-thread shutdown must retain the latest scheduled Studio state.");
    }

    private static async Task ExposesSaveFailure()
    {
        using var fixture = new PersistenceFixture();
        var coordinator = new StudioProjectPersistenceCoordinator(
            new GenerationOutputSession(),
            new FailingProjectStore(),
            TimeSpan.Zero);
        int stateChanges = 0;
        coordinator.PersistenceStateChanged += (_, _) => stateChanges++;

        coordinator.ScheduleSave(fixture.CreateProject());
        await coordinator.FlushAsync();

        TestAssert.True(
            coordinator.LastError?.Contains(
                "diagnostic save failure",
                StringComparison.Ordinal) == true,
            "A swallowed project-store failure must remain visible to Studio and support diagnostics.");
        TestAssert.Equal(1, stateChanges,
            "Studio should receive one state change when a save first fails.");
        coordinator.Dispose();
    }

    private static Task PreservesInterruptedQueue()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        using var render = new StudioFinalRenderViewModel(
            outputEditor: null,
            renderingService: null,
            applyPendingEdit: static () => true,
            setHostBusy: static _ => { });
        render.Bind(project);
        render.RestoreRecoveryState(new StudioProjectRecoveryState(
            project.PrimaryAsset.Id,
            [
                new StudioRenderQueueEntryDocument(
                    project.PrimaryAsset.Id,
                    StudioPersistedRenderState.Interrupted),
            ]));

        StudioProjectRecoveryState captured = render.CaptureRecoveryState(
            project.PrimaryAsset.Id,
            TimeSpan.Zero);
        TestAssert.Equal(StudioPersistedRenderState.Interrupted,
            captured.RenderQueue[0].State,
            "A crash-time render must remain interrupted rather than being assumed complete.");
        TestAssert.Equal("Interrupted", render.QueueItems[0].Status,
            "The restored queue should surface its interrupted state.");
        return Task.CompletedTask;
    }

    private sealed class PersistenceFixture : IDisposable
    {
        public PersistenceFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "ReplayFoundryStudioPersistenceTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SourcePath = Path.Combine(Root, "source.mkv");
            File.WriteAllBytes(SourcePath, [1, 2, 3, 4, 5]);
            Store = new JsonStudioProjectStore(Path.Combine(Root, "projects"));
        }

        public string Root { get; }
        public string SourcePath { get; }
        public JsonStudioProjectStore Store { get; }

        public GenerationOutputProject CreateProject()
        {
            var media = TestMediaFactory.Create(
                SourcePath,
                duration: TimeSpan.FromMinutes(2),
                hasAudio: true);
            const string candidateId = "candidate-1";
            const string neighborhoodId = "caption-candidate-1";
            var sourceSelection = new GenerationCaptionSourceSelection(
                SourcePath,
                media.AudioStreams[0].Index,
                CaptionAudioContentRole.CreatorCommentary);
            var segment = new AudioTranscriptionSegment(
                "segment-1",
                neighborhoodId,
                "we found the hidden route",
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(11),
                TimeSpan.FromSeconds(13));
            GenerationCandidateCaptionTrack captions =
                GenerationCandidateCaptionTrack.RestoreStudioHandoff(
                    candidateId,
                    neighborhoodId,
                    sourceSelection,
                    GenerationCaptionStylePreset.KaraokeSweep,
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(20),
                    media.Duration,
                    [segment],
                    isUserEdited: true,
                    GenerationCaptionSuppressionReason.None);
            var context = new ClipEditorialContext(
                candidateId,
                SourcePath,
                "Test Game",
                TimeSpan.FromSeconds(12),
                TimeSpan.FromSeconds(28),
                media.Duration,
                91,
                "A retained deterministic moment.");
            context = context.WithTranscripts(
            [
                new ClipEditorialTranscriptContext(
                    media.AudioStreams[0].Index,
                    new AudioContentRoleAssignment(
                        AudioContentRole.CreatorSpeech,
                        AudioContentRoleSource.UserConfirmed),
                    "we found the hidden route",
                    ClipEditorialTranscriptAuthority.UserCorrected,
                    [
                        new ClipEditorialTranscriptSpan(
                            TimeSpan.FromSeconds(12),
                            TimeSpan.FromSeconds(13),
                            "we found the hidden route"),
                    ]),
            ]);
            context = context.WithVisualText(new ClipVisualTextContext(
                candidateId,
                SourcePath,
                NormalizedRectangle.FullFrame,
                frames: [],
                anchors:
                [
                    new VisualTextAnchor(
                        "hidden route",
                        "Hidden Route",
                        VisualTextAnchorAuthority.RepeatedAcrossFrames,
                        [TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(13)]),
                ]));
            var metadata = new ClipEditorialMetadataDraft(
                "A grounded title",
                "I found the hidden route during this run.",
                ["TestGame"],
                ClipEditorialMetadataOrigin.UserEdited,
                new ClipEditorialMetadataGeneratorIdentity("test", "1.0"),
                attempt: 1,
                readiness: ClipEditorialMetadataReadiness.UserEditedDraft,
                qualityIssues: ClipEditorialMetadataReview.BuildIssues(
                    ["UnstableReadableTextReuse"]),
                priorAcceptedTitles:
                [
                    "An earlier grounded title #TestGame",
                ]);
            var features = new ClipPreferenceFeatureVector(
                [new ClipPreferenceFeature(
                    ClipPreferenceFeatureCode.Duration,
                    0.5)]);
            var appearance = new StudioClipAppearance(
                GenerationCaptionStylePreset.KaraokeSweep,
                72,
                StudioVideoEffectPreset.Noir,
                45,
                captionWordLimit: StudioCaptionWordLimitPreset.Streamlined,
                captionMaximumWidthPercent: 75,
                captionFontScalePercent: 110);
            GenerationOutputAsset asset =
                GenerationOutputAsset.RestoreStudioHandoff(
                    candidateId,
                    rank: 1,
                    media,
                    outputFullPath: null,
                    thumbnailFullPath: null,
                    TimeSpan.FromSeconds(12),
                    TimeSpan.FromSeconds(28),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30),
                    score: 91,
                    qualityTarget: 80,
                    GenerationCandidateSelectionReason.QualityQualified,
                    "A retained deterministic moment.",
                    captions,
                    appearance,
                    context,
                    metadata,
                    features,
                    GenerationOutputAssetDisposition.IncludeInFinalRender);
            return new GenerationOutputProject(
                "project-persistence",
                GenerationMode.IndividualClips,
                Path.Combine(Root, "output"),
                requestedCount: 1,
                ClipFulfillmentPreference.QualityFirst,
                GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
                [asset],
                DateTimeOffset.UtcNow,
                resultCountMode: GenerationResultCountMode.Exact,
                candidateSetFingerprint: "candidates-test");
        }

        public GenerationOutputProject CreateProjectWithHiddenMoment(
            bool includeBindings = false)
        {
            GenerationOutputProject project = CreateProject();
            GenerationOutputAsset asset = project.PrimaryAsset;
            const string hiddenId = "hidden-persistence-1";
            GenerationCaptionSourceSelection? captionSelection =
                includeBindings
                    ? new GenerationCaptionSourceSelection(
                        asset.SourceFullPath,
                        asset.SourceMedia.AudioStreams[0].Index,
                        CaptionAudioContentRole.CreatorCommentary)
                    : null;
            ClipEditorialContext? editorialContext = includeBindings
                ? new ClipEditorialContext(
                    hiddenId,
                    asset.SourceFullPath,
                    "Test Game alternate",
                    TimeSpan.FromSeconds(40),
                    TimeSpan.FromSeconds(55),
                    asset.SourceDuration,
                    68,
                    "A safe alternate retained below the requested quality target.")
                : null;
            GenerationHiddenMoment hidden =
                GenerationHiddenMoment.RestoreStudioHandoff(
                    hiddenId,
                    reviewOrder: 1,
                    sourceOrder: 0,
                    sourceMedia: asset.SourceMedia,
                    sourceStart: TimeSpan.FromSeconds(40),
                    sourceEnd: TimeSpan.FromSeconds(55),
                    finalScore: 68,
                    qualityTarget: 80,
                    reason: GenerationHiddenMomentReason.BelowQualityTarget,
                    explanation:
                        "A safe alternate retained below the requested quality target.",
                    preferenceFeatures: asset.PreferenceFeatures!,
                    editorialPreference:
                        ClipEditorialGenerationPreference.AiRequired,
                    editorialContext,
                    editorialMetadata: null,
                    captionSourceSelection: captionSelection,
                    captionStyle: includeBindings
                        ? GenerationCaptionStylePreset.KaraokeSweep
                        : null);
            return new GenerationOutputProject(
                project.Id,
                project.Mode,
                project.OutputDirectory,
                project.RequestedCount,
                project.FulfillmentPreference,
                project.FulfillmentOutcome,
                project.Assets,
                project.CreatedAtUtc,
                project.FinalizedAtUtc,
                project.ResultCountMode,
                hiddenMoments: [hidden],
                candidateSetFingerprint: project.CandidateSetFingerprint);
        }

        public GenerationOutputProject CreateProjectWithGameKnowledge()
        {
            GenerationOutputProject project = CreateProject();
            GenerationOutputAsset asset = project.PrimaryAsset;
            var identity = new ConfirmedGameIdentity(
                "Q777",
                "Test Game",
                edition: null,
                releaseYear: 2026,
                developer: "Test Studio",
                series: "Test Game",
                GameIdentityAuthority.Wikidata,
                "Wikidata",
                "en",
                userConfirmed: true,
                DateTimeOffset.UnixEpoch,
                GameKnowledgeSourcePermissions.WikimediaDefault);
            string[] texts = Enumerable.Range(0, 10)
                .Select(index => index < 4
                    ? $"Selected route segment {index} provides bounded context."
                    : $"Unselected route segment {index} belongs only to the reusable cache.")
                .ToArray();
            const string sourceId = "gks-studio-public-context";
            var source = new GameKnowledgeSource(
                sourceId,
                GameKnowledgeSourceKind.Wikipedia,
                "Test Game",
                new Uri("https://example.test/wiki/Test_Game?oldid=777"),
                "777",
                DateTimeOffset.UnixEpoch,
                "CC-BY-SA-4.0",
                new Uri("https://creativecommons.org/licenses/by-sa/4.0/"),
                "Test Game contributors, revision 777.",
                GameKnowledgePassage.ComputeSha256(string.Join("\n", texts)));
            GameKnowledgePassage[] passages = texts.Select((text, index) =>
                new GameKnowledgePassage(
                    $"gkp-studio-{index}",
                    sourceId,
                    index == 0 ? "Overview" : "Plot",
                    text,
                    GameKnowledgePassage.ComputeSha256(text))).ToArray();
            DateTimeOffset retrievedAtUtc = DateTimeOffset.UtcNow;
            var snapshot = new GameKnowledgeSnapshot(
                identity,
                new GameKnowledgeProviderIdentity(
                    "Wikimedia open game knowledge",
                    "2.0.0"),
                retrievedAtUtc,
                [source],
                passages,
                [
                    new GameKnowledgeComponentState(
                        GameKnowledgeComponentKind.WikipediaPrimary,
                        GameKnowledgeComponentCompleteness.Complete,
                        retrievedAtUtc,
                        revisionId: source.RevisionId,
                        licenseIdentifier: source.LicenseIdentifier,
                        attribution: source.Attribution,
                        contentSha256: source.ContentSha256),
                    new GameKnowledgeComponentState(
                        GameKnowledgeComponentKind.StrategyWiki,
                        GameKnowledgeComponentCompleteness.Disabled,
                        retrievedAtUtc),
                ]);
            var knowledge = new ClipGameKnowledgeContext(
                "Test Game",
                snapshot,
                passages.Take(4).Select(passage => new GameKnowledgeMatch(
                    passage,
                    GameKnowledgeMatchStrength.GeneralContext,
                    relevance: 0,
                    matchedTerms: [])));
            ClipEditorialContext retained = asset.EditorialContext!;
            var context = new ClipEditorialContext(
                retained.CandidateId,
                retained.SourceFullPath,
                retained.SourceLabel,
                retained.SourceStart,
                retained.SourceEnd,
                retained.SourceDuration,
                retained.DeterministicScore,
                retained.DeterministicReason,
                retained.Transcripts,
                retained.Evidence,
                new ClipEditorialGameContext(
                    "Test Game",
                    "#TestGame",
                    contextNotes: null,
                    ClipEditorialGameContextSource.UserConfirmed,
                    useOpenGameKnowledge: true,
                    identity),
                knowledge,
                retained.GameplayRegion,
                retained.VisualText);
            return project.ReplaceAsset(
                asset.WithCurrentCutEditorialMetadata(
                    context,
                    asset.EditorialMetadata!));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class RecordingProjectStore : IStudioProjectStore
    {
        public List<(GenerationOutputProject Project, long Revision)> Saved { get; } = [];

        public void Save(GenerationOutputProject project, long revision,
            StudioProjectRecoveryState? recovery = null) =>
            Saved.Add((project, revision));

        public StudioProjectLoadResult Load(string projectId) =>
            new(StudioProjectLoadOutcome.NotFound, "not found");

        public bool Exists(string projectId) => false;

        public void Delete(string projectId)
        {
        }
    }

    private sealed class FailingProjectStore : IStudioProjectStore
    {
        public void Save(
            GenerationOutputProject project,
            long revision,
            StudioProjectRecoveryState? recovery = null) =>
            throw new IOException("diagnostic save failure");

        public StudioProjectLoadResult Load(string projectId) =>
            new(StudioProjectLoadOutcome.NotFound, "not found");

        public bool Exists(string projectId) => false;

        public void Delete(string projectId)
        {
        }
    }

    private sealed class BlockingLoadProjectStore : IStudioProjectStore
    {
        public ManualResetEventSlim LoadStarted { get; } = new(false);
        public ManualResetEventSlim AllowLoad { get; } = new(false);
        public List<(GenerationOutputProject Project, long Revision)> Saved { get; } = [];

        public void Save(
            GenerationOutputProject project,
            long revision,
            StudioProjectRecoveryState? recovery = null) =>
            Saved.Add((project, revision));

        public StudioProjectLoadResult Load(string projectId)
        {
            LoadStarted.Set();
            AllowLoad.Wait(TimeSpan.FromSeconds(5));
            return new StudioProjectLoadResult(
                StudioProjectLoadOutcome.NotFound,
                "not found");
        }

        public bool Exists(string projectId) => false;
        public void Delete(string projectId)
        {
        }
    }

    private sealed class BlockingFirstSaveProjectStore : IStudioProjectStore
    {
        private int _saveCount;
        public ManualResetEventSlim FirstSaveStarted { get; } = new(false);
        public ManualResetEventSlim AllowFirstSave { get; } = new(false);
        public List<(GenerationOutputProject Project, long Revision)> Saved { get; } = [];

        public void Save(
            GenerationOutputProject project,
            long revision,
            StudioProjectRecoveryState? recovery = null)
        {
            if (Interlocked.Increment(ref _saveCount) == 1)
            {
                FirstSaveStarted.Set();
                AllowFirstSave.Wait(TimeSpan.FromSeconds(5));
            }
            Saved.Add((project, revision));
        }

        public StudioProjectLoadResult Load(string projectId) =>
            new(StudioProjectLoadOutcome.NotFound, "not found");
        public bool Exists(string projectId) => false;
        public void Delete(string projectId)
        {
        }
    }

    private sealed class NonPumpingSingleThreadSynchronizationContext :
        SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // A WPF dispatcher cannot run posted work while its UI thread is
            // synchronously blocked in Dispose. Intentionally leave posted
            // callbacks pending so this test detects captured continuations.
        }
    }
}
