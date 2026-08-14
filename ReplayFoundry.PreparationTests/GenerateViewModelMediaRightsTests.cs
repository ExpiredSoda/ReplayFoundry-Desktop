using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.RecentProjects;
using ReplayFoundry.Desktop.Features.Generate.Workflow;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerateViewModelWorkflowTests
{
    private static async Task MediaRightsConfirmationGatesExactSourceSelection()
    {
        var confirmation = new TestMediaRightsConfirmation(result: false);
        ViewModelContext context = CreateContext(
            mediaRightsConfirmation: confirmation);

        await context.ViewModel.ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            1,
            confirmation.RequestCount,
            "Continue should request one confirmation for the selected batch.");
        TestAssert.Equal(
            0,
            context.Coordinator.PreparationRunCount,
            "Declining rights confirmation must stop before source preparation.");
        TestAssert.Equal(
            ReplayFoundry.Desktop.Features.Generate.Workflow.GenerateWorkflowState.SourceSelection,
            context.ViewModel.WorkflowState,
            "Declining rights confirmation must keep Generate at source selection.");
        TestAssert.Equal(
            1,
            context.ViewModel.SelectedSourceCount,
            "Declining must leave the source selection intact.");

        confirmation.Result = true;
        await context.ViewModel.ContinueToGenerationSetupAsync();
        await context.ViewModel.ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            2,
            confirmation.RequestCount,
            "An accepted unchanged selection should not prompt again in this session.");
        TestAssert.Equal(
            1,
            context.Coordinator.PreparationRunCount,
            "Accepted rights should allow preparation and its normal cache reuse.");
        TestAssert.Equal(
            context.PrimaryPath,
            confirmation.LastSources.Single().FullPath,
            "The confirmation must receive the exact selected source.");

        context.ViewModel.IsMontageSelected = true;
        await context.ViewModel.ContinueToGenerationSetupAsync();
        TestAssert.Equal(
            2,
            confirmation.RequestCount,
            "Changing setup mode alone must not invalidate rights for unchanged files.");

        string addedPath = TestMediaFactory.CreateExistingSourcePath(
            "generate-rights-added.mkv");
        context.ViewModel.AddDroppedFiles([addedPath]);
        await context.ViewModel.ContinueToGenerationSetupAsync();

        TestAssert.Equal(
            3,
            confirmation.RequestCount,
            "Changing the source selection must invalidate prior confirmation.");
        TestAssert.Equal(
            2,
            confirmation.LastSources.Count,
            "The renewed confirmation must cover the complete changed batch.");
        TestAssert.Equal(
            context.PrimaryPath,
            confirmation.LastSources[0].FullPath,
            "The renewed confirmation must preserve source order.");
        TestAssert.Equal(
            addedPath,
            confirmation.LastSources[1].FullPath,
            "The renewed confirmation must include the added source in order.");
    }

    private static Task RecentProjectsOpenCachedStudioDrafts()
    {
        string sourcePath = TestMediaFactory.CreateExistingSourcePath(
            "generate-rights-recent.mkv");
        var project = new RecentGenerationProject(
            "rights-recent-project",
            GenerationMode.IndividualClips,
            [sourcePath],
            clipCount: 1,
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero),
            isFinalized: false);
        GenerationOutputProject retained = CreateRetainedProject(
            project.ProjectId,
            sourcePath);

        var currentCatalog = new StubRecentProjectCatalog(
            project,
            retained);
        var projectSwitch = new StubStudioProjectSwitchService();
        var currentConfirmation = new TestMediaRightsConfirmation(result: false);
        ViewModelContext currentContext = CreateContext(
            seedSource: false,
            mediaRightsConfirmation: currentConfirmation,
            recentProjectCatalog: currentCatalog,
            studioProjectSwitch: projectSwitch);
        int studioRequests = 0;
        currentContext.ViewModel.StudioRequested += (_, _) => studioRequests++;

        currentContext.ViewModel.OpenRecentProjectCommand.Execute(project);

        TestAssert.Equal(1, studioRequests,
            "The live project should open Studio directly.");
        TestAssert.Equal(0, currentConfirmation.RequestCount,
            "Opening an already-created Studio draft must not ask for source-processing rights.");
        TestAssert.Equal(retained.Id, projectSwitch.LastProject?.Id,
            "Generate must route the retained project through Studio's switch boundary.");

        var restoredCatalog = new StubRecentProjectCatalog(project, null);
        var restoredConfirmation = new TestMediaRightsConfirmation(result: false);
        ViewModelContext restoredContext = CreateContext(
            seedSource: false,
            mediaRightsConfirmation: restoredConfirmation,
            recentProjectCatalog: restoredCatalog);

        restoredContext.ViewModel.OpenRecentProjectCommand.Execute(project);
        TestAssert.Equal(0, restoredContext.ViewModel.SelectedSourceCount,
            "An expired summary must not silently turn into a new Generate run.");
        TestAssert.Equal(0, restoredConfirmation.RequestCount,
            "Opening a recent-project summary must not start source processing.");
        TestAssert.Equal(0, restoredContext.Coordinator.PreparationRunCount,
            "An unavailable Studio draft must remain an informational result.");
        TestAssert.True(
            restoredContext.ViewModel.RecentProjectStatus?.Contains(
                "has no Studio draft that can be reopened",
                StringComparison.OrdinalIgnoreCase) == true,
            "The past-session result should explain why Studio cannot reopen it.");
        return Task.CompletedTask;
    }

    private static Task RecentProjectsRequireClearConfirmation()
    {
        string sourcePath = TestMediaFactory.CreateExistingSourcePath(
            "generate-clear-confirmation.mkv");
        var recent = new RecentGenerationProject(
            "clear-confirmation-project",
            GenerationMode.IndividualClips,
            [sourcePath],
            clipCount: 1,
            new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero),
            isFinalized: false);
        var catalog = new StubRecentProjectCatalog(recent, null);
        var confirmation = new StubRecentProjectsClearConfirmation(false);
        ViewModelContext context = CreateContext(
            seedSource: false,
            recentProjectCatalog: catalog,
            recentProjectsClearConfirmation: confirmation);

        context.ViewModel.ClearRecentProjectsCommand.Execute(null);
        TestAssert.Equal(1, confirmation.RequestCount,
            "Clear all must first describe its scope and request confirmation.");
        TestAssert.Equal(0, catalog.ClearCount,
            "Declining the warning must leave every saved project intact.");
        TestAssert.True(context.ViewModel.HasRecentProjects,
            "Declining clear all must retain the visible project.");

        confirmation.Result = true;
        context.ViewModel.ClearRecentProjectsCommand.Execute(null);
        TestAssert.Equal(1, catalog.ClearCount,
            "Accepting the warning must invoke exactly one bounded clear.");
        TestAssert.False(context.ViewModel.HasRecentProjects,
            "A confirmed clear must immediately update the Generate surface.");
        return Task.CompletedTask;
    }

    private sealed class StubRecentProjectCatalog : IRecentGenerationProjectCatalog
    {
        private readonly ObservableCollection<RecentGenerationProject> _projects;
        private readonly GenerationOutputProject? _project;

        public StubRecentProjectCatalog(
            RecentGenerationProject project,
            GenerationOutputProject? retainedProject)
        {
            _projects = new ObservableCollection<RecentGenerationProject>
            {
                project,
            };
            Projects = new ReadOnlyObservableCollection<RecentGenerationProject>(
                _projects);
            _project = retainedProject;
        }

        public int ClearCount { get; private set; }

        public ReadOnlyObservableCollection<RecentGenerationProject> Projects
        {
            get;
        }

        public int ClearAll()
        {
            ClearCount++;
            int count = _projects.Count;
            _projects.Clear();
            return count;
        }

        public bool TryGetStudioProject(
            string projectId,
            out GenerationOutputProject? project)
        {
            project = _project?.Id.Equals(
                projectId,
                StringComparison.Ordinal) == true
                    ? _project
                    : null;
            return project is not null;
        }
    }

    private sealed class StubRecentProjectsClearConfirmation(bool result) :
        IRecentProjectsClearConfirmation
    {
        public bool Result { get; set; } = result;
        public int RequestCount { get; private set; }

        public bool ConfirmClear(int projectCount)
        {
            RequestCount++;
            TestAssert.Equal(1, projectCount,
                "The warning must receive the exact visible project count.");
            return Result;
        }
    }

    private sealed class StubStudioProjectSwitchService :
        IStudioProjectSwitchService
    {
        public GenerationOutputProject? LastProject { get; private set; }

        public StudioProjectSwitchResult TrySwitchProject(
            GenerationOutputProject project)
        {
            LastProject = project;
            return new StudioProjectSwitchResult(
                StudioProjectSwitchOutcome.Switched,
                "switched");
        }
    }

    private static GenerationOutputProject CreateRetainedProject(
        string projectId,
        string sourcePath)
    {
        var asset = new GenerationOutputAsset(
            projectId + "-asset",
            1,
            TestMediaFactory.Create(sourcePath, TimeSpan.FromMinutes(5)),
            outputFullPath: null,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(50),
            80,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            "Retained candidate");
        return new GenerationOutputProject(
            projectId,
            GenerationMode.IndividualClips,
            Path.Combine(Path.GetTempPath(), projectId),
            1,
            ClipFulfillmentPreference.QualityFirst,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UtcNow);
    }
}
