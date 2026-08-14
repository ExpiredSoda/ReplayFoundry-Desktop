using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static class EditorialMetadataPreferenceLearningTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Editorial preference extraction retains structural numbers only",
            ExtractionIsStructuralAndImmutable),
        new(
            "Editorial preference evidence has explicit bounded strengths",
            EvidenceStrengthsAreTyped),
        new(
            "Editorial preference learning defaults off without creating a profile",
            DisabledRecorderDoesNotCreateProfile),
        new(
            "Editorial preference store replaces aggregate-only evidence atomically",
            StoreReplacesAggregateEvidence),
        new(
            "Editorial preference consent and profile participate in local reset",
            ConsentAndProfileParticipateInReset),
        new(
            "Settings requires explicit local structural style-learning consent",
            SettingsControlsConsentExplicitly),
        new(
            "Studio Save records only actual human metadata corrections",
            StudioSaveRecordsActualCorrections),
    ];

    private static Task ExtractionIsStructuralAndImmutable()
    {
        const string secretTitle = "VOIDRIFT Channel-9842?!";
        const string secretDescription =
            "Transcript/model text at C:\\Private\\game-name.mkv\nSecond line!";
        var tags = new List<string>
        {
            "#PrivateGame",
            "CreatorChannel9842",
        };
        EditorialMetadataPreferenceFeatureVector vector =
            EditorialMetadataStructuralFeatureExtractor.Extract(
                secretTitle,
                secretDescription,
                tags);
        tags.Clear();

        TestAssert.Equal(
            12,
            vector.Features.Count,
            "The versioned structural schema should remain complete.");
        TestAssert.True(
            vector.Features.All(static feature =>
                feature.NormalizedValue is >= 0 and <= 1),
            "Every structural measurement must be normalized.");
        TestAssert.True(
            Enum.GetNames<EditorialMetadataPreferenceFeatureCode>()
                .All(static name =>
                    !new[]
                    {
                        "Word",
                        "Token",
                        "Ngram",
                        "Embedding",
                        "Game",
                        "Transcript",
                        "Path",
                        "Channel",
                        "Model",
                    }.Any(forbidden => name.Contains(
                        forbidden,
                        StringComparison.OrdinalIgnoreCase))),
            "The versioned schema must remain structural and game-agnostic.");
        TestAssert.Equal(
            secretTitle.Length /
                (double)EditorialMetadataStructuralFeatureExtractor
                    .MaximumObservedTitleCharacters,
            vector.Find(
                EditorialMetadataPreferenceFeatureCode
                    .TitleCharacterCount)!.Value,
            "Title length should be numeric and bounded.");
        TestAssert.Equal(
            2d /
                EditorialMetadataStructuralFeatureExtractor
                    .MaximumObservedDescriptionLines,
            vector.Find(
                EditorialMetadataPreferenceFeatureCode
                    .DescriptionLineCount)!.Value,
            "Description layout should be represented without retaining text.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<EditorialMetadataPreferenceFeature>)
                vector.Features).Clear(),
            "Structural vectors must be immutable snapshots.");

        string serialized = JsonSerializer.Serialize(vector);
        foreach (string forbidden in new[]
                 {
                     "VOIDRIFT",
                     "Channel-9842",
                     "Transcript",
                     "model text",
                     "Private",
                     "game-name",
                     "CreatorChannel",
                 })
        {
            TestAssert.False(
                serialized.Contains(
                    forbidden,
                    StringComparison.OrdinalIgnoreCase),
                "Structural extraction must not retain semantic text: " +
                forbidden);
        }
        return Task.CompletedTask;
    }

    private static Task EvidenceStrengthsAreTyped()
    {
        EditorialMetadataPreferenceFeatureVector before =
            CreateVector("Before 1!", "One line.", ["before"]);
        EditorialMetadataPreferenceFeatureVector after =
            CreateVector("AFTER 22!!", "First.\nSecond.", ["after", "#two"]);

        EditorialMetadataPreferenceEvidence weak =
            EditorialMetadataPreferenceEvidence.UnchangedPublish(after);
        TestAssert.Equal(
            EditorialMetadataPreferenceEvidenceKind.UnchangedPublish,
            weak.Kind,
            "Unchanged publish evidence kind.");
        AssertObservation(
            weak.Observations.Single(),
            EditorialMetadataPreferenceOutcome.Accepted,
            0.25,
            "Unchanged publish evidence should remain deliberately weak.");

        EditorialMetadataPreferenceEvidence correction =
            EditorialMetadataPreferenceEvidence.HumanCorrection(
                before,
                after);
        TestAssert.Equal(
            2,
            correction.Observations.Count,
            "A proven human correction should contribute both sides.");
        AssertObservation(
            correction.Observations[0],
            EditorialMetadataPreferenceOutcome.Rejected,
            1,
            "The pre-correction structure should be strongly rejected.");
        AssertObservation(
            correction.Observations[1],
            EditorialMetadataPreferenceOutcome.Accepted,
            1,
            "The corrected structure should be strongly accepted.");

        foreach ((EditorialMetadataWordingRating rating,
                 EditorialMetadataPreferenceOutcome expected) in new[]
                 {
                     (
                         EditorialMetadataWordingRating.Like,
                         EditorialMetadataPreferenceOutcome.Accepted),
                     (
                         EditorialMetadataWordingRating.Neutral,
                         EditorialMetadataPreferenceOutcome.Neutral),
                     (
                         EditorialMetadataWordingRating.Dislike,
                         EditorialMetadataPreferenceOutcome.Rejected),
                 })
        {
            EditorialMetadataPreferenceEvidence explicitEvidence =
                EditorialMetadataPreferenceEvidence.ExplicitWordingRating(
                    after,
                    rating);
            TestAssert.Equal(
                rating,
                explicitEvidence.ExplicitRating!.Value,
                "Explicit Like/Neutral/Dislike should stay typed.");
            AssertObservation(
                explicitEvidence.Observations.Single(),
                expected,
                1,
                "Explicit wording feedback should be strong.");
        }

        EditorialMetadataPreferenceEvidence youtube =
            EditorialMetadataPreferenceEvidence
                .ConfirmedYouTubeCorrection(before, after);
        TestAssert.Equal(
            EditorialMetadataPreferenceEvidenceKind
                .ConfirmedYouTubeCorrection,
            youtube.Kind,
            "The future confirmed YouTube correction seam must be explicit.");
        TestAssert.Equal(
            2,
            youtube.Observations.Count,
            "A confirmed YouTube correction should retain before and after structural vectors.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<EditorialMetadataPreferenceObservation>)
                correction.Observations).Clear(),
            "Evidence observations must be immutable.");
        return Task.CompletedTask;
    }

    private static Task DisabledRecorderDoesNotCreateProfile()
    {
        string root = CreateRoot();
        string profilePath = Path.Combine(root, "editorial-profile.json");
        string consentPath = Path.Combine(root, "editorial-consent.json");
        try
        {
            var consent = new EditorialMetadataPreferenceLearningConsentState(
                new JsonEditorialMetadataPreferenceLearningConsentStore(
                    consentPath));
            int storeFactoryCalls = 0;
            var recorder = new EditorialMetadataPreferenceRecorder(
                consent,
                () =>
                {
                    storeFactoryCalls++;
                    return new JsonEditorialMetadataPreferenceStore(
                        profilePath);
                });
            EditorialMetadataPreferenceEvidence evidence =
                EditorialMetadataPreferenceEvidence.UnchangedPublish(
                    CreateVector(
                        "Secret Game 9842!",
                        "Private transcript at C:\\Vault\\capture.mkv",
                        ["HiddenChannel"]));

            TestAssert.False(
                consent.IsEnabled,
                "Editorial preference learning must default off.");
            TestAssert.False(
                File.Exists(consentPath),
                "Reading the default-off consent must not persist anything.");
            TestAssert.False(
                recorder.TryRecord(evidence),
                "A disabled recorder must reject evidence.");
            TestAssert.Equal(
                0,
                storeFactoryCalls,
                "Disabled mode must not even instantiate profile storage.");
            TestAssert.False(
                File.Exists(profilePath),
                "Disabled mode must not create a preference profile.");

            DateTimeOffset enabledAt = new(
                2026,
                8,
                10,
                12,
                0,
                0,
                TimeSpan.Zero);
            consent.Enable(enabledAt);
            TestAssert.True(
                File.Exists(consentPath),
                "Only an explicit enable should persist consent.");
            TestAssert.True(
                new JsonEditorialMetadataPreferenceLearningConsentStore(
                    consentPath).Current.IsEnabled,
                "Versioned explicit consent should survive local reload.");
            TestAssert.True(
                recorder.TryRecord(evidence),
                "Explicitly enabled local learning should accept evidence.");
            TestAssert.Equal(
                1,
                storeFactoryCalls,
                "The profile store should be created lazily once.");
            string profileJson = File.ReadAllText(profilePath);
            foreach (string forbidden in new[]
                     {
                         "Secret Game",
                         "Private transcript",
                         "Vault",
                         "capture.mkv",
                         "HiddenChannel",
                     })
            {
                TestAssert.False(
                    profileJson.Contains(
                        forbidden,
                        StringComparison.OrdinalIgnoreCase),
                    "The aggregate profile must not persist semantic metadata: " +
                    forbidden);
            }

            consent.Disable();
            TestAssert.False(
                recorder.TryRecord(
                    EditorialMetadataPreferenceEvidence.HumanCorrection(
                        evidence.Observations[0].Features,
                        evidence.Observations[0].Features)),
                "Disabling consent must stop subsequent writes.");
            TestAssert.Equal(
                profileJson,
                File.ReadAllText(profilePath),
                "Disabled mode must leave an existing local profile unchanged.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static Task StoreReplacesAggregateEvidence()
    {
        string root = CreateRoot();
        string path = Path.Combine(root, "editorial-profile.json");
        try
        {
            var store = new JsonEditorialMetadataPreferenceStore(path);
            EditorialMetadataPreferenceFeatureVector before =
                CreateVector("Before 1!", "One line.", ["one"]);
            EditorialMetadataPreferenceFeatureVector after =
                CreateVector("AFTER 22!!", "First.\nSecond.", ["one", "two"]);
            EditorialMetadataPreferenceEvidence liked =
                EditorialMetadataPreferenceEvidence.ExplicitWordingRating(
                    before,
                    EditorialMetadataWordingRating.Like);
            EditorialMetadataPreferenceEvidence disliked =
                EditorialMetadataPreferenceEvidence.ExplicitWordingRating(
                    before,
                    EditorialMetadataWordingRating.Dislike);

            store.Update(previous: null, liked);
            EditorialMetadataPreferenceProfile replaced = store.Update(
                liked,
                disliked);
            TestAssert.Equal(
                0d,
                replaced.AcceptedWeight,
                "Replacing Like should remove its accepted weight.");
            TestAssert.Equal(
                1d,
                replaced.RejectedWeight,
                "Replacing Like with Dislike should add rejected weight.");
            TestAssert.Equal(
                1,
                replaced.Count(
                    EditorialMetadataPreferenceEvidenceKind
                        .ExplicitWordingRating),
                "Replacing a rating must not duplicate evidence counts.");

            EditorialMetadataPreferenceEvidence correction =
                EditorialMetadataPreferenceEvidence.HumanCorrection(
                    before,
                    after);
            EditorialMetadataPreferenceProfile corrected = store.Update(
                disliked,
                correction);
            TestAssert.Equal(
                1d,
                corrected.AcceptedWeight,
                "A correction should strongly accept its after vector.");
            TestAssert.Equal(
                1d,
                corrected.RejectedWeight,
                "A correction should strongly reject its before vector.");
            TestAssert.Equal(
                1,
                corrected.EvidenceCount,
                "Replacement should leave exactly one aggregate evidence item.");
            TestAssert.Equal(
                1,
                corrected.Count(
                    EditorialMetadataPreferenceEvidenceKind.HumanCorrection),
                "The replacement evidence kind should be retained.");
            TestAssert.Throws<NotSupportedException>(
                () => ((IList<EditorialMetadataPreferenceFeatureStatistics>)
                    corrected.Features).Clear(),
                "The learned profile must be immutable.");

            EditorialMetadataPreferenceProfile reloaded =
                new JsonEditorialMetadataPreferenceStore(path).Current;
            TestAssert.Equal(
                corrected.AcceptedWeight,
                reloaded.AcceptedWeight,
                "Aggregate accepted weight should survive reload.");
            TestAssert.Equal(
                corrected.RejectedWeight,
                reloaded.RejectedWeight,
                "Aggregate rejected weight should survive reload.");
            TestAssert.Equal(
                corrected.EvidenceCount,
                reloaded.EvidenceCount,
                "Typed evidence counts should survive reload.");
            TestAssert.Equal(
                0,
                Directory.GetFiles(root, "*.tmp").Length,
                "Atomic profile writes must clean their staging files.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static async Task ConsentAndProfileParticipateInReset()
    {
        string root = CreateRoot();
        string temporaryRoot = Path.Combine(root, "temporary-workspaces");
        try
        {
            string consentPath = Path.Combine(
                root,
                "editorial-metadata-preference-consent.json");
            string profilePath = Path.Combine(
                root,
                "editorial-metadata-preferences.json");
            var consent = new EditorialMetadataPreferenceLearningConsentState(
                new JsonEditorialMetadataPreferenceLearningConsentStore(
                    consentPath));
            consent.Enable(DateTimeOffset.UtcNow);
            var recorder = new EditorialMetadataPreferenceRecorder(
                consent,
                () => new JsonEditorialMetadataPreferenceStore(profilePath));
            recorder.TryRecord(
                EditorialMetadataPreferenceEvidence.UnchangedPublish(
                    CreateVector("Title!", "Description.", ["tag"])));

            var maintenance = new ReplayFoundryLocalDataMaintenanceService(
                root,
                temporaryRoot);
            ReplayFoundryLocalDataUsage usage = maintenance.Inspect().Single(
                static item =>
                    item.Kind ==
                    ReplayFoundryLocalDataKind.PreferencesAndHistory);
            TestAssert.Equal(
                2,
                usage.FileCount,
                "Consent and the structural profile should both be classified as preferences/history.");
            maintenance.ScheduleReset(new ReplayFoundryLocalDataResetRequest(
                [ReplayFoundryLocalDataKind.PreferencesAndHistory]));
            ReplayFoundryLocalDataCleanupResult result =
                await maintenance.ApplyScheduledResetAsync();

            TestAssert.True(
                result.Succeeded,
                "The scheduled local preference reset should succeed.");
            TestAssert.False(
                File.Exists(consentPath),
                "Local reset should delete editorial learning consent.");
            TestAssert.False(
                File.Exists(profilePath),
                "Local reset should delete the structural preference profile.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task SettingsControlsConsentExplicitly()
    {
        var consent = new EditorialMetadataPreferenceLearningConsentState(
            new InMemoryEditorialMetadataPreferenceLearningConsentStore());
        using var settings = new SettingsViewModel(consent);

        TestAssert.False(
            settings.IsEditorialMetadataPreferenceLearningEnabled,
            "Local editorial style learning must be off by default.");
        TestAssert.True(
            settings.EnableEditorialMetadataPreferenceLearningCommand
                .CanExecute(null),
            "Settings should expose an explicit opt-in while learning is off.");
        string disclosure =
            settings.EditorialMetadataPreferenceLearningDetail + " " +
            settings.EditorialMetadataPreferenceLearningPrivacy;
        foreach (string required in new[]
                 {
                     "numeric",
                     "length",
                     "Nothing is uploaded",
                     "words",
                     "n-grams",
                     "embeddings",
                     "game names",
                     "transcripts",
                     "file paths",
                     "channel IDs",
                     "model text",
                     "generation or ranking",
                 })
        {
            TestAssert.True(
                disclosure.Contains(
                    required,
                    StringComparison.OrdinalIgnoreCase),
                "The local-learning disclosure must state: " + required);
        }

        settings.EnableEditorialMetadataPreferenceLearningCommand.Execute(
            null);
        TestAssert.True(
            consent.IsEnabled,
            "Only the explicit Settings action should enable learning.");
        TestAssert.False(
            settings.EnableEditorialMetadataPreferenceLearningCommand
                .CanExecute(null),
            "The opt-in action should disable once consent is active.");
        TestAssert.True(
            settings.DisableEditorialMetadataPreferenceLearningCommand
                .CanExecute(null),
            "Settings should expose an immediate opt-out.");
        TestAssert.True(
            settings.EditorialMetadataPreferenceLearningNotice.Contains(
                "Nothing was uploaded",
                StringComparison.OrdinalIgnoreCase),
            "Enabling local learning must not imply any upload.");

        settings.DisableEditorialMetadataPreferenceLearningCommand.Execute(
            null);
        TestAssert.False(
            consent.IsEnabled,
            "The explicit Settings opt-out should stop future learning.");
        return Task.CompletedTask;
    }

    private static Task StudioSaveRecordsActualCorrections()
    {
        string root = CreateRoot();
        string profilePath = Path.Combine(root, "studio-style-profile.json");
        try
        {
            string sourcePath = TestMediaFactory.CreateSourcePath(
                "editorial-style-learning.mkv");
            var context = new ClipEditorialContext(
                "candidate-editorial-style-learning",
                sourcePath,
                "PrivateGameName",
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1.5),
                TimeSpan.FromMinutes(5),
                82,
                "Deterministic evidence selected this interval.");
            var metadata = new ClipEditorialMetadataDraft(
                "Short title",
                "One line.",
                ["original"],
                ClipEditorialMetadataOrigin.Heuristic,
                new ClipEditorialMetadataGeneratorIdentity(
                    "Test structural generator",
                    "1.0"),
                attempt: 0);
            var asset = new GenerationOutputAsset(
                context.CandidateId,
                1,
                TestMediaFactory.Create(sourcePath, context.SourceDuration),
                outputFullPath: null,
                context.SourceStart,
                context.SourceEnd,
                context.DeterministicScore,
                70,
                GenerationCandidateSelectionReason.QualityQualified,
                context.DeterministicReason,
                editorialContext: context,
                editorialMetadata: metadata);
            var project = new GenerationOutputProject(
                "project-editorial-style-learning",
                GenerationMode.IndividualClips,
                root,
                1,
                ClipFulfillmentPreference.QualityFirst,
                GenerationClipFulfillmentOutcome
                    .RequestedCountMetAtQualityTarget,
                [asset],
                DateTimeOffset.UtcNow);
            var session = new GenerationOutputSession();
            session.Publish(project);
            var consent = new EditorialMetadataPreferenceLearningConsentState(
                new InMemoryEditorialMetadataPreferenceLearningConsentStore());
            var recorder = new EditorialMetadataPreferenceRecorder(
                consent,
                () => new JsonEditorialMetadataPreferenceStore(profilePath));
            using var editor = new StudioEditorialMetadataViewModel(
                session,
                generator: null,
                profileEditor: null,
                rerollPreference: null,
                new StudioEditorialMetadataCorrectionRecorder(recorder));
            editor.Bind(project, asset);

            TestAssert.False(
                File.Exists(profilePath),
                "Programmatic Studio binding must not create a preference profile.");
            editor.SaveCommand.Execute(null);
            TestAssert.False(
                File.Exists(profilePath),
                "Saving unchanged metadata must not create preference evidence.");

            editor.Title = "Saved while local learning is off";
            editor.SaveCommand.Execute(null);
            TestAssert.Equal(
                "Saved while local learning is off",
                session.Current!.Assets[0].EditorialMetadata!.Title,
                "Default-off learning must not block or alter the Studio Save.");
            TestAssert.False(
                File.Exists(profilePath),
                "A real Studio Save while consent is off must not create a preference profile.");
            string learnedBeforeTitle = editor.Title.Trim();
            consent.Enable(DateTimeOffset.UtcNow);
            editor.SaveCommand.Execute(null);
            TestAssert.False(
                File.Exists(profilePath),
                "Enabling consent must not retroactively record an unchanged Save.");

            editor.Title = "A MUCH LONGER SAVED TITLE 2042!!";
            editor.Description = "First saved line.\nSecond saved line!";
            editor.Tags = "alpha, #beta, gamma";
            editor.SaveCommand.Execute(null);

            EditorialMetadataPreferenceProfile profile =
                new JsonEditorialMetadataPreferenceStore(profilePath).Current;
            TestAssert.Equal(
                1,
                profile.Count(
                    EditorialMetadataPreferenceEvidenceKind.HumanCorrection),
                "One actual Studio Save correction should create one typed evidence item.");
            TestAssert.Equal(
                1d,
                profile.AcceptedWeight,
                "The saved after structure should be strongly accepted.");
            TestAssert.Equal(
                1d,
                profile.RejectedWeight,
                "The replaced before structure should be strongly rejected.");
            TestAssert.Equal(
                learnedBeforeTitle.Length /
                    (double)EditorialMetadataStructuralFeatureExtractor
                        .MaximumObservedTitleCharacters,
                profile.Find(
                    EditorialMetadataPreferenceFeatureCode
                        .TitleCharacterCount)!.RejectedMean!.Value,
                "The exact previously saved title structure should be rejected.");
            TestAssert.Equal(
                editor.Title.Trim().Length /
                    (double)EditorialMetadataStructuralFeatureExtractor
                        .MaximumObservedTitleCharacters,
                profile.Find(
                    EditorialMetadataPreferenceFeatureCode
                        .TitleCharacterCount)!.AcceptedMean!.Value,
                "The exact newly saved title structure should be accepted.");

            editor.SaveCommand.Execute(null);
            TestAssert.Equal(
                1,
                new JsonEditorialMetadataPreferenceStore(profilePath)
                    .Current.Count(
                        EditorialMetadataPreferenceEvidenceKind
                            .HumanCorrection),
                "Repeated Save without a real metadata change must not duplicate evidence.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static void AssertObservation(
        EditorialMetadataPreferenceObservation observation,
        EditorialMetadataPreferenceOutcome expectedOutcome,
        double expectedWeight,
        string message)
    {
        TestAssert.Equal(
            expectedOutcome,
            observation.Outcome,
            message + " Outcome.");
        TestAssert.Equal(
            expectedWeight,
            observation.Weight,
            message + " Weight.");
    }

    private static EditorialMetadataPreferenceFeatureVector CreateVector(
        string title,
        string description,
        IEnumerable<string> tags) =>
        EditorialMetadataStructuralFeatureExtractor.Extract(
            title,
            description,
            tags);

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-EditorialPreferenceTests-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
