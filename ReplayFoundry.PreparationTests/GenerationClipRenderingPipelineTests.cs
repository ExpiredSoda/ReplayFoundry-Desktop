using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.Progress;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static async Task CaptionRenderingUsesOwnedScripts()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            audioStreamCount: 2,
            captionsEnabled: true);
        GenerationCandidateCaptionTrack[] tracks =
            fixture.Moments.SelectedCandidates
                .Select(
                    candidate =>
                        new GenerationCandidateCaptionTrack(
                            candidate,
                            fixture.GenerationRequest.SetupOptions
                                .CaptionSettings.FindForSource(
                                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
                            GenerationCaptionStylePreset.KaraokeSweep,
                            CreateTranscription(candidate)))
                .ToArray();
        var captions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            tracks,
            TimeSpan.Zero);
        var runner = new WritingProcessRunner();

        GenerationOutputProject draft = fixture.CreateDraft(captions);
        StudioProjectRenderResult result =
            await fixture.CreateStudioRenderer(runner).FinalizeAsync(
                draft,
                new RecordingProgress<StudioProjectRenderProgress>(),
                CancellationToken.None);

        TestAssert.Equal(
            captions.Tracks[0].CandidateId,
            result.FinalizedProject.PrimaryAsset.Captions!.CandidateId,
            "The completed render must retain its caption identity.");
        TestAssert.Equal(
            captions.Tracks[0].Segments[0].Text,
            result.FinalizedProject.PrimaryAsset.Captions!.Segments[0].Text,
            "The completed render must retain caption text while releasing the Generate analysis graph.");
        TestAssert.True(
            runner.Requests
                .Where(request =>
                    request.Arguments[^1].EndsWith(
                        ".mp4",
                        StringComparison.OrdinalIgnoreCase))
                .All(
                request =>
                    request.Arguments.Contains("-vf") &&
                    request.Arguments.Any(
                        argument =>
                            argument.Contains(
                                "ass=filename=",
                                StringComparison.Ordinal))),
            "Each clip render must burn its owned ASS script.");
        TestAssert.False(
            Directory.EnumerateDirectories(
                result.FinalizedProject.OutputDirectory,
                ".caption-work",
                SearchOption.TopDirectoryOnly).Any(),
            "Run-owned caption scripts must not remain in the published output.");
    }

    private static Task StudioReplacementPreservesProject()
    {
        string source = TestMediaFactory.CreateSourcePath(
            "studio-replacement.mkv");
        string root = CreateRoot();
        try
        {
            var original = new GenerationOutputAsset(
                "studio-2",
                1,
                TestMediaFactory.Create(
                    source,
                    TimeSpan.FromMinutes(10),
                    hasAudio: true),
                outputFullPath: null,
                TimeSpan.FromMinutes(3),
                TimeSpan.FromMinutes(4),
                90,
                70,
                GenerationCandidateSelectionReason.QualityQualified,
                "test");
            var project = new GenerationOutputProject(
                "project-studio",
                GenerationMode.IndividualClips,
                root,
                1,
                ClipFulfillmentPreference.FillRequestedCount,
                GenerationClipFulfillmentOutcome
                    .RequestedCountMetAtQualityTarget,
                [original],
                DateTimeOffset.UnixEpoch);
            var session = new GenerationOutputSession();
            session.Publish(project);
            GenerationOutputAsset replacement =
                original.WithStudioEdits(
                    TimeSpan.FromSeconds(150),
                    TimeSpan.FromSeconds(250),
                    original.Appearance);
            session.ReplaceAsset(project.Id, replacement);

            TestAssert.Equal(
                project.Id,
                session.Current!.Id,
                "Studio replacement must preserve the project identity.");
            TestAssert.Equal(
                original.OriginalSourceStart,
                session.Current.PrimaryAsset.OriginalSourceStart,
                "The generated boundary remains the one-minute reference point.");
            TestAssert.Equal(
                replacement.SourceStart,
                session.Current.PrimaryAsset.SourceStart,
                "The current edit must be rebound into the shared session.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static Task ThumbnailCommandIsBounded()
    {
        string root = CreateRoot();
        try
        {
            string input = Path.Combine(root, "rendered clip.mp4");
            string output = Path.Combine(root, "rendered clip.thumbnail.jpg");
            FfmpegClipRenderCommand command =
                FfmpegClipRenderCommandBuilder.BuildThumbnail(
                    input,
                    TimeSpan.FromSeconds(30),
                    output);

            TestAssert.True(
                ContainsPair(command.Arguments, "-ss", "10"),
                "The thumbnail should sample one-third into the rendered clip.");
            TestAssert.True(
                ContainsPair(command.Arguments, "-frames:v", "1"),
                "Only one frame may be decoded into the thumbnail artifact.");
            TestAssert.True(
                command.Arguments.Contains(
                    "scale=640:640:force_original_aspect_ratio=decrease"),
                "Thumbnail dimensions must remain bounded without distorting the render.");
            TestAssert.Equal(input, command.Arguments[8], "The rendered path remains one argument.");
            TestAssert.Equal(output, command.Arguments[^1], "The JPEG output remains one argument.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static Task ConcatenationCopiesStreams()
    {
        string root = CreateRoot();
        string list = Path.Combine(root, "concat.txt");
        File.WriteAllText(list, "file 'one.mp4'");
        FfmpegClipRenderCommand command =
            FfmpegClipRenderCommandBuilder.BuildConcatenation(
                list,
                Path.Combine(root, "montage.mp4"),
                TimeSpan.FromSeconds(30));

        TestAssert.True(
            ContainsPair(command.Arguments, "-c", "copy"),
            "Normalized segments should be joined without another encode.");
        TestAssert.Equal(1, Count(command.Arguments, "-i"), "One concat input.");
        Directory.Delete(root, recursive: true);
        return Task.CompletedTask;
    }

    private static async Task IndividualRenderingCommitsAtomically()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            scoreSets: [[95, 90]]);
        var runner = new WritingProcessRunner();
        StudioProjectRenderResult result =
            await fixture.CreateStudioRenderer(runner).FinalizeAsync(
                fixture.CreateDraft(),
                new RecordingProgress<StudioProjectRenderProgress>(),
                CancellationToken.None);

        TestAssert.Equal(4, runner.Requests.Count, "One render and one thumbnail process per clip.");
        TestAssert.Equal(2, result.FinalizedProject.Assets.Count, "One file per selected moment.");
        TestAssert.True(
            result.FinalizedProject.Assets.All(
                artifact =>
                    File.Exists(artifact.OutputFullPath!)),
            "Committed artifacts must exist.");
        TestAssert.True(
            result.FinalizedProject.Assets.All(
                artifact =>
                    artifact.HasThumbnail &&
                    File.Exists(artifact.ThumbnailFullPath!)),
            "Each committed clip must retain its generated Library thumbnail.");
        AssertNoStaging(fixture.Root);
    }

    private static Task IndividualOutputFilenameUsesSavedTitle()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            scoreSets: [[95]]);
        GenerationOutputAsset sourceAsset = fixture.CreateDraft().PrimaryAsset;
        var metadata = new ClipEditorialMetadataDraft(
                    "Found the hidden route #ExampleGame",
                    "A saved audience description.",
                    ["ExampleGame"],
                    ClipEditorialMetadataOrigin.UserEdited,
                    new ClipEditorialMetadataGeneratorIdentity(
                        "filename test",
                        "1.0.0"),
                    attempt: 0,
                    readiness:
                        ClipEditorialMetadataReadiness.UserApproved);
        var context = new ClipEditorialContext(
            sourceAsset.Id,
            sourceAsset.SourceMedia.FullPath,
            Path.GetFileNameWithoutExtension(
                sourceAsset.SourceMedia.FullPath),
            sourceAsset.SourceStart,
            sourceAsset.SourceEnd,
            sourceAsset.SourceMedia.Duration,
            sourceAsset.Score,
            sourceAsset.Explanation);
        var asset = new GenerationOutputAsset(
            sourceAsset.Id,
            sourceAsset.Rank,
            sourceAsset.SourceMedia,
            null,
            sourceAsset.SourceStart,
            sourceAsset.SourceEnd,
            sourceAsset.Score,
            sourceAsset.QualityTarget,
            sourceAsset.SelectionReason,
            sourceAsset.Explanation,
            sourceAsset.Captions,
            sourceAsset.Appearance,
            context,
            metadata,
            sourceAsset.PreferenceFeatures,
            null,
            sourceAsset.Disposition);

        string fileName =
            FfmpegStudioProjectRenderingService.BuildOutputFileName(asset);

        TestAssert.Equal(
            "001-Found the hidden route #ExampleGame.mp4",
            fileName,
            "The exported Windows file should carry the exact saved audience title instead of an opaque source/candidate identifier.");
        return Task.CompletedTask;
    }

    private static async Task CompletedRenderDiscardEnablesRetry()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            scoreSets: [[95]]);
        var runner = new WritingProcessRunner();
        FfmpegStudioProjectRenderingService renderer =
            fixture.CreateStudioRenderer(runner);
        GenerationOutputProject draft = fixture.CreateDraft();

        StudioProjectRenderResult first = await renderer.FinalizeAsync(
            draft,
            new RecordingProgress<StudioProjectRenderProgress>(),
            CancellationToken.None);
        TestAssert.True(
            Directory.Exists(fixture.FinalDirectory),
            "The completed render should own one committed output directory.");

        renderer.DiscardCompletedRender(first);
        TestAssert.False(
            Directory.Exists(fixture.FinalDirectory),
            "Discarding a stale completed render must remove its owned directory.");

        StudioProjectRenderResult retry = await renderer.FinalizeAsync(
            draft,
            new RecordingProgress<StudioProjectRenderProgress>(),
            CancellationToken.None);
        TestAssert.True(
            retry.FinalizedProject.IsFinalized &&
            Directory.Exists(fixture.FinalDirectory),
            "A clean retry must succeed at the same project output path.");
        AssertNoStaging(fixture.Root);
    }

    private static async Task CompletedRenderAcceptReleasesOwnership()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            scoreSets: [[95]]);
        var runner = new WritingProcessRunner();
        FfmpegStudioProjectRenderingService renderer =
            fixture.CreateStudioRenderer(runner);

        StudioProjectRenderResult result = await renderer.FinalizeAsync(
            fixture.CreateDraft(),
            new RecordingProgress<StudioProjectRenderProgress>(),
            CancellationToken.None);
        TestAssert.Equal(
            1,
            renderer.CompletedRenderOwnerCount,
            "A returned completed render must remain owned until its caller commits or discards it.");

        renderer.AcceptCompletedRender(result);

        TestAssert.Equal(
            0,
            renderer.CompletedRenderOwnerCount,
            "A Library-accepted render must not remain in the process-lifetime owner registry.");
        TestAssert.True(
            Directory.Exists(fixture.FinalDirectory) &&
            result.FinalizedProject.Assets.All(asset =>
                File.Exists(asset.OutputFullPath!)),
            "Accepting ownership must preserve every Library output file.");
        TestAssert.Throws<InvalidOperationException>(
            () => renderer.DiscardCompletedRender(result),
            "A terminally accepted render cannot later be deleted through the renderer.");
        AssertNoStaging(fixture.Root);
    }

    private static async Task MontageRendersOncePerSegment()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.Montage,
            scoreSets: [[95, 90]]);
        var runner = new WritingProcessRunner();
        StudioProjectRenderResult result =
            await fixture.CreateStudioRenderer(runner).FinalizeAsync(
                fixture.CreateDraft(),
                new RecordingProgress<StudioProjectRenderProgress>(),
                CancellationToken.None);

        TestAssert.Equal(4, runner.Requests.Count, "Two encodes, one join, and one thumbnail.");
        TestAssert.True(
            result.FinalizedProject.Assets.All(
                asset => asset.OutputFullPath ==
                    result.FinalizedProject.PrimaryAsset.OutputFullPath),
            "Every montage selection must bind to the one final montage output.");
        TestAssert.True(
            result.FinalizedProject.Assets.All(
                asset => asset.ThumbnailFullPath ==
                    result.FinalizedProject.PrimaryAsset.ThumbnailFullPath &&
                    asset.HasThumbnail),
            "Every montage selection must bind to the same final thumbnail.");
        TestAssert.True(
            ContainsPair(runner.Requests[^2].Arguments, "-c", "copy"),
            "The final join must use stream copy.");
        TestAssert.False(
            Directory.Exists(
                Path.Combine(result.FinalizedProject.OutputDirectory, ".segments")),
            "Intermediate segments must not be published.");
        AssertNoStaging(fixture.Root);
    }

    private static async Task FailureRemovesStaging()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips);
        var runner = new WritingProcessRunner(failOnCall: 1);

        await TestAssert.ThrowsAsync<InvalidOperationException>(
            () => fixture.CreateStudioRenderer(runner).FinalizeAsync(
                fixture.CreateDraft(),
                new RecordingProgress<StudioProjectRenderProgress>(),
                CancellationToken.None),
            "A failed encode must fail the render batch.");
        TestAssert.False(
            Directory.Exists(fixture.FinalDirectory),
            "A partial final directory must never be visible.");
        AssertNoStaging(fixture.Root);
    }

    private static async Task CancellationRemovesStaging()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips);
        using var cancellation = new CancellationTokenSource();
        var runner = new WritingProcessRunner(
            cancelOnCall: 1,
            cancellation: cancellation);

        await TestAssert.ThrowsAsync<OperationCanceledException>(
            () => fixture.CreateStudioRenderer(runner).FinalizeAsync(
                fixture.CreateDraft(),
                new RecordingProgress<StudioProjectRenderProgress>(),
                cancellation.Token),
            "Active rendering cancellation must propagate.");
        TestAssert.False(
            Directory.Exists(fixture.FinalDirectory),
            "Cancellation must not publish a partial batch.");
        AssertNoStaging(fixture.Root);
    }

    private static async Task PipelineFindsThenRenders()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips);
        var pipeline = new GenerationPipelineRunner(
            new GenerationPreflightRunner(),
            fixture.MomentService,
            new FixedOutputPathProvider(fixture.FinalDirectory));
        var progress = new RecordingProgress<GenerationProgressUpdate>();

        GenerationResult result = await pipeline.RunAsync(
            fixture.GenerationRequest,
            progress,
            CancellationToken.None);

        TestAssert.Equal(1, result.Candidates.Count, "Generated candidate.");
        TestAssert.Equal(
            0,
            result.OutputFileCount,
            "Generate must not encode a final clip before Studio approval.");
        TestAssert.True(
            progress.Values.Any(
                update => update.Title == "Finding the moments worth keeping"),
            "Moment-finding progress.");
        TestAssert.Equal(
            "Opening your Studio project",
            progress.Values[^1].Title,
            "Generate completion must hand an editable draft to Studio.");

        TestAssert.False(
            result.Moments.IsRequestedCountMet,
            "The focused fixture should exercise a safe-candidate shortfall.");
        var completion = new GenerationProgressViewModel(
            static () => { },
            static () => { });
        completion.Complete(result);
        TestAssert.True(
            completion.CompletionSummary!.Contains(
                result.Moments.FulfillmentMessage,
                StringComparison.Ordinal),
                "Completion must explain why fewer editable moments were selected than requested.");
    }

    private static async Task PipelineRejectsNoMoments()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            scoreSets: [[]]);
        var pipeline = new GenerationPipelineRunner(
            new GenerationPreflightRunner(),
            fixture.MomentService,
            new FixedOutputPathProvider(fixture.FinalDirectory));

        await TestAssert.ThrowsAsync<GenerationSourceException>(
            () => pipeline.RunAsync(
                fixture.GenerationRequest,
                new RecordingProgress<GenerationProgressUpdate>(),
                CancellationToken.None),
            "An empty deterministic selection must be actionable.");
    }

    private static async Task PipelinePublishesWorkspaceHandoff()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            scoreSets: [[95, 90]]);
        var session = new GenerationOutputSession();
        var pipeline = new GenerationPipelineRunner(
            new GenerationPreflightRunner(),
            fixture.MomentService,
            new FixedOutputPathProvider(fixture.FinalDirectory),
            session);

        GenerationResult result = await pipeline.RunAsync(
            fixture.GenerationRequest,
            new RecordingProgress<GenerationProgressUpdate>(),
            CancellationToken.None);

        TestAssert.True(
            session.Current is not null,
            "A completed selection must publish one Studio draft handoff.");
        TestAssert.Equal(
            result.CandidateCount,
            session.Current!.SelectedCount,
            "The handoff must preserve every selected candidate.");
        TestAssert.True(
            session.Current.Assets.All(static asset => !asset.IsRendered),
            "Generate must publish only unrendered Studio assets.");
    }

}
