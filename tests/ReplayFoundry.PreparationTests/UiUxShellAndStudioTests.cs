using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using ReplayFoundry.Desktop;
using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Library.Sections;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Publish.Sections;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Browser;
using ReplayFoundry.Desktop.Features.Studio.Inspector;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Presentation.Controls;
using ReplayFoundry.Desktop.Presentation.Converters;
using ReplayFoundry.Desktop.Presentation.Feedback;
using ReplayFoundry.Desktop.Presentation.Accessibility;
using ReplayFoundry.Desktop.Presentation.Workspaces;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Shell;
using ReplayFoundry.Desktop.Shell.Guidance;
using ReplayFoundry.Desktop.Shell.Navigation;
using ReplayFoundry.Desktop.Shell.Windowing;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task ShellRegistersAllInstances()
    {
        var studio = new StudioViewModel();
        var library = new LibraryViewModel();
        var publish = new PublishViewModel();
        var settings = new SettingsViewModel();
        var shell = CreateShell(studio, library, publish, settings);

        foreach (ShellDestination destination in Enum.GetValues<ShellDestination>())
            TestAssert.True(shell.NavigateCommand.CanExecute(destination), $"{destination} should be registered.");
        TestAssert.True(ReferenceEquals(shell.CurrentWorkspace, shell.CurrentWorkspace), "Current workspace should be stable.");
        return Task.CompletedTask;
    }

    private static Task ShellDefaultsToGenerate()
    {
        var shell = CreateShell();
        TestAssert.Equal(ShellDestination.Generate, shell.CurrentDestination, "Generate should remain the default destination.");
        TestAssert.True(shell.CurrentWorkspace is GenerateViewModel, "Generate should own the initial workspace.");
        return Task.CompletedTask;
    }

    private static Task ShellNavigationChangesBothProjections()
    {
        var shell = CreateShell();
        shell.NavigateCommand.Execute(ShellDestination.Library);
        TestAssert.Equal(ShellDestination.Library, shell.CurrentDestination, "Destination should change to Library.");
        TestAssert.True(shell.CurrentWorkspace is LibraryViewModel, "Library should own the current workspace.");
        shell.NavigateCommand.Execute(ShellDestination.Settings);
        TestAssert.Equal(ShellDestination.Settings, shell.CurrentDestination, "Destination should change to Settings.");
        TestAssert.True(shell.CurrentWorkspace is SettingsViewModel, "Settings should own the current workspace.");
        return Task.CompletedTask;
    }

    private static Task ShellPreservesWorkspaceInstances()
    {
        var studio = new StudioViewModel();
        var library = new LibraryViewModel { SelectedCategory = LibraryCategory.GeneratedClips };
        var publish = new PublishViewModel { Title = "Draft" };
        var settings = new SettingsViewModel { SelectedSection = SettingsSection.PrivacyDiagnostics };
        var shell = CreateShell(studio, library, publish, settings);

        shell.NavigateCommand.Execute(ShellDestination.Studio);
        shell.NavigateCommand.Execute(ShellDestination.Library);
        shell.NavigateCommand.Execute(ShellDestination.Publish);
        shell.NavigateCommand.Execute(ShellDestination.Settings);
        TestAssert.Equal(SettingsSection.PrivacyDiagnostics, settings.SelectedSection, "Settings selection should persist.");
        TestAssert.Equal("Draft", publish.Title, "Publish draft should persist.");
        TestAssert.Equal(LibraryCategory.GeneratedClips, library.SelectedCategory, "Library selection should persist.");
        TestAssert.True(ReferenceEquals(studio, GetWorkspace(shell, ShellDestination.Studio)), "Studio instance should be persistent.");
        return Task.CompletedTask;
    }

    private static Task ShellRejectsUnknownDestination()
    {
        var shell = CreateShell();
        ShellDestination unknown = (ShellDestination)999;
        TestAssert.False(shell.NavigateCommand.CanExecute(unknown), "Unknown destinations must not be executable.");
        TestAssert.Throws<ArgumentException>(() => shell.NavigateCommand.Execute(unknown), "Unknown destinations should fail explicitly.");
        return Task.CompletedTask;
    }

    private static Task ImplicitTemplatesCoverAllWorkspaces()
    {
        RunOnSta(() =>
        {
            var app = EnsureApplication();
            foreach (Type viewModelType in new[]
                     {
                         typeof(GenerateViewModel), typeof(StudioViewModel), typeof(LibraryViewModel),
                         typeof(PublishViewModel), typeof(SettingsViewModel)
                     })
            {
                TestAssert.True(app.TryFindResource(new DataTemplateKey(viewModelType)) is DataTemplate, $"Implicit template missing for {viewModelType.Name}.");
            }
        });
        return Task.CompletedTask;
    }

    private static Task StudioSelectionProjectsEmptyState()
    {
        var studio = new StudioViewModel();
        studio.Inspector.SelectedInspector = StudioInspectorSection.Graphics;
        TestAssert.Equal("Graphics", studio.Inspector.SelectedInspectorTitle, "Graphics should have one home in the inspector.");
        TestAssert.Equal("No generated clips yet", studio.BrowserPreviewItems[0].Title, "Graphics must not replace the project clip browser.");
        return Task.CompletedTask;
    }

    private static Task StudioInspectorProjectsEmptyState()
    {
        var studio = new StudioViewModel();
        studio.Inspector.SelectedInspector = StudioInspectorSection.Metadata;
        TestAssert.Equal(
            "Title & description",
            studio.Inspector.SelectedInspectorTitle,
            "Inspector title should use the plain-language label for the selected section.");
        return Task.CompletedTask;
    }

    private static Task StudioCaptionControlTargetsActualContent()
    {
        var studio = new StudioViewModel(WorkspaceSurfaceState.ContentReady);
        TestAssert.True(
            studio.Preview.IsCaptionContentVisible,
            "Preview captions should start visible when a captioned clip is selected.");
        string xaml = File.ReadAllText(Path.Combine(
            RepositoryLayout.Root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Preview",
            "StudioPreviewView.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(
            RepositoryLayout.Root,
            "src",
            "ReplayFoundry.Desktop",
            "Features",
            "Studio",
            "Preview",
            "StudioPreviewView.xaml.cs"));
        TestAssert.True(
            xaml.Contains(
                "Command=\"{Binding ToggleCaptionVisibilityCommand}\"",
                StringComparison.Ordinal),
            "The CC button must show and hide the actual caption overlay.");
        TestAssert.False(
            xaml.Contains("Caption position", StringComparison.Ordinal) ||
            xaml.Contains("ToggleSafeAreaCommand", StringComparison.Ordinal),
            "The CC button must not create a second caption-position guide.");
        TestAssert.True(
            xaml.Contains("<local:StudioCaptionPreviewText", StringComparison.Ordinal) &&
            xaml.Contains(
                "Width=\"{Binding LiveCaptionMaximumWidthPixels}\"",
                StringComparison.Ordinal) &&
            xaml.Contains(
                "CaptionFontSize=\"{Binding LiveCaptionFontSizePixels}\"",
                StringComparison.Ordinal) &&
            xaml.Contains(
                "CaptionStyle=\"{Binding LiveCaptionStyle}\"",
                StringComparison.Ordinal) &&
            xaml.Contains(
                "AccentProgress=\"{Binding LiveCaptionAccentProgress}\"",
                StringComparison.Ordinal) &&
            xaml.Contains(
                "SweepLength=\"{Binding LiveCaptionSweepLength}\"",
                StringComparison.Ordinal) &&
            xaml.Contains(
                "CaptionScale=\"{Binding LiveCaptionScale}\"",
                StringComparison.Ordinal),
            "Studio must project the same caption width, type size, timed sweep, scale, and effect policy used by final rendering.");
        TestAssert.False(
            xaml.Contains(
                "Brush.StudioCaptionPanel",
                StringComparison.Ordinal) ||
            xaml.Contains(
                "Brush.StudioCaptionBorder",
                StringComparison.Ordinal),
            "The actual caption overlay must not be wrapped in a decorative guide box.");
        TestAssert.True(
            codeBehind.Contains(
                "new DispatcherTimer(",
                StringComparison.Ordinal) &&
            codeBehind.Contains(
                "DispatcherPriority.Normal",
                StringComparison.Ordinal) &&
            codeBehind.Contains(
                "PreviewPlayer.Position",
                StringComparison.Ordinal),
            "Timed caption effects must sample native media and its bounded UI-thread presentation clock.");
        TestAssert.True(
            codeBehind.Contains(
                "IsVisibleChanged += OnIsVisibleChanged",
                StringComparison.Ordinal) &&
            codeBehind.Contains(
                "retired.Source = null",
                StringComparison.Ordinal) &&
            codeBehind.Contains(
                "PreviewPlayerHost.Children.Remove(retired)",
                StringComparison.Ordinal),
            "Only the visible responsive Studio preview may own a native media graph; hidden preview instances must release their source to prevent doubled audio and competing position ticks.");
        return Task.CompletedTask;
    }

    private static Task StudioProjectCommandsStayDisabled()
    {
        var studio = new StudioViewModel();
        TestAssert.False(studio.Preview.PlayCommand.CanExecute(null), "Playback must remain disabled without a project.");
        TestAssert.False(studio.SelectBrowserAssetCommand.CanExecute("missing"), "Clip selection must remain disabled without a project.");
        return Task.CompletedTask;
    }

    private static Task StudioRenderReadinessIsActionable()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryRenderReadiness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var media = TestMediaFactory.Create(
                TestMediaFactory.CreateSourcePath("render-readiness.mkv"),
                TimeSpan.FromMinutes(4));
            var asset = new GenerationOutputAsset(
                "render-readiness",
                1,
                media,
                outputFullPath: null,
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(50),
                80,
                70,
                GenerationCandidateSelectionReason.QualityQualified,
                "test").WithDisposition(
                    GenerationOutputAssetDisposition.ExcludeFromFinalRender);
            var project = new GenerationOutputProject(
                "render-readiness-project",
                GenerationMode.IndividualClips,
                root,
                1,
                ClipFulfillmentPreference.QualityFirst,
                GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
                [asset],
                DateTimeOffset.UnixEpoch);
            var session = new GenerationOutputSession();
            session.Publish(project);
            using var studio = new StudioViewModel(
                session,
                session,
                new RecordingStudioClipRenderer());

            TestAssert.False(
                studio.FinalRender.AddToQueueCommand.CanExecute(null),
                "An excluded selected Browser clip should keep queueing disabled.");
            TestAssert.True(studio.FinalRender.NeedsIncludedCandidate,
                "The disabled state must name the selected-clip requirement.");
            TestAssert.True(studio.ReviewRenderRequirementsCommand.CanExecute(null),
                "The requirement should be actionable rather than hidden in a tooltip.");
            studio.ReviewRenderRequirementsCommand.Execute(null);
            TestAssert.Equal(StudioInspectorSection.Clip, studio.Inspector.SelectedInspector,
                "The action should focus the selected clip while keeping the clip browser available.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task StudioVisualEditingMapsUpdate()
    {
        var studio = new StudioViewModel();
        studio.Inspector.SelectedInspector = StudioInspectorSection.Graphics;
        TestAssert.Equal(1, studio.BrowserPreviewItems.Count, "The clip browser should retain its honest empty state.");
        TestAssert.Equal("No generated clips yet", studio.BrowserPreviewItems[0].Title, "Graphics uses the inspector while project clips remain available.");
        studio.Inspector.SelectedInspector = StudioInspectorSection.Audio;
        TestAssert.Equal(0, studio.Inspector.AudioStreams.Count, "Audio details require a selected real clip.");
        TestAssert.True(studio.Inspector.AudioMixSummary.Contains("Select", StringComparison.Ordinal), "The empty audio inspector should be explicit.");
        return Task.CompletedTask;
    }

    private static Task StudioOmitsStaticLayerDump()
    {
        var studio = new StudioViewModel(WorkspaceSurfaceState.ContentReady);
        TestAssert.Equal("No clip selected", studio.SelectedClipDurationText, "No aggregate project duration should masquerade as the selected clip duration.");
        string studioXaml = File.ReadAllText(
            Path.Combine(
                RepositoryLayout.Root,
                "src",
                "ReplayFoundry.Desktop",
                "Features",
                "Studio",
                "StudioView.xaml"));
        TestAssert.False(
            studioXaml.Contains("StudioTimelineView", StringComparison.Ordinal),
            "Studio should use its editable Inspector instead of a redundant static layer dump.");
        return Task.CompletedTask;
    }

    private static Task EmptyWorkspacesPreserveAnatomy()
    {
        TestAssert.False(new StudioViewModel().ShouldShowPlaceholder, "Empty Studio should show its editor anatomy.");
        TestAssert.False(new LibraryViewModel().ShouldShowPlaceholder, "Empty Library should show its browsing anatomy.");
        TestAssert.False(new PublishViewModel().ShouldShowPlaceholder, "Empty Publish should show its handoff anatomy.");
        TestAssert.True(new StudioViewModel(WorkspaceSurfaceState.Error).ShouldShowPlaceholder, "Studio errors should use the blocking issue surface.");
        TestAssert.True(new LibraryViewModel(WorkspaceSurfaceState.Unavailable).ShouldShowPlaceholder, "Unavailable Library should use the blocking issue surface.");
        TestAssert.True(new PublishViewModel(WorkspaceSurfaceState.Error).ShouldShowPlaceholder, "Publish errors should use the blocking issue surface.");
        return Task.CompletedTask;
    }

    private static Task ShellChromeProjectsActiveWorkspaceIdentity()
    {
        var shell = CreateShell();
        TestAssert.True(
            ReferenceEquals(shell.CurrentWorkspace, shell.CurrentWorkspaceChrome),
            "The title bar must project the same retained workspace instance as the content host.");
        TestAssert.Equal("Generate", shell.CurrentWorkspaceChrome.WorkspaceTitle, "Generate should own the initial chrome title.");

        shell.NavigateCommand.Execute(ShellDestination.Studio);
        TestAssert.Equal("Build the final cut", shell.CurrentWorkspaceChrome.WorkspaceTitle, "Studio should replace the shared chrome title.");
        TestAssert.True(
            shell.CurrentWorkspaceChrome.WorkspaceDescription.Contains("Library", StringComparison.OrdinalIgnoreCase),
            "The shared chrome should retain the active workspace description.");
        return Task.CompletedTask;
    }

    private static Task StudioProjectChromeIsCoherent()
    {
        var studio = new StudioViewModel(
            WorkspaceSurfaceState.ContentReady);
        TestAssert.Equal(
            "Add to queue",
            studio.FinalRender.ButtonText,
            "Studio should expose one concise final-output action.");
        TestAssert.Equal(
            "Studio is ready",
            studio.SaveStateText,
            "Studio should state its empty ready state plainly.");
        TestAssert.Equal(
            "Clip",
            studio.Inspector.SelectedInspectorTitle,
            "The Inspector should use the concise selected-section label.");
        TestAssert.True(
            studio.Inspector.InspectorSections.All(
                static item =>
                    !string.IsNullOrWhiteSpace(item.Description)),
            "Every Inspector navigation item should keep its supporting text inside the selection surface.");
        return Task.CompletedTask;
    }

    private static Task GeneratedOutputActivatesDownstreamWorkspaces()
    {
        var session = new GenerationOutputSession();
        using var catalog = new GenerationLibraryCatalog(
            session,
            new InMemoryLibraryCatalogStore());
        using var studio = new StudioViewModel(session);
        using var library = new LibraryViewModel(catalog);
        using var publish = new PublishViewModel(
            catalog,
            youtube: null,
            new InMemoryYouTubePublishPreferencesStore(),
            new TestThumbnailFilePicker());

        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryGeneratedOutputTests-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        var media = TestMediaFactory.Create(
            Path.Combine(outputDirectory, "source.mkv"),
            TimeSpan.FromMinutes(5));
        var editorialContext = new ClipEditorialContext(
            "generated-1",
            media.FullPath,
            "source",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(40),
            media.Duration,
            82,
            "Strong deterministic evidence.");
        var editorialMetadata = new ClipEditorialMetadataDraft(
            "Grounded test moment",
            "A grounded description for the selected interval.",
            ["source"],
            ClipEditorialMetadataOrigin.Heuristic,
            new ClipEditorialMetadataGeneratorIdentity(
                "Test generator",
                "1.0.0"),
            attempt: 0);
        var asset = new GenerationOutputAsset(
            "generated-1",
            1,
            media,
            outputFullPath: null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(40),
            82,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            "Strong deterministic evidence.",
            editorialContext: editorialContext,
            editorialMetadata: editorialMetadata);
        var project = new GenerationOutputProject(
            "project-test",
            GenerationMode.IndividualClips,
            outputDirectory,
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome
                .RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UnixEpoch);

        session.Publish(project);

        TestAssert.True(studio.HasProject, "Studio must activate from the shared handoff.");
        TestAssert.Equal(1, studio.BrowserPreviewItems.Count, "Studio must show the generated clip.");
        TestAssert.Equal(
            "Grounded test moment",
            studio.BrowserPreviewItems[0].Title,
            "Studio Browser cards must show generated editorial titles instead of replacing them with internal clip numbers.");
        TestAssert.Equal(
            "Pick 1 of 1",
            studio.BrowserPreviewItems[0].BatchRankText,
            "The stable clip rank should remain visible without overloading the media detail.");
        TestAssert.Equal(
            "1920 × 1080 · 60 FPS",
            studio.Preview.PreviewFormatText,
            "Studio must display the exact shared renderer profile.");
        TestAssert.False(library.IsContentReady, "Library must ignore an unrendered Studio draft.");
        TestAssert.Equal(0, library.Items.Count, "Library must not list draft clips.");
        TestAssert.False(publish.HasAsset, "Publish must ignore an unrendered Studio draft.");

        string renderedPath = Path.Combine(outputDirectory, "clip-01.mp4");
        File.WriteAllBytes(renderedPath, [0]);
        GenerationOutputProject finalized = project.Finalize(
            [asset.WithRenderedOutput(renderedPath)],
            DateTimeOffset.UnixEpoch.AddSeconds(1));
        session.FinalizeProject(finalized);

        TestAssert.True(library.IsContentReady, "Library must activate after Studio finalization.");
        TestAssert.Equal(1, library.Items.Count, "Library must list the finalized clip.");
        TestAssert.Equal(
            "Grounded test moment",
            library.Items[0].Title,
            "Library must show the same title as Studio after the render completes.");
        TestAssert.Equal(
            "Grounded test moment",
            catalog.Assets.Single().Title,
            "The finalized Library record must retain the independent publish title.");
        TestAssert.Equal(
            "16:9",
            library.Items[0].AspectRatio,
            "Library must display the finalized output aspect instead of a placeholder label.");
        TestAssert.True(publish.HasAsset, "Publish must receive the finalized asset.");
        TestAssert.True(
            publish.AssetTitle.Contains("clip-01", StringComparison.Ordinal),
            "Publish must project the real generated filename.");

        library.SelectedCategory = LibraryCategory.Montages;
        TestAssert.Equal(
            0,
            library.Items.Count,
            "The Montage category must filter out finalized individual clips.");
        library.SelectedCategory = LibraryCategory.Projects;
        library.SearchQuery = "not-present";
        TestAssert.Equal(
            0,
            library.Items.Count,
            "Library search must filter the durable catalog rather than only changing its label.");
        library.ClearFiltersCommand.Execute(null);
        TestAssert.Equal(
            1,
            library.Items.Count,
            "Clearing filters must restore the durable finalized asset.");

        session.Clear();
        TestAssert.False(studio.HasProject, "Clearing the handoff must return Studio to empty.");
        TestAssert.Equal(
            1,
            library.Items.Count,
            "Clearing the transient handoff must retain finalized Library history.");
        TestAssert.True(
            publish.HasAsset,
            "Publish must continue to expose finalized Library history after the transient handoff is cleared.");

        Directory.Delete(outputDirectory, recursive: true);
        return Task.CompletedTask;
    }

    private static Task StudioAppliesBoundaryDraft()
    {
        var session = new GenerationOutputSession();
        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryStudioEditTests");
        var asset = new GenerationOutputAsset(
            "studio-edit-1",
            1,
            TestMediaFactory.Create(
                TestMediaFactory.CreateSourcePath("studio-edit.mkv"),
                TimeSpan.FromMinutes(8),
                hasAudio: true,
                audioStreamCount: 2),
            outputFullPath: null,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(3),
            88,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            "test");
        var project = new GenerationOutputProject(
            "studio-edit-project",
            GenerationMode.IndividualClips,
            outputDirectory,
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome
                .RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UnixEpoch);
        session.Publish(project);
        var renderer = new RecordingStudioClipRenderer();
        using var studio = new StudioViewModel(
            session,
            session,
            renderer);
        TimeSpan generatedStart =
            studio.Inspector.Clip.DraftSourceStart;
        TestAssert.True(
            studio.Inspector.Clip.NudgeStartEarlierCommand.CanExecute(null),
            "A mutable Studio clip must expose frame-accurate In-point nudging.");
        studio.Inspector.Clip.NudgeStartEarlierCommand.Execute(null);
        TestAssert.True(
            studio.Inspector.Clip.DraftSourceStart < generatedStart,
            "The earlier-frame control must move the draft In point earlier.");
        TestAssert.True(
            studio.Inspector.Clip.StartAdjustmentSummary.Contains(
                "frame earlier",
                StringComparison.Ordinal),
            "The trim UI must explain its precise frame offset.");
        studio.Inspector.Clip.ResetBoundaryDraftCommand.Execute(null);
        studio.Inspector.Clip.StartAdjustmentSeconds = -15;
        studio.Inspector.Clip.EndAdjustmentSeconds = 20;

        studio.Inspector.Clip.ApplyBoundaryEditCommand.Execute(null);

        TestAssert.Equal(0, renderer.CallCount, "Saving a Studio edit must not render media.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(105),
            session.Current!.PrimaryAsset.SourceStart,
            "The earlier start must be published after render success.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(200),
            session.Current.PrimaryAsset.SourceEnd,
            "The later end must be published after render success.");
        TestAssert.Equal(
            2,
            session.Current.PrimaryAsset.SourceMedia.AudioStreams.Count,
            "Boundary editing must preserve the full source-audio inventory.");
        TestAssert.True(
            ReferenceEquals(
                session.Current.PrimaryAsset,
                studio.Inspector.SelectedAsset),
            "Studio must rebind the selected asset after the atomic project replacement.");
        TestAssert.Equal(
            -15d,
            studio.Inspector.Clip.StartAdjustmentSeconds,
            "The rebound Studio draft must preserve the applied start adjustment.");
        TestAssert.Equal(
            20d,
            studio.Inspector.Clip.EndAdjustmentSeconds,
            "The rebound Studio draft must preserve the applied end adjustment.");
        TestAssert.Equal(
            session.Current.PrimaryAsset.DisplayName,
            session.Current.PrimaryAsset.ToString(),
            "Studio clip choices must render a user-facing label even when a control ignores DisplayMemberPath.");

        return Task.CompletedTask;
    }

    private static async Task StudioFinalRenderSnapshotsVisibleEdits()
    {
        var session = new GenerationOutputSession();
        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryStudioFinalizeTests-" +
            Guid.NewGuid().ToString("N"));
        var sourceMedia = TestMediaFactory.Create(
            TestMediaFactory.CreateSourcePath("studio-final.mkv"),
            TimeSpan.FromMinutes(8),
            hasAudio: true);
        var editorialContext = new ClipEditorialContext(
            "studio-final-1",
            sourceMedia.FullPath,
            "ExampleGame",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(3),
            sourceMedia.Duration,
            88,
            "test",
            transcripts: [],
            evidence: [],
            gameContext: new ClipEditorialGameContext(
                "Example Game",
                "#ExampleGame",
                contextNotes: null,
                ClipEditorialGameContextSource.UserConfirmed));
        var editorialMetadata = new ClipEditorialMetadataDraft(
            "Choosing my next route #ExampleGame",
            "I compare the available paths and choose where to go next.",
            ["ExampleGame", "route choice"],
            ClipEditorialMetadataOrigin.UserEdited,
            new ClipEditorialMetadataGeneratorIdentity("Test editor", "1.0"),
            attempt: 0,
            readiness: ClipEditorialMetadataReadiness.UserApproved);
        var asset = new GenerationOutputAsset(
            "studio-final-1",
            1,
            sourceMedia,
            outputFullPath: null,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(3),
            88,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            "test",
            editorialContext: editorialContext,
            editorialMetadata: editorialMetadata);
        var project = new GenerationOutputProject(
            "studio-final-project",
            GenerationMode.IndividualClips,
            outputDirectory,
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome
                .RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UnixEpoch);
        session.Publish(project);
        var renderer = new RecordingStudioClipRenderer();
        using var studio = new StudioViewModel(
            session,
            session,
            renderer);
        studio.Inspector.Clip.StartAdjustmentSeconds = -12;
        studio.Inspector.Clip.EndAdjustmentSeconds = 18;
        studio.Inspector.Clip.SelectedVideoEffect =
            studio.Inspector.Clip.VideoEffectOptions.Single(
            option => option.Value == StudioVideoEffectPreset.Noir);
        studio.Inspector.Clip.VideoEffectIntensityPercent = 64;

        studio.Inspector.Clip.ApplyBoundaryEditCommand.Execute(null);
        studio.Inspector.Editorial.SaveCommand.Execute(null);
        studio.FinalRender.AddToQueueCommand.Execute(null);

        await studio.FinalRender.FinalizeProjectAsync();

        TestAssert.Equal(1, renderer.CallCount, "Studio must run one project finalization.");
        TestAssert.False(
            session.Current!.IsFinalized,
            "Successful rendering must preserve the editable Studio draft after committing a separate Library copy.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(108),
            renderer.LastDraft!.PrimaryAsset.SourceStart,
            "Finalization must snapshot the visible earlier start adjustment.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(198),
            renderer.LastDraft.PrimaryAsset.SourceEnd,
            "Finalization must snapshot the visible later end adjustment.");
        TestAssert.Equal(
            StudioVideoEffectPreset.Noir,
            renderer.LastDraft.PrimaryAsset.Appearance.VideoEffect,
            "Finalization must snapshot the visible video treatment.");
        TestAssert.Equal(
            64d,
            renderer.LastDraft.PrimaryAsset.Appearance
                .VideoEffectIntensityPercent,
            "Finalization must snapshot treatment intensity.");
    }

    private static Task StudioReadOnlyRenderProgressBindsOneWay()
    {
        string repositoryRoot = RepositoryLayout.Root;
        string studioView = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "ReplayFoundry.Desktop",
                "Features",
                "Studio",
                "StudioView.xaml"));
        int automationIndex = studioView.IndexOf(
            "AutomationProperties.Name=\"Final render progress\"",
            StringComparison.Ordinal);
        TestAssert.True(
            automationIndex >= 0,
            "Studio must retain a named final-render progress surface.");
        int progressStart = studioView.LastIndexOf(
            "<ProgressBar",
            automationIndex,
            StringComparison.Ordinal);
        int progressEnd = studioView.IndexOf(
            "/>",
            automationIndex,
            StringComparison.Ordinal);
        TestAssert.True(
            progressStart >= 0 && progressEnd > automationIndex,
            "The named final-render progress surface must remain a self-contained ProgressBar.");
        string finalRenderProgress = studioView[
            progressStart..(progressEnd + 2)];

        TestAssert.True(
            finalRenderProgress.Contains(
                "Value=\"{Binding Percent, Mode=OneWay}\"",
                StringComparison.Ordinal),
            "A read-only Studio progress property must bind OneWay; the WPF ProgressBar default is TwoWay and crashes during view activation.");
        TestAssert.True(
            finalRenderProgress.Contains(
                "IsIndeterminate=\"{Binding IsRendering, Mode=OneWay}\"",
                StringComparison.Ordinal),
            "Studio rendering must activate the shared indeterminate motion while FFmpeg is working between coarse clip-count updates.");
        TestAssert.True(
            finalRenderProgress.Contains(
                "Style=\"{DynamicResource Control.ProgressBar.Standard}\"",
                StringComparison.Ordinal),
            "Studio rendering must use the shared standard progress treatment instead of defining a one-off loading bar.");

        return Task.CompletedTask;
    }

    private static Task StudioHiddenMomentsVisibilityUsesChildDataContext()
    {
        string repositoryRoot = RepositoryLayout.Root;
        string studioView = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "ReplayFoundry.Desktop",
                "Features",
                "Studio",
                "StudioView.xaml"));

        TestAssert.True(
            studioView.Contains(
                "DataContext=\"{Binding HiddenMoments}\"",
                StringComparison.Ordinal),
            "The Hidden Moments overlay must use the focused child view model.");
        TestAssert.True(
            studioView.Contains(
                "Visibility=\"{Binding IsOpen, Converter={StaticResource BoolToVisibility}}\"",
                StringComparison.Ordinal),
            "Once the overlay replaces its DataContext, visibility must bind directly to IsOpen.");
        TestAssert.False(
            studioView.Contains(
                "Visibility=\"{Binding HiddenMoments.IsOpen, Converter={StaticResource BoolToVisibility}}\"",
                StringComparison.Ordinal),
            "The overlay must not resolve the parent path against its child DataContext and appear permanently open.");

        return Task.CompletedTask;
    }

    private static Task StudioPreviewDefersInitialSeek()
    {
        string repositoryRoot = RepositoryLayout.Root;
        string previewCode = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "ReplayFoundry.Desktop",
                "Features",
                "Studio",
                "Preview",
                "StudioPreviewView.xaml.cs"));

        TestAssert.True(
            previewCode.Contains(
                "Dispatcher.BeginInvoke",
                StringComparison.Ordinal) &&
            previewCode.Contains(
                "DispatcherPriority.Background",
                StringComparison.Ordinal),
            "MediaElement can ignore a synchronous seek during MediaOpened; the bounded preview must defer the initial candidate-relative seek until the control finishes opening.");
        TestAssert.True(
            previewCode.Contains(
                "BeginPausedSeekPrime(proxyPosition)",
                StringComparison.Ordinal) &&
            previewCode.Contains(
                "player.IsMuted = true",
                StringComparison.Ordinal) &&
            previewCode.Contains(
                "player.Position = TimeSpan.FromSeconds(proxyPosition)",
                StringComparison.Ordinal) &&
            previewCode.Contains(
                "ReferenceEquals(player, PreviewPlayer)",
                StringComparison.Ordinal),
            "A paused seek must prime the current native graph before Studio declares its trim-context offset synchronized.");
        return Task.CompletedTask;
    }

}
