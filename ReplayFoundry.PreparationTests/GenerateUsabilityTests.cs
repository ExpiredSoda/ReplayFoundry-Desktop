using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Audio;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.ClipGoals;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.MomentGuidance;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.RecentProjects;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static class GenerateUsabilityTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Multi-stream captions require explicit stream and role confirmation", MultiStreamRequiresConfirmation),
        new("Audio auditions prepare source-named waveforms and use the selected absolute stream", AudioAuditionUsesExactStream),
        new("Audio auditions prepare before captioning is enabled", AudioAuditionsPrepareOnEntry),
        new("Clip count fulfillment explains whether quality is a target or a threshold", ClipCountQualityRelationshipIsExplicit),
        new("Priority moments expose full-source playback state without changing guidance", PriorityMomentPlaybackStateIsExplicit),
        new("Media time display omits sub-second precision without changing timestamps", MediaTimeDisplayIsHumanReadable),
        new("Recent Generate projects persist without serializing media payloads", RecentProjectsPersist),
        new("Recent Generate projects retain and reopen multiple editable Studio drafts", RecentProjectsReopenStudioDrafts),
        new("Recent Generate projects cap at ten and clear saved drafts without learning data", RecentProjectsCapAndClearSavedDrafts),
    ];

    private static Task MultiStreamRequiresConfirmation()
    {
        PreparedGenerationSource source = CreatePreparedSource(
            "multi-stream-role.mkv",
            audioStreamCount: 2);
        using var viewModel = new CaptionAudioSelectionViewModel(
            source,
            initialSelection: null);

        TestAssert.Null(viewModel.SelectedStream,
            "Metadata order or default disposition must not choose a caption stream.");
        TestAssert.Null(viewModel.SelectedRole,
            "Metadata titles must not assign creator or game speech semantics.");
        TestAssert.False(viewModel.IsValid,
            "A multi-stream source needs an explicit stream and role.");

        viewModel.SelectedStream = viewModel.Streams[1];
        TestAssert.False(viewModel.IsValid,
            "A stream alone must not imply its semantic role.");
        viewModel.SelectedRole = viewModel.Roles.Single(value =>
            value.Value == CaptionAudioContentRole.GameDialogue);
        TestAssert.True(viewModel.IsValid,
            "A user-confirmed stream and role should be valid.");
        TestAssert.Equal(
            viewModel.Streams[1].Stream.Index,
            viewModel.CreateSelection()!.AbsoluteAudioStreamIndex,
            "The exact absolute stream index must be retained.");
        return Task.CompletedTask;
    }

    private static async Task AudioAuditionUsesExactStream()
    {
        PreparedGenerationSource source = CreatePreparedSource(
            "audition-stream.mkv",
            audioStreamCount: 2);
        var audition = new RecordingAuditionService();
        using var viewModel = new CaptionAudioSelectionViewModel(
            source,
            initialSelection: null,
            auditionService: audition);
        viewModel.SelectedStream = viewModel.Streams[1];
        await viewModel.PrepareAuditionsAsync();

        await ((AsyncDelegateCommand)viewModel.AuditionCommand).ExecuteAsync();

        TestAssert.Equal(
            viewModel.Streams[1].Stream.Index,
            audition.StreamIndex,
            "Audition must use the explicitly selected absolute stream.");
        TestAssert.Equal(
            2,
            audition.PreparedStreamIndices.Count,
            "Each inspected stream should receive one bounded prepared sample.");
        TestAssert.True(
            viewModel.SelectedStream.DisplayName.Contains(
                source.Source.FileName,
                StringComparison.Ordinal),
            "The manual selector should lead with the recording source name.");
        TestAssert.False(
            viewModel.SelectedStream.DisplayName.Contains(
                "PCM",
                StringComparison.OrdinalIgnoreCase),
            "Codec internals should not dominate the source selector.");
        TestAssert.True(viewModel.HasWaveform,
            "The selected prepared sample should project an immutable waveform.");
        TestAssert.Equal(4, viewModel.WaveformBars.Count,
            "The waveform projection should preserve every prepared peak.");
        TestAssert.True(viewModel.IsAuditionPlaying,
            "The selected waveform should react while its exact sample is playing.");
        TestAssert.Equal(0.25d, viewModel.AuditionProgress,
            "Waveform progress should follow the player's bounded media position.");
    }

    private static async Task AudioAuditionsPrepareOnEntry()
    {
        GenerationSetupDraft draft = CreateSetupDraft("audition-on-entry.mkv");
        var audition = new RecordingAuditionService();
        using var viewModel = new AudioStepViewModel(
            draft,
            auditionService: audition);

        TestAssert.False(viewModel.IsCaptioningEnabled,
            "Caption rendering should remain opt-in.");
        await viewModel.PrepareAuditionsAsync();

        TestAssert.Equal(1, audition.PreparedStreamIndices.Count,
            "Entering Audio should prepare its bounded source audition without enabling captions.");
    }

    private static Task RecentProjectsPersist()
    {
        string root = CreateRoot();
        string storePath = Path.Combine(root, "recent.json");
        string source = TestMediaFactory.CreateExistingSourcePath("recent-source.mkv");
        var asset = new GenerationOutputAsset(
            "recent-candidate",
            1,
            TestMediaFactory.Create(source, TimeSpan.FromMinutes(5)),
            outputFullPath: null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            88,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            "test");
        var project = new GenerationOutputProject(
            "recent-project",
            GenerationMode.IndividualClips,
            root,
            1,
            ClipFulfillmentPreference.QualityFirst,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        using (var catalog = new RecentGenerationProjectCatalog(
                   session,
                   new JsonRecentGenerationProjectStore(storePath)))
        {
            session.Publish(project);
            TestAssert.Equal(1, catalog.Projects.Count, "Live recent entry.");
        }

        IReadOnlyList<RecentGenerationProject> restored =
            new JsonRecentGenerationProjectStore(storePath).Read();
        TestAssert.Equal(1, restored.Count, "Persisted recent entry.");
        TestAssert.Equal(source, restored[0].SourcePaths[0], "Restored source path.");
        TestAssert.True(
            new FileInfo(storePath).Length < 16 * 1024,
            "The recent catalog must remain metadata-only.");
        Directory.Delete(root, recursive: true);
        return Task.CompletedTask;
    }

    private static Task RecentProjectsReopenStudioDrafts()
    {
        string root = CreateRoot();
        string storePath = Path.Combine(root, "recent-cache.json");
        var session = new GenerationOutputSession();
        using var studio = new StudioViewModel(session);
        var libraryStore = new InMemoryLibraryCatalogStore();
        using var library = new GenerationLibraryCatalog(
            session,
            libraryStore);
        using var catalog = new RecentGenerationProjectCatalog(
            session,
            new JsonRecentGenerationProjectStore(storePath));
        GenerationOutputProject first = CreateRecentProject(
            root,
            "cached-first",
            "cached-first.mkv");
        GenerationOutputProject second = CreateRecentProject(
            root,
            "cached-second",
            "cached-second.mkv");

        session.Publish(first);
        session.Publish(second);
        TestAssert.True(catalog.TryGetStudioProject(
                first.Id,
                out GenerationOutputProject? retainedFirst),
            "A retained non-current project should remain available to Studio.");
        StudioProjectSwitchResult firstSwitch =
            studio.TrySwitchProject(retainedFirst!);
        TestAssert.True(firstSwitch.Succeeded,
            "Studio should approve a safe retained-project switch.");
        TestAssert.Equal(first.Id, session.Current?.Id,
            "Opening a retained project should activate its exact Studio draft.");
        TestAssert.True(
            catalog.Projects.Single(project => project.ProjectId == first.Id)
                .IsStudioReady,
            "A process-local retained project should be marked Studio ready.");

        IReadOnlyList<RecentGenerationProject> summaries =
            new JsonRecentGenerationProjectStore(storePath).Read();
        TestAssert.False(summaries.Any(static project => project.IsStudioReady),
            "Persisted metadata summaries must not claim an expired Studio cache is ready.");
        var restartedSession = new GenerationOutputSession();
        using var restartedCatalog = new RecentGenerationProjectCatalog(
            restartedSession,
            new JsonRecentGenerationProjectStore(storePath));
        RecentGenerationProject restartedSummary = restartedCatalog.Projects
            .Single(project => project.ProjectId == first.Id);
        TestAssert.False(restartedSummary.IsStudioReady,
            "A restarted process must describe metadata-only history honestly.");
        TestAssert.False(restartedCatalog.TryGetStudioProject(
                first.Id,
                out GenerationOutputProject? expiredProject),
            "A metadata-only summary must never fabricate a partial Studio project.");
        TestAssert.Null(expiredProject,
            "An expired summary must not expose a partial project graph.");
        TestAssert.Null(restartedSession.Current,
            "Opening an expired summary must not mutate the Studio session.");

        GenerationOutputProject finalDraft = CreateRecentProject(
            Path.Combine(root, "final-output"),
            "cached-finalized",
            "cached-finalized.mkv");
        GenerationOutputAsset rendered = finalDraft.PrimaryAsset.WithRenderedOutput(
            Path.Combine(finalDraft.OutputDirectory, "clip.mp4"));
        var finalized = new GenerationOutputProject(
            finalDraft.Id,
            finalDraft.Mode,
            finalDraft.OutputDirectory,
            finalDraft.RequestedCount,
            finalDraft.FulfillmentPreference,
            finalDraft.FulfillmentOutcome,
            [rendered],
            finalDraft.CreatedAtUtc,
            finalizedAtUtc: DateTimeOffset.UtcNow,
            resultCountMode: finalDraft.ResultCountMode,
            hiddenMoments: finalDraft.HiddenMoments);
        session.Publish(finalized);
        TestAssert.True(catalog.TryGetStudioProject(
                finalized.Id,
                out GenerationOutputProject? retainedFinalized),
            "A retained finalized project should remain available to Studio.");
        StudioProjectSwitchResult finalizedSwitch =
            studio.TrySwitchProject(retainedFinalized!);
        TestAssert.True(finalizedSwitch.Succeeded,
            "Studio should reopen a finalized project as an editable revision.");
        TestAssert.False(session.Current!.IsFinalized,
            "Reopening a completed project should produce an editable draft.");
        TestAssert.False(
            session.Current.Id.Equals(
                finalized.Id,
                StringComparison.Ordinal),
            "A reopened revision requires a distinct project identity so Studio render state cannot leak from the completed project.");
        TestAssert.Equal(
            finalized.PrimaryAsset.Id,
            session.Current.PrimaryAsset.Id,
            "Reopening a revision must preserve the candidate identity.");
        TestAssert.Equal(
            finalized.CandidateSetFingerprint,
            session.Current.CandidateSetFingerprint,
            "Reopening a revision must preserve the candidate-set fingerprint.");
        TestAssert.False(
            session.Current.OutputDirectory.Equals(
                finalized.OutputDirectory,
                StringComparison.OrdinalIgnoreCase),
            "A re-render must reserve a new output directory instead of targeting completed files.");
        TestAssert.Null(session.Current.PrimaryAsset.OutputFullPath,
            "A reopened Studio revision must not claim the prior render as its draft output.");
        TestAssert.Equal(1, library.Assets.Count,
            "Opening an editable revision must retain the completed Library revision.");
        string originalLibraryProjectId = library.Assets[0].ProjectId;
        GenerationOutputProject revisionDraft = session.Current;
        GenerationOutputAsset revisionRendered =
            revisionDraft.PrimaryAsset.WithRenderedOutput(
                Path.Combine(revisionDraft.OutputDirectory, "clip.mp4"));
        GenerationOutputProject revisionFinalized = revisionDraft.Finalize(
            [revisionRendered],
            DateTimeOffset.UtcNow);

        session.FinalizeProject(revisionFinalized);

        TestAssert.Equal(2, library.Assets.Count,
            "Finalizing a reopened revision must add a new Library item without replacing the earlier completed revision.");
        TestAssert.True(
            library.Assets.Any(asset => asset.ProjectId.Equals(
                originalLibraryProjectId,
                StringComparison.Ordinal)),
            "The earlier completed revision must remain archived in Library.");
        TestAssert.True(
            library.Assets.Any(asset => asset.ProjectId.Equals(
                revisionFinalized.Id,
                StringComparison.Ordinal)),
            "The re-finalized revision must enter Library under its distinct project identity.");
        TestAssert.Equal(
            2,
            catalog.Projects.Count(project =>
                project.ProjectId.Equals(
                    finalized.Id,
                    StringComparison.Ordinal) ||
                project.ProjectId.Equals(
                    revisionFinalized.Id,
                    StringComparison.Ordinal)),
            "Recent projects must retain both the original completion and its finalized revision.");
        Directory.Delete(root, recursive: true);
        return Task.CompletedTask;
    }

    private static async Task RecentProjectsCapAndClearSavedDrafts()
    {
        string root = CreateRoot();
        string storePath = Path.Combine(root, "recent-limit.json");
        var studioStore = new JsonStudioProjectStore(
            Path.Combine(root, "studio-projects"));
        string learnedFeedbackPath = Path.Combine(root, "learned-feedback.json");
        File.WriteAllText(learnedFeedbackPath, "retained-feedback");
        var session = new GenerationOutputSession();
        using var persistence = new StudioProjectPersistenceCoordinator(
            session,
            studioStore,
            TimeSpan.FromMinutes(1));
        using var catalog = new RecentGenerationProjectCatalog(
            session,
            new JsonRecentGenerationProjectStore(storePath),
            studioStore);
        var projects = new List<GenerationOutputProject>();
        for (int index = 0; index < 12; index++)
        {
            GenerationOutputProject project = CreateRecentProject(
                Path.Combine(root, $"output-{index}"),
                $"recent-limit-{index}",
                $"recent-limit-{index}.mkv");
            projects.Add(project);
            studioStore.Save(project, revision: 1);
            session.Publish(project);
        }

        TestAssert.Equal(
            RecentGenerationProjectCatalog.MaximumItems,
            catalog.Projects.Count,
            "Recent projects must stop at the user-visible ten-project limit.");
        TestAssert.False(
            studioStore.Exists(projects[0].Id),
            "Evicting the oldest shortcut must also remove its stale Studio draft.");
        TestAssert.True(
            studioStore.Exists(projects[^1].Id),
            "The newest bounded Studio draft must remain available.");

        int removed = catalog.ClearAll();
        await persistence.FlushAsync().ConfigureAwait(false);
        TestAssert.Equal(10, removed, "Clear-all count.");
        TestAssert.Equal(0, catalog.Projects.Count, "Visible recent projects.");
        TestAssert.Null(session.Current,
            "Clearing recent projects must release the active saved Studio draft.");
        TestAssert.Equal(
            0,
            new JsonRecentGenerationProjectStore(storePath).Read().Count,
            "The recent shortcut store must be empty after clear-all.");
        TestAssert.False(
            projects.Skip(2).Any(project => studioStore.Exists(project.Id)),
            "Clear all must remove every retained Studio project directory.");
        TestAssert.Equal(
            "retained-feedback",
            File.ReadAllText(learnedFeedbackPath),
            "Recent-project cleanup must not touch independent learned feedback data.");

        Directory.Delete(root, recursive: true);
    }

    private static GenerationOutputProject CreateRecentProject(
        string outputDirectory,
        string projectId,
        string sourceName)
    {
        string source = TestMediaFactory.CreateExistingSourcePath(sourceName);
        var asset = new GenerationOutputAsset(
            projectId + "-candidate",
            1,
            TestMediaFactory.Create(source, TimeSpan.FromMinutes(5)),
            outputFullPath: null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            88,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            "test");
        return new GenerationOutputProject(
            projectId,
            GenerationMode.IndividualClips,
            outputDirectory,
            1,
            ClipFulfillmentPreference.QualityFirst,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UtcNow);
    }

    private static Task ClipCountQualityRelationshipIsExplicit()
    {
        GenerationSetupDraft draft = CreateSetupDraft("clip-goals.mkv");
        var viewModel = new ClipGoalsStepViewModel(draft);
        TestAssert.Equal("Quality target", viewModel.QualityControlLabel,
            "Fill requested count must not present quality as a hard cutoff.");
        TestAssert.True(
            viewModel.CountQualityRelationshipDescription.Contains(
                "not a hard cutoff",
                StringComparison.OrdinalIgnoreCase),
            "Fill-count behavior must be explicit beside the control.");

        viewModel.SelectedFulfillmentOption = viewModel.FulfillmentOptions
            .Single(option =>
                option.Value == ClipFulfillmentPreference.QualityFirst);
        TestAssert.Equal("Quality threshold", viewModel.QualityControlLabel,
            "Quality-first should expose the exact hard threshold.");
        TestAssert.True(
            viewModel.CountQualityRelationshipDescription.Contains(
                "hard cutoff",
                StringComparison.OrdinalIgnoreCase),
            "Quality-first may return fewer clips and must say why.");
        return Task.CompletedTask;
    }

    private static Task PriorityMomentPlaybackStateIsExplicit()
    {
        GenerationSetupDraft draft = CreateSetupDraft("priority-playback.mkv");
        using var viewModel = new MomentGuidanceStepViewModel(draft);
        MomentGuidanceSourceViewModel source = viewModel.SelectedSource;
        TestAssert.Equal(source.MaximumSeconds, source.PreviewMaximumSeconds,
            "The priority timeline must cover the full prepared source.");
        TestAssert.True(Path.IsPathFullyQualified(source.SourceFullPath),
            "The view receives the exact prepared source for local playback.");
        source.ReportPlaybackOpened();
        source.ReportPlaybackState(true);
        TestAssert.True(source.IsPlaybackPlaying,
            "Playback state should remain typed and bindable.");
        TestAssert.Equal("Icon.Pause", source.PlayPauseIconKey,
            "Playing state should project the semantic pause icon.");
        source.CurrentPositionSeconds = 42.875;
        source.AddPointCommand.Execute(null);
        TestAssert.Equal("0:42", source.CurrentPositionText,
            "Priority playback should display whole seconds only.");
        TestAssert.Equal("0:42", source.Items[0].Timing,
            "Priority tick labels should display whole seconds only.");
        TestAssert.Equal(42.875, source.Items[0].StartSeconds,
            "Removing display milliseconds must not reduce internal timestamp precision.");
        return Task.CompletedTask;
    }

    private static Task MediaTimeDisplayIsHumanReadable()
    {
        TestAssert.Equal(
            "0:07",
            MediaTimeFormatter.Format(TimeSpan.FromMilliseconds(7999)),
            "Sub-minute media time should show minutes and seconds.");
        TestAssert.Equal(
            "1:02:03",
            MediaTimeFormatter.Format(
                TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2) +
                TimeSpan.FromSeconds(3.9)),
            "Long media time should show hours, minutes, and seconds.");
        TestAssert.Equal(
            "49:00:00",
            MediaTimeFormatter.Format(TimeSpan.FromHours(49)),
            "Hour display must not wrap at a day boundary.");
        return Task.CompletedTask;
    }

    private static PreparedGenerationSource CreatePreparedSource(
        string name,
        int audioStreamCount)
    {
        string path = TestMediaFactory.CreateExistingSourcePath(name);
        var selected = new SelectedVideoSource(path, isReference: true);
        return new PreparedGenerationSource(
            selected,
            TestMediaFactory.Create(
                path,
                TimeSpan.FromMinutes(2),
                hasAudio: true,
                audioStreamCount: audioStreamCount),
            TestMediaFactory.CreateSnapshot(path, new FileInfo(path).Length));
    }

    private static GenerationSetupDraft CreateSetupDraft(string name)
    {
        PreparedGenerationSource source = CreatePreparedSource(name, 1);
        var request = new GenerationSourcePreparationRequest([source.Source]);
        var preparation = new GenerationSourcePreparationResult(
            request,
            [source]);
        return new GenerationSetupDraft(
            new GenerationSetupRequest(
                GenerationMode.IndividualClips,
                preparation));
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryGenerateUsabilityTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class RecordingAuditionService : IAudioStreamAuditionService
    {
        public event EventHandler<AudioStreamAuditionPlaybackChangedEventArgs>?
            PlaybackChanged;

        public int StreamIndex { get; private set; } = -1;
        public List<int> PreparedStreamIndices { get; } = [];

        public Task<AudioStreamAuditionPreview> PrepareAsync(
            PreparedGenerationSource source,
            int absoluteAudioStreamIndex,
            CancellationToken cancellationToken)
        {
            PreparedStreamIndices.Add(absoluteAudioStreamIndex);
            return Task.FromResult(
                new AudioStreamAuditionPreview(
                    source.Media.FullPath,
                    absoluteAudioStreamIndex,
                    TimeSpan.FromSeconds(24),
                    TimeSpan.FromSeconds(30),
                    [0, 0.25, 0.5, 1]));
        }

        public Task PlayAsync(
            PreparedGenerationSource source,
            int absoluteAudioStreamIndex,
            CancellationToken cancellationToken)
        {
            StreamIndex = absoluteAudioStreamIndex;
            PlaybackChanged?.Invoke(
                this,
                new AudioStreamAuditionPlaybackChangedEventArgs(
                    source.Media.FullPath,
                    absoluteAudioStreamIndex,
                    TimeSpan.FromSeconds(7.5),
                    TimeSpan.FromSeconds(30),
                    isPlaying: true));
            return Task.CompletedTask;
        }

        public void Stop()
        {
        }

        public void Release(PreparedGenerationSource source)
        {
        }
    }

}
