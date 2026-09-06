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
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static Task RepeatedGenerationResultsHaveDistinctProjectIdentity()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            audioStreamCount: 1,
            sourceName: "same-result-rerun.mkv");
        GenerationOutputProject first = fixture.CreateDraft();
        GenerationOutputProject second = fixture.CreateDraft();

        TestAssert.False(first.Id.Equals(second.Id, StringComparison.Ordinal),
            "Every Generate run needs a distinct Studio project identity even when candidates and output input are identical.");
        TestAssert.Equal(
            first.CandidateSetFingerprint,
            second.CandidateSetFingerprint,
            "The separate deterministic candidate-set fingerprint should remain stable across identical reruns.");
        TestAssert.True(
            first.Assets.Select(static asset => asset.Id).SequenceEqual(
                second.Assets.Select(static asset => asset.Id)),
            "Per-run project identity must not rewrite stable candidate identities.");

        var session = new GenerationOutputSession();
        using var catalog =
            new ReplayFoundry.Desktop.Features.Generate.RecentProjects.RecentGenerationProjectCatalog(
                session,
                new ReplayFoundry.Desktop.Features.Generate.RecentProjects.JsonRecentGenerationProjectStore(
                    Path.Combine(fixture.Root, "recent-reruns.json")));
        session.Publish(first);
        session.Publish(second);
        TestAssert.Equal(2, catalog.Projects.Count,
            "The recent-project cache must retain identical-result reruns as two real sessions.");
        return Task.CompletedTask;
    }

    private static Task ProfileIsBounded()
    {
        var landscape = GenerationClipOutputProfile.FromReference(
            TestMediaFactory.Create(
                TestMediaFactory.CreateSourcePath("landscape.mkv"),
                width: 3840,
                height: 2160).PrimaryVideoStream);
        var portrait = GenerationClipOutputProfile.FromReference(
            TestMediaFactory.Create(
                TestMediaFactory.CreateSourcePath("portrait.mkv"),
                width: 1080,
                height: 1920).PrimaryVideoStream);

        TestAssert.Equal(1920, landscape.Width, "Landscape width.");
        TestAssert.Equal(1080, landscape.Height, "Landscape height.");
        TestAssert.Equal(1080, portrait.Width, "Portrait width.");
        TestAssert.Equal(1920, portrait.Height, "Portrait height.");
        TestAssert.Equal(60, landscape.FramesPerSecond, "Frame-rate policy.");
        TestAssert.Equal(
            "1920 × 1080 · 60 FPS",
            landscape.DisplayText,
            "The shared profile must expose its exact renderer format.");
        return Task.CompletedTask;
    }

    private static Task StudioPreviewContextCoversEdits()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            audioStreamCount: 2,
            sourceName: "preview-context.mkv");
        GenerationOutputAsset asset = fixture.CreateDraft().PrimaryAsset;
        var request = new StudioPreviewMediaRequest(asset);

        TestAssert.Equal(
            asset.OriginalSourceStart > TimeSpan.FromMinutes(1)
                ? asset.OriginalSourceStart - TimeSpan.FromMinutes(1)
                : TimeSpan.Zero,
            request.SourceStart,
            "Preview context start.");
        TestAssert.Equal(
            asset.OriginalSourceEnd + TimeSpan.FromMinutes(1) <
                asset.SourceDuration
                ? asset.OriginalSourceEnd + TimeSpan.FromMinutes(1)
                : asset.SourceDuration,
            request.SourceEnd,
            "Preview context end.");
        TestAssert.True(
            StudioClipBoundaryPolicy.IsValid(
                asset,
                request.SourceStart,
                request.SourceEnd),
            "The preview must cover every valid trim boundary.");
        return Task.CompletedTask;
    }

    private static Task StudioPreviewCacheSeparatesLiveCaptionStyling()
    {
        string source = TestMediaFactory.CreateExistingSourcePath(
            "studio-preview-cache.mkv");
        var asset = new GenerationOutputAsset(
            "preview-cache",
            1,
            TestMediaFactory.Create(source, TimeSpan.FromMinutes(4)),
            outputFullPath: null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            90,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            "test");
        StudioPreviewCacheKey baseline = StudioPreviewCacheKey.Create(
            new StudioPreviewMediaRequest(asset));
        GenerationOutputAsset captionOnly = asset.WithStudioEdits(
            asset.SourceStart,
            asset.SourceEnd,
            new StudioClipAppearance(
                GenerationCaptionStylePreset.Pop,
                22,
                StudioVideoEffectPreset.None,
                0));
        StudioPreviewCacheKey captionKey = StudioPreviewCacheKey.Create(
            new StudioPreviewMediaRequest(captionOnly));
        GenerationOutputAsset effect = asset.WithStudioEdits(
            asset.SourceStart,
            asset.SourceEnd,
            new StudioClipAppearance(
                GenerationCaptionStylePreset.Clean,
                StudioClipAppearance.DefaultCaptionVerticalPositionPercent,
                StudioVideoEffectPreset.Noir,
                60));
        StudioPreviewCacheKey effectKey = StudioPreviewCacheKey.Create(
            new StudioPreviewMediaRequest(effect));

        TestAssert.Equal(
            "1.3",
            StudioPreviewCacheKey.PolicyVersion,
            "Current frame-clock handling must invalidate previews rendered by the prior cache policy.");
        TestAssert.Equal(
            baseline.Hash,
            captionKey.Hash,
            "Caption style and position are rendered live and must reuse the proxy.");
        TestAssert.False(
            baseline.Hash.Equals(effectKey.Hash, StringComparison.Ordinal),
            "A video treatment changes the proxy and must invalidate the cache.");
        return Task.CompletedTask;
    }

    private static Task StudioCaptionToggleHidesActualOverlay()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep,
            CreateTranscription(candidate));
        var captions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            [track],
            TimeSpan.Zero);
        GenerationOutputProject project = fixture.CreateDraft(captions);
        using var preview = new StudioPreviewViewModel(mediaService: null);
        preview.Bind(hasProject: true, project, project.PrimaryAsset);
        preview.PreviewPositionSeconds =
            project.PrimaryAsset.SourceStart.TotalSeconds + 1.2;

        TestAssert.True(
            preview.HasLiveCaption,
            "The timed caption should be visible before using the CC toggle.");
        TestAssert.True(
            preview.ToggleCaptionVisibilityCommand.CanExecute(null),
            "A captioned Studio clip should enable the CC toggle.");
        preview.ToggleCaptionVisibilityCommand.Execute(null);
        TestAssert.False(
            preview.IsCaptionContentVisible,
            "The CC toggle should hide actual caption content.");
        TestAssert.Equal(
            "CC OFF",
            preview.CaptionVisibilityShortText,
            "The transport must make hidden caption state visible without relying on icon color alone.");
        TestAssert.False(
            preview.HasLiveCaption,
            "Hidden caption content must collapse the live overlay.");
        preview.Bind(hasProject: false, project: null, asset: null);
        preview.Bind(hasProject: true, project, project.PrimaryAsset);
        preview.PreviewPositionSeconds =
            project.PrimaryAsset.SourceStart.TotalSeconds + 1.2;
        TestAssert.True(
            preview.IsCaptionContentVisible,
            "Opening or switching to a captioned Studio clip must restore captions to visible by default.");
        TestAssert.Equal(
            "CC ON",
            preview.CaptionVisibilityShortText,
            "The transport must clearly report the restored visible state.");
        TestAssert.True(
            preview.HasLiveCaption,
            "Reopening a captioned project should restore the same timed caption overlay without another generation run.");

        return Task.CompletedTask;
    }

    private static Task SegmentCommandMapsExactStreams()
    {
        PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            audioStreamCount: 2,
            sourceName: "source with spaces.mkv");
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        string output = Path.Combine(
            fixture.Root,
            "output with spaces.mp4");
        FfmpegClipRenderCommand command =
            FfmpegClipRenderCommandBuilder.BuildSegment(
                candidate,
                GenerationClipOutputProfile.FromReference(
                    candidate.AnalyzedSource.PreparedSource.Media
                        .PrimaryVideoStream),
                output);

        TestAssert.True(
            ContainsPair(command.Arguments, "-map", "0:0"),
            "The exact primary video stream must be mapped.");
        string audioFilter = command.Arguments[
            command.Arguments.ToList().IndexOf("-filter_complex") + 1];
        TestAssert.True(
            audioFilter.Contains("[0:1][0:2]", StringComparison.Ordinal) &&
            audioFilter.Contains("amix=inputs=2", StringComparison.Ordinal),
            "Every inspected absolute audio stream must enter one bounded mix.");
        TestAssert.True(
            audioFilter.Contains("normalize=0", StringComparison.Ordinal) &&
            audioFilter.Contains(
                "alimiter=limit=0.95:level=disabled:latency=1",
                StringComparison.Ordinal),
            "The source mix must preserve each track's gain, limit only unsafe peaks, and compensate limiter latency.");
        TestAssert.True(
            ContainsPair(command.Arguments, "-map", "[aout]"),
            "The verified audio mix must be mapped.");
        TestAssert.True(
            command.Arguments.Contains(
                candidate.AnalyzedSource.PreparedSource.Media.FullPath),
            "A spaced source path must remain one argument.");
        TestAssert.True(
            ContainsPair(command.Arguments, "-c:v", "h264_mf") &&
            ContainsPair(command.Arguments, "-hw_encoding", "0") &&
            ContainsPair(command.Arguments, "-profile:v", "77"),
            "Rendering must use the bounded Windows Media Foundation software H.264 policy.");
        TestAssert.True(
            command.Arguments.Contains("-b:v"),
            "The Windows Media Foundation command must carry an explicit bounded video bit rate.");
        TestAssert.False(
            command.Arguments.Contains("libopenh264", StringComparer.Ordinal),
            "Rendering cannot depend on the excluded OpenH264 library.");
        TestAssert.False(
            command.Arguments.Any(
                argument => argument.Contains(
                    "Misleading title",
                    StringComparison.Ordinal)),
            "Track titles must not influence stream selection.");
        TestAssert.Equal(output, command.Arguments[^1], "Output argument.");
        fixture.Dispose();
        return Task.CompletedTask;
    }

    private static Task SegmentCommandSuppliesSilence()
    {
        PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: false);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        FfmpegClipRenderCommand command =
            FfmpegClipRenderCommandBuilder.BuildSegment(
                candidate,
                GenerationClipOutputProfile.FromReference(
                    candidate.AnalyzedSource.PreparedSource.Media
                        .PrimaryVideoStream),
                Path.Combine(fixture.Root, "silent.mp4"));

        TestAssert.True(
            ContainsPair(
                command.Arguments,
                "-i",
                "anullsrc=r=48000:cl=stereo"),
            "A source without audio should get deterministic silence.");
        TestAssert.True(
            ContainsPair(command.Arguments, "-map", "1:a:0"),
            "The generated silence stream must be mapped.");
        fixture.Dispose();
        return Task.CompletedTask;
    }

    private static Task CaptionedCommandPreservesAudioMix()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            audioStreamCount: 2);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        FfmpegClipRenderCommand command =
            FfmpegClipRenderCommandBuilder.BuildSegment(
                candidate.AnalyzedSource.PreparedSource.Media,
                candidate.Candidate.Window.Start,
                candidate.Candidate.Window.End,
                GenerationClipOutputProfile.FromReference(
                    candidate.AnalyzedSource.PreparedSource.Media
                        .PrimaryVideoStream),
                Path.Combine(fixture.Root, "captioned.mp4"),
                "caption.ass",
                fixture.Root);

        string videoFilter = command.Arguments[
            command.Arguments.ToList().IndexOf("-vf") + 1];
        string audioFilter = command.Arguments[
            command.Arguments.ToList().IndexOf("-filter_complex") + 1];
        TestAssert.True(
            videoFilter.Contains(
                "ass=filename='caption.ass'",
                StringComparison.Ordinal),
            "The caption script must enter the existing video filter chain.");
        TestAssert.True(
            audioFilter.Contains(
                "[0:1][0:2]amix=inputs=2",
                StringComparison.Ordinal),
            "Caption selection must not remove either audible source stream.");
        TestAssert.Equal(
            fixture.Root,
            command.WorkingDirectory,
            "ASS resolution should use the explicitly owned working directory.");
        return Task.CompletedTask;
    }

    private static Task AssBuilderSupportsFiveEffects()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        AudioTranscriptionResult transcription =
            CreateTranscription(candidate);

        foreach (GenerationCaptionStylePreset style in
                 Enum.GetValues<GenerationCaptionStylePreset>())
        {
            var track = new GenerationCandidateCaptionTrack(
                candidate,
                new GenerationCaptionSourceSelection(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath,
                    candidate.AnalyzedSource.PreparedSource.Media
                        .AudioStreams[0].Index,
                    CaptionAudioContentRole.CreatorCommentary),
                style,
                transcription);
            AssSubtitleDocument first =
                AssSubtitleDocumentBuilder.Build(track, 1080, 1920);
            AssSubtitleDocument second =
                AssSubtitleDocumentBuilder.Build(track, 1080, 1920);
            TestAssert.Equal(
                first.Script,
                second.Script,
                $"{style} ASS output must be deterministic.");
            TestAssert.True(
                first.Script.Contains("Dialogue:", StringComparison.Ordinal),
                $"{style} must produce timed ASS dialogue.");
            TestAssert.Equal(
                style,
                first.EffectiveStyle,
                $"{style} should remain active when words are available.");
        }
        return Task.CompletedTask;
    }

    private static Task AssBuilderPositionsCaptions()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            new GenerationCaptionSourceSelection(
                candidate.AnalyzedSource.PreparedSource.Media.FullPath,
                candidate.AnalyzedSource.PreparedSource.Media
                    .AudioStreams[0].Index,
                CaptionAudioContentRole.CreatorCommentary),
            GenerationCaptionStylePreset.Pop,
            CreateTranscription(candidate));

        AssSubtitleDocument document =
            AssSubtitleDocumentBuilder.Build(
                track,
                1080,
                1920,
                verticalPositionPercent: 25);

        TestAssert.True(
            document.Script.Contains(
                "\\pos(540,480)",
                StringComparison.Ordinal),
            "A 25-percent vertical position must map to the exact ASS pixel coordinate.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => AssSubtitleDocumentBuilder.Build(
                track,
                1080,
                1920,
                verticalPositionPercent: 9),
            "Caption position must retain the safe 10-percent upper bound.");
        return Task.CompletedTask;
    }

    private static Task CaptionPresentationDefaultsRemainCompatible()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            new GenerationCaptionSourceSelection(
                candidate.AnalyzedSource.PreparedSource.Media.FullPath,
                candidate.AnalyzedSource.PreparedSource.Media.AudioStreams[0].Index,
                CaptionAudioContentRole.CreatorCommentary),
            GenerationCaptionStylePreset.Clean,
            CreateTranscription(candidate));

        AssSubtitleDocument legacy =
            AssSubtitleDocumentBuilder.Build(track, 1080, 1920);
        AssSubtitleDocument explicitDefaults =
            AssSubtitleDocumentBuilder.Build(
                track,
                1080,
                1920,
                captionWordLimit:
                    StudioCaptionWordLimitPreset.FullSegment,
                captionMaximumWidthPercent:
                    StudioClipAppearance.DefaultCaptionMaximumWidthPercent,
                captionFontScalePercent:
                    StudioClipAppearance.DefaultCaptionFontScalePercent);
        StudioCaptionFrameLayout layout =
            StudioCaptionPresentationPolicy.CalculateFrameLayout(
                1080,
                1920,
                GenerationCaptionStylePreset.Clean,
                StudioClipAppearance.DefaultCaptionMaximumWidthPercent,
                StudioClipAppearance.DefaultCaptionFontScalePercent);

        TestAssert.Equal(
            legacy.Script,
            explicitDefaults.Script,
            "Omitted caption presentation options must resolve to the explicit legacy defaults.");
        TestAssert.Equal(48, layout.HorizontalMarginPixels,
            "The default width must preserve the renderer's 48-pixel safe margins.");
        TestAssert.Equal(90, layout.BaseFontSizePixels,
            "The default font scale must preserve the renderer's height-derived font size.");
        TestAssert.Equal(
            StudioCaptionWordLimitPreset.Streamlined,
            StudioClipAppearance.CreateDefault(
                GenerationCaptionStylePreset.Clean).CaptionWordLimit,
            "New Studio clips must default to short timed phrases; explicitly restored legacy clips retain their persisted choice.");
        return Task.CompletedTask;
    }

    private static Task CaptionPresentationMatchesPreviewAndRender()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        string[] words =
        [
            "one", "two", "three", "four", "five", "six",
            "seven", "eight", "nine", "ten", "eleven",
        ];
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.Clean,
            CreateTimedTranscription(candidate, words));
        var captions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            [track],
            TimeSpan.Zero);
        foreach ((int wordCount, string expectedGroups) in new[]
                 {
                     (6, "3,3"),
                     (9, "5,4"),
                     (11, "4,4,3"),
                 })
        {
            AudioTranscriptionSegment groupingSegment =
                CreateTimedTranscription(
                    candidate,
                    Enumerable.Range(1, wordCount)
                        .Select(index => $"word{index}")
                        .ToArray()).Segments[0];
            string actualGroups = string.Join(
                ",",
                StudioCaptionPresentationPolicy.ProjectCues(
                        groupingSegment,
                        StudioCaptionWordLimitPreset.Streamlined)
                    .Select(static cue => cue.Words.Count));
            TestAssert.Equal(
                expectedGroups,
                actualGroups,
                "Streamlined captions must balance timed words without orphan flashes.");
        }
        foreach (GenerationCaptionStylePreset phraseStyle in new[]
                 {
                     GenerationCaptionStylePreset.Clean,
                     GenerationCaptionStylePreset.WordFocus,
                     GenerationCaptionStylePreset.KaraokeSweep,
                     GenerationCaptionStylePreset.HighContrast,
                 })
        {
            TestAssert.Equal(
                StudioCaptionWordLimitPreset.Streamlined,
                StudioCaptionPresentationPolicy.ResolveEffectiveWordLimit(
                    phraseStyle,
                    StudioCaptionWordLimitPreset.Streamlined),
                $"{phraseStyle} must honor the selected 3/5/8/full phrase grouping in preview and render.");
        }
        TestAssert.Equal(
            StudioCaptionWordLimitPreset.FullSegment,
            StudioCaptionPresentationPolicy.ResolveEffectiveWordLimit(
                GenerationCaptionStylePreset.Pop,
                StudioCaptionWordLimitPreset.Punchy),
            "Pop must own its one-word transitions instead of applying an invisible phrase-page boundary.");

        GenerationCandidateCaptionTrack popGroupingTrack =
            track.WithRequestedStyle(GenerationCaptionStylePreset.Pop);
        AssSubtitleDocument popPunchy = AssSubtitleDocumentBuilder.Build(
            popGroupingTrack,
            1080,
            1920,
            captionWordLimit: StudioCaptionWordLimitPreset.Punchy);
        AssSubtitleDocument popBalanced = AssSubtitleDocumentBuilder.Build(
            popGroupingTrack,
            1080,
            1920,
            captionWordLimit: StudioCaptionWordLimitPreset.Balanced);
        TestAssert.Equal(
            popPunchy.Script,
            popBalanced.Script,
            "The phrase-size choice must not create hidden timing differences in Pop's one-word final render.");
        StudioCaptionFrameLayout expectedPopLayout =
            StudioCaptionPresentationPolicy.CalculateFrameLayout(
                1080,
                1920,
                GenerationCaptionStylePreset.Pop,
                StudioClipAppearance.DefaultCaptionMaximumWidthPercent,
                StudioClipAppearance.DefaultCaptionFontScalePercent);
        TestAssert.True(
            popPunchy.Script.Contains(
                $"Style: Pop,Segoe UI,{expectedPopLayout.EffectiveFontSizePixels},",
                StringComparison.Ordinal),
            "Final Pop ASS typography must consume the same effective font size exposed to Studio preview.");
        AssSubtitleDocument cleanPunchy = AssSubtitleDocumentBuilder.Build(
            track,
            1080,
            1920,
            captionWordLimit: StudioCaptionWordLimitPreset.Punchy);
        AssSubtitleDocument cleanBalanced = AssSubtitleDocumentBuilder.Build(
            track,
            1080,
            1920,
            captionWordLimit: StudioCaptionWordLimitPreset.Balanced);
        TestAssert.False(
            cleanPunchy.Script.Equals(
                cleanBalanced.Script,
                StringComparison.Ordinal),
            "Clean captions must continue to render visibly different phrase windows for different word limits.");

        var popCalculator = new StudioLiveCaptionFrameCalculator();
        double justBeforeFourthWord =
            track.Segments[0].Words[3].AbsoluteSourceStart.TotalSeconds - 0.04;
        LiveCaptionFrameState popPunchyFrame = popCalculator.Calculate(
            popGroupingTrack,
            StudioCaptionWordLimitPreset.Punchy,
            GenerationCaptionStylePreset.Pop,
            justBeforeFourthWord,
            popGroupingTrack.SourceWindowStart,
            popGroupingTrack.SourceWindowStart +
                popGroupingTrack.SourceWindowDuration);
        LiveCaptionFrameState popBalancedFrame = popCalculator.Calculate(
            popGroupingTrack,
            StudioCaptionWordLimitPreset.Balanced,
            GenerationCaptionStylePreset.Pop,
            justBeforeFourthWord,
            popGroupingTrack.SourceWindowStart,
            popGroupingTrack.SourceWindowStart +
                popGroupingTrack.SourceWindowDuration);
        TestAssert.Equal(
            "three",
            popPunchyFrame.Text!,
            "Pop preview must keep the current spoken word visible until the next word actually begins.");
        TestAssert.Equal(
            popPunchyFrame,
            popBalancedFrame,
            "Pop preview must be invariant to the disabled phrase-size choice, matching final render behavior.");
        GenerationOutputProject project = fixture.CreateDraft(captions);
        var appearance = new StudioClipAppearance(
            GenerationCaptionStylePreset.Clean,
            StudioClipAppearance.DefaultCaptionVerticalPositionPercent,
            StudioVideoEffectPreset.None,
            0,
            captionWordLimit: StudioCaptionWordLimitPreset.Streamlined,
            captionMaximumWidthPercent: 75,
            captionFontScalePercent: 125);
        GenerationOutputAsset asset = project.PrimaryAsset.WithStudioEdits(
            project.PrimaryAsset.SourceStart,
            project.PrimaryAsset.SourceEnd,
            appearance);
        project = project.ReplaceAsset(asset);
        GenerationClipOutputProfile profile =
            GenerationClipOutputProfile.FromAsset(asset);
        StudioCaptionFrameLayout expectedLayout =
            StudioCaptionPresentationPolicy.CalculateFrameLayout(
                profile.Width,
                profile.Height,
                appearance.CaptionStyle,
                appearance.CaptionMaximumWidthPercent,
                appearance.CaptionFontScalePercent);
        AssSubtitleDocument document = AssSubtitleDocumentBuilder.Build(
            track,
            profile.Width,
            profile.Height,
            asset.SourceStart,
            asset.Duration,
            appearance.CaptionVerticalPositionPercent,
            appearance.CaptionWordLimit,
            appearance.CaptionMaximumWidthPercent,
            appearance.CaptionFontScalePercent);
        using var preview = new StudioPreviewViewModel(mediaService: null);
        preview.Bind(hasProject: true, project, asset);
        preview.PreviewPositionSeconds =
            track.Segments[0].AbsoluteSourceStart.TotalSeconds + 0.1;

        TestAssert.Equal(
            "one two three four",
            preview.LiveCaptionText!,
            "The live overlay must balance the shared caption windows without leaving an orphan word.");
        TestAssert.True(
            document.Script.Contains(
                "one two three four",
                StringComparison.Ordinal) &&
            document.Script.Contains(
                "five six seven eight",
                StringComparison.Ordinal) &&
            document.Script.Contains(
                "nine ten eleven",
                StringComparison.Ordinal),
            "The final ASS document must render the same balanced ordered windows.");
        IReadOnlyList<StudioCaptionCue> projectedTrackCues =
            StudioCaptionPresentationPolicy.ProjectCues(
                track,
                appearance.CaptionWordLimit);
        TestAssert.Equal(
            projectedTrackCues.Count,
            document.Script.Split("Dialogue: ").Length - 1,
            "Final ASS must emit the same cue count as the shared track projection used by Studio preview.");
        TestAssert.True(
            projectedTrackCues.All(
                cue => document.Script.Contains(
                    cue.Text,
                    StringComparison.Ordinal)),
            "Final ASS must preserve every ordered phrase from the shared track projection.");
        TestAssert.Equal(
            (double)expectedLayout.MaximumWidthPixels,
            preview.LiveCaptionMaximumWidthPixels,
            "Preview width must use the same 75-percent safe-width calculation as ASS.");
        TestAssert.Equal(
            StudioCaptionPresentationPolicy.GetWpfPreviewFontSize(
                expectedLayout),
            preview.LiveCaptionFontSizePixels,
            "Preview font size must convert ASS points to WPF device-independent pixels so line wrapping matches the final burn.");
        TestAssert.True(
            document.Script.Contains(
                $"Style: Clean,Segoe UI,{expectedLayout.BaseFontSizePixels},",
                StringComparison.Ordinal) &&
            document.Script.Contains(
                $",2,{expectedLayout.HorizontalMarginPixels},{expectedLayout.HorizontalMarginPixels},",
                StringComparison.Ordinal),
            "Final ASS typography must use the same font and width geometry exposed by preview.");

        preview.PreviewPositionSeconds =
            track.Segments[0].Words[4].AbsoluteSourceStart.TotalSeconds + 0.01;
        TestAssert.Equal(
            "five six seven eight",
            preview.LiveCaptionText!,
            "The live overlay must advance at the same timed window boundary as final ASS.");

        AudioTranscriptionResult timedBaseline =
            CreateTimedTranscription(
                candidate,
                ["Wait", "what", "Don't", "stop"]);
        TimeSpan absoluteOffset =
            timedBaseline.Manifest.AbsoluteSourceOffset;
        AudioTranscriptionWord[] punctuatedWords =
        [
            CreateWord("Wait", 1.203, 1.497),
            CreateWord("what", 1.804, 2.096),
            CreateWord("Don't", 2.223, 2.517),
            CreateWord("stop", 2.704, 2.996),
        ];
        AudioTranscriptionWord CreateWord(
            string text,
            double startSeconds,
            double endSeconds)
        {
            TimeSpan start = TimeSpan.FromSeconds(startSeconds);
            TimeSpan end = TimeSpan.FromSeconds(endSeconds);
            return new AudioTranscriptionWord(
                text,
                start,
                end,
                absoluteOffset + start,
                absoluteOffset + end);
        }
        const string punctuatedText =
            "Wait... what?! Don't stop.";
        var punctuatedSegment = new AudioTranscriptionSegment(
            "punctuated-gap-segment",
            timedBaseline.NeighborhoodId,
            punctuatedText,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3.20),
            absoluteOffset + TimeSpan.FromSeconds(1),
            absoluteOffset + TimeSpan.FromSeconds(3.20),
            punctuatedWords);
        var punctuatedTranscription = new AudioTranscriptionResult(
            timedBaseline.NeighborhoodId,
            timedBaseline.AbsoluteAudioStreamIndex,
            [punctuatedSegment],
            timedBaseline.Manifest,
            timedBaseline.DetectedLanguage,
            timedBaseline.Warnings);
        var karaokeTrack = new GenerationCandidateCaptionTrack(
            candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep,
            punctuatedTranscription);
        StudioCaptionCue punctuatedCue =
            StudioCaptionPresentationPolicy.ProjectCues(
                punctuatedSegment,
                StudioCaptionWordLimitPreset.FullSegment).Single();
        TestAssert.Equal(
            punctuatedText,
            punctuatedCue.Text,
            "Timed caption projection must retain original punctuation and spacing.");
        TestAssert.Equal(
            "what",
            punctuatedCue.Text.Substring(
                punctuatedCue.WordSpans[1].StartIndex,
                punctuatedCue.WordSpans[1].Length),
            "Repeated styling must use the shared character span instead of reconstructing text.");

        AssSubtitleDocument karaokeDocument =
            AssSubtitleDocumentBuilder.Build(
                karaokeTrack,
                profile.Width,
                profile.Height,
                asset.SourceStart,
                asset.Duration,
                appearance.CaptionVerticalPositionPercent,
                StudioCaptionWordLimitPreset.FullSegment,
                appearance.CaptionMaximumWidthPercent,
                appearance.CaptionFontScalePercent);
        string normalizedKaraokeScript =
            System.Text.RegularExpressions.Regex.Replace(
                karaokeDocument.Script,
                @"\{[^}]*\}",
                string.Empty);
        TestAssert.True(
            normalizedKaraokeScript.Contains(
                "Wait... what?! Don't stop.",
                StringComparison.Ordinal) &&
            karaokeDocument.Script.Contains(
                "{\\1c&H005EC7FF&\\2c&H00A59E98&\\kf",
                StringComparison.Ordinal) &&
            karaokeDocument.Script.Contains(
                "\\fscx112\\fscy112\\t(0,140,\\fscx105\\fscy105)}Wait",
                StringComparison.Ordinal) &&
            karaokeDocument.Script.Contains(
                "\\fscx112\\fscy112\\t(0,140,\\fscx105\\fscy105)}what",
                StringComparison.Ordinal) &&
            karaokeDocument.Script.Contains(
                "{\\c&H00A59E98&}",
                StringComparison.Ordinal) &&
            karaokeDocument.Script.Contains(
                "WrapStyle: 1",
                StringComparison.Ordinal),
            "Karaoke render text must preserve punctuation while sweeping gold across each actual word and muting future words.");
        TimeSpan priorEnd = TimeSpan.Zero;
        foreach (string dialogue in karaokeDocument.Script
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                     .Where(static line => line.StartsWith(
                         "Dialogue: ",
                         StringComparison.Ordinal)))
        {
            string[] fields = dialogue.Split(',', 4);
            TimeSpan start = TimeSpan.ParseExact(
                fields[1],
                "h\\:mm\\:ss\\.ff",
                System.Globalization.CultureInfo.InvariantCulture);
            TimeSpan end = TimeSpan.ParseExact(
                fields[2],
                "h\\:mm\\:ss\\.ff",
                System.Globalization.CultureInfo.InvariantCulture);
            TestAssert.True(
                start >= priorEnd,
                "Adjacent Karaoke word and silence intervals must not overlap after ASS quantization.");
            priorEnd = end;
        }

        var punctuationCaptions =
            new GenerationCaptionPreparationResult(
                fixture.Moments,
                [karaokeTrack],
                TimeSpan.Zero);
        GenerationOutputProject punctuationProject =
            fixture.CreateDraft(punctuationCaptions);
        GenerationOutputAsset punctuationAsset =
            punctuationProject.PrimaryAsset;
        preview.Bind(
            hasProject: true,
            punctuationProject,
            punctuationAsset);
        preview.PreviewPositionSeconds =
            absoluteOffset.TotalSeconds + 1.35;
        TestAssert.Equal(
            punctuatedText,
            preview.LiveCaptionText!,
            "Studio must preview the exact punctuated text written to ASS.");
        TestAssert.Equal(
            punctuatedCue.WordSpans[0].StartIndex,
            preview.LiveCaptionAccentStartIndex,
            "Studio and ASS must begin the Karaoke sweep at the same mapped word.");
        TestAssert.Equal(
            punctuatedCue.WordSpans[0].Length,
            preview.LiveCaptionSweepLength,
            "Studio and ASS must sweep the same mapped word span.");
        TestAssert.True(
            Math.Abs(preview.LiveCaptionAccentProgress - 0.5) < 0.001,
            "Studio must expose continuous Karaoke progress across the active word.");
        preview.PreviewPositionSeconds =
            absoluteOffset.TotalSeconds + 1.65;
        TestAssert.Equal(
            punctuatedCue.WordSpans[1].StartIndex,
            preview.LiveCaptionAccentStartIndex,
            "A retained inter-word pause must hold the next word in the future color instead of advancing early.");
        TestAssert.Equal(
            0d,
            preview.LiveCaptionAccentProgress,
            "Karaoke progress must remain stopped during the retained inter-word pause.");

        var popTrack = new GenerationCandidateCaptionTrack(
            candidate,
            karaokeTrack.SourceSelection,
            GenerationCaptionStylePreset.Pop,
            punctuatedTranscription);
        var popCaptions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            [popTrack],
            TimeSpan.Zero);
        GenerationOutputProject popProject = fixture.CreateDraft(popCaptions);
        preview.Bind(hasProject: true, popProject, popProject.PrimaryAsset);
        foreach ((double offsetMilliseconds, double expectedScale) in
                 new[]
                 {
                     (0d, 0.82d),
                     (60d, 0.97d),
                     (120d, 1.12d),
                     (190d, 1.06d),
                     (260d, 1d),
                 })
        {
            preview.PreviewPositionSeconds =
                absoluteOffset.TotalSeconds + 1.20 +
                offsetMilliseconds / 1000d;
            TestAssert.True(
                Math.Abs(preview.LiveCaptionScale - expectedScale) < 0.001,
                "Studio Pop scaling must match the ASS 82-to-112-to-100 percent animation.");
        }
        preview.Bind(hasProject: false, project: null, asset: null);
        TestAssert.Null(
            preview.LiveCaptionText,
            "Rebinding the preview must clear the prior clip's caption immediately, before any asynchronous media load finishes.");
        return Task.CompletedTask;
    }

    private static Task TimedCaptionsClearDuringSilence()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        AudioTranscriptionResult baseline =
            CreateTimedTranscription(candidate, ["Ready", "now", "go"]);
        TimeSpan offset = baseline.Manifest.AbsoluteSourceOffset;
        AudioTranscriptionWord Word(
            string text,
            double start,
            double end) => new(
                text,
                TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(end),
                offset + TimeSpan.FromSeconds(start),
                offset + TimeSpan.FromSeconds(end));
        AudioTranscriptionWord[] words =
        [
            Word("Ready", 1.0, 1.30),
            Word("now", 1.35, 1.60),
            Word("go", 3.0, 3.30),
        ];
        var segment = new AudioTranscriptionSegment(
            "speech-with-reviewed-silence",
            baseline.NeighborhoodId,
            "Ready now go",
            TimeSpan.FromSeconds(0.5),
            TimeSpan.FromSeconds(3.8),
            offset + TimeSpan.FromSeconds(0.5),
            offset + TimeSpan.FromSeconds(3.8),
            words);
        var transcription = new AudioTranscriptionResult(
            baseline.NeighborhoodId,
            baseline.AbsoluteAudioStreamIndex,
            [segment],
            baseline.Manifest,
            baseline.DetectedLanguage,
            baseline.Warnings);
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.Clean,
            transcription);

        IReadOnlyList<StudioCaptionCue> cues =
            StudioCaptionPresentationPolicy.ProjectCues(
                segment,
                StudioCaptionWordLimitPreset.FullSegment);
        TestAssert.Equal(2, cues.Count,
            "A long measured inter-word silence must split a caption page even when the creator keeps the full-segment word limit.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(1),
            cues[0].RelativeStart,
            "The first caption page must begin at its first measured word, without invented pre-roll.");
        TestAssert.Equal(
            TimeSpan.FromMilliseconds(1600),
            cues[0].RelativeEnd,
            "A caption page must clear at its last measured word instead of inventing post-roll across silence.");
        TestAssert.True(
            cues[0].RelativeEnd < TimeSpan.FromSeconds(2) &&
            cues[1].RelativeStart > TimeSpan.FromSeconds(2),
            "The shared cue policy must retain an actual no-caption interval during measured silence.");

        var captions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            [track],
            TimeSpan.Zero);
        GenerationOutputProject project = fixture.CreateDraft(captions);
        GenerationOutputAsset asset = project.PrimaryAsset;
        using var preview = new StudioPreviewViewModel(mediaService: null);
        preview.Bind(hasProject: true, project, asset);
        preview.PreviewPositionSeconds =
            offset.TotalSeconds + 0.95;
        TestAssert.Null(
            preview.LiveCaptionText,
            "Studio must not show the caption before the first measured word begins.");
        preview.PreviewPositionSeconds =
            offset.TotalSeconds + 1.0;
        TestAssert.Equal(
            "Ready now",
            preview.LiveCaptionText!,
            "Studio should show the short caption page at speech onset.");
        preview.PreviewPositionSeconds =
            offset.TotalSeconds + 2.0;
        TestAssert.Null(
            preview.LiveCaptionText,
            "Studio must remove the caption during a long measured silence.");
        preview.PreviewPositionSeconds =
            offset.TotalSeconds + 2.95;
        TestAssert.Null(
            preview.LiveCaptionText,
            "Studio must not show the next page before its measured word.");
        preview.PreviewPositionSeconds =
            offset.TotalSeconds + 3.0;
        TestAssert.Equal(
            "go",
            preview.LiveCaptionText!,
            "Studio should show the next page exactly at its measured word.");

        AssSubtitleDocument document = AssSubtitleDocumentBuilder.Build(
            track,
            1080,
            1920,
            asset.SourceStart,
            asset.Duration,
            StudioClipAppearance.DefaultCaptionVerticalPositionPercent,
            StudioCaptionWordLimitPreset.FullSegment);
        string[] dialogueLines = document.Script
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.StartsWith(
                "Dialogue: ",
                StringComparison.Ordinal))
            .ToArray();
        TestAssert.Equal(2, dialogueLines.Length,
            "Final ASS rendering must use the same two speech-bounded pages as Studio preview.");
        return Task.CompletedTask;
    }

    private static Task PunchyCaptionsFollowWordsAndInternalPauses()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        AudioTranscriptionResult baseline =
            CreateTimedTranscription(candidate, ["reference"]);
        TimeSpan offset = baseline.Manifest.AbsoluteSourceOffset;
        AudioTranscriptionWord Word(
            string text,
            double start,
            double end) => new(
                text,
                TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(end),
                offset + TimeSpan.FromSeconds(start),
                offset + TimeSpan.FromSeconds(end));
        AudioTranscriptionWord[] words =
        [
            Word("So", 0.10, 0.28),
            Word("as", 0.30, 0.42),
            Word("you", 0.44, 0.59),
            Word("can", 0.61, 0.75),
            Word("see", 0.77, 0.95),
            Word("this", 1.00, 1.18),
            Word("is", 1.20, 1.31),
            Word("going", 1.33, 1.54),
            Word("to", 1.56, 1.67),
            Word("be", 1.69, 1.81),
            Word("very", 1.83, 2.00),
            Word("conspiratorial", 2.02, 2.43),
            Word("type", 3.21, 3.42),
            Word("of", 3.44, 3.56),
            Word("game", 3.58, 3.83),
        ];
        const string text =
            "So as you can see this is going to be very conspiratorial type of game";
        var segment = new AudioTranscriptionSegment(
            "long-utterance-with-pause",
            baseline.NeighborhoodId,
            text,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(4),
            offset,
            offset + TimeSpan.FromSeconds(4),
            words);
        var transcription = new AudioTranscriptionResult(
            baseline.NeighborhoodId,
            baseline.AbsoluteAudioStreamIndex,
            [segment],
            baseline.Manifest,
            baseline.DetectedLanguage,
            baseline.Warnings);
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep,
            transcription);

        IReadOnlyList<StudioCaptionCue> cues =
            StudioCaptionPresentationPolicy.ProjectCues(
                segment,
                StudioCaptionWordLimitPreset.Punchy);
        TestAssert.Equal(5, cues.Count,
            "Punchy must create four three-word pages before the pause and one three-word page after it.");
        TestAssert.True(
            cues.All(static cue => cue.Words.Count <= 3),
            "Punchy must retain a hard maximum of three displayed words on every page.");
        TestAssert.True(
            new[]
            {
                "So as you",
                "can see this",
                "is going to",
                "be very conspiratorial",
                "type of game",
            }.SequenceEqual(cues.Select(static cue => cue.Text)),
            "Punchy must retain the exact transcript order while balancing only within each speech run.");
        TestAssert.Equal(
            words[0].RelativeStart,
            cues[0].RelativeStart,
            "The first Punchy page must begin at the first word onset.");
        TestAssert.Equal(
            words[3].RelativeStart,
            cues[0].RelativeEnd,
            "A continuous Punchy page must hand off exactly when the next page's first word begins.");
        TestAssert.Equal(
            words[11].RelativeEnd,
            cues[3].RelativeEnd,
            "The pre-pause page must clear when its last measured word ends.");
        TestAssert.Equal(
            words[12].RelativeStart,
            cues[4].RelativeStart,
            "The post-pause page must wait for its first measured word onset.");
        TestAssert.True(
            cues[4].RelativeStart - cues[3].RelativeEnd >
                TimeSpan.FromMilliseconds(500),
            "An audible internal pause must remain a real no-caption interval.");

        var calculator = new StudioLiveCaptionFrameCalculator();
        LiveCaptionFrameState beforeSpeech = calculator.Calculate(
            track,
            StudioCaptionWordLimitPreset.Punchy,
            GenerationCaptionStylePreset.KaraokeSweep,
            offset.TotalSeconds + 0.09,
            track.SourceWindowStart,
            track.SourceWindowStart + track.SourceWindowDuration);
        LiveCaptionFrameState firstPage = calculator.Calculate(
            track,
            StudioCaptionWordLimitPreset.Punchy,
            GenerationCaptionStylePreset.KaraokeSweep,
            offset.TotalSeconds + 0.10,
            track.SourceWindowStart,
            track.SourceWindowStart + track.SourceWindowDuration);
        LiveCaptionFrameState fourthWord = calculator.Calculate(
            track,
            StudioCaptionWordLimitPreset.Punchy,
            GenerationCaptionStylePreset.KaraokeSweep,
            offset.TotalSeconds + 0.61,
            track.SourceWindowStart,
            track.SourceWindowStart + track.SourceWindowDuration);
        LiveCaptionFrameState duringPause = calculator.Calculate(
            track,
            StudioCaptionWordLimitPreset.Punchy,
            GenerationCaptionStylePreset.KaraokeSweep,
            offset.TotalSeconds + 2.8,
            track.SourceWindowStart,
            track.SourceWindowStart + track.SourceWindowDuration);
        TestAssert.Null(beforeSpeech.Text,
            "Live preview must remain empty before the first exact word onset.");
        TestAssert.Equal("So as you", firstPage.Text!,
            "Live preview must show only the first three-word Punchy page at speech onset.");
        TestAssert.Equal("can see this", fourthWord.Text!,
            "Live preview must change pages exactly at the fourth word onset.");
        TestAssert.Null(duringPause.Text,
            "Live preview must clear throughout the measured internal pause.");

        AssSubtitleDocument document = AssSubtitleDocumentBuilder.Build(
            track,
            1080,
            1920,
            captionWordLimit: StudioCaptionWordLimitPreset.Punchy);
        TestAssert.False(
            document.Script.Contains(text, StringComparison.Ordinal),
            "Final karaoke rendering must never collapse a timed Punchy sentence into one full-sentence event.");
        string normalizedScript =
            System.Text.RegularExpressions.Regex.Replace(
                document.Script,
                @"\{[^}]*\}",
                string.Empty);
        foreach (string cueText in cues.Select(static cue => cue.Text))
        {
            TestAssert.True(
                normalizedScript.Contains(cueText, StringComparison.Ordinal),
                "Final karaoke rendering must preserve every projected Punchy phrase.");
        }
        return Task.CompletedTask;
    }

    private static Task CaptionPresentationPersistsThroughStudioEdits()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.Clean,
            CreateTranscription(candidate));
        var captions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            [track],
            TimeSpan.Zero);
        GenerationOutputProject project = fixture.CreateDraft(captions);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var editor = new StudioClipEditorViewModel(session);
        editor.Bind(project, project.PrimaryAsset);
        editor.SelectedCaptionWordLimit = editor.CaptionWordLimitOptions.Single(
            static option =>
                option.Value == StudioCaptionWordLimitPreset.Streamlined);
        editor.CaptionMaximumWidthPercent = 72;
        editor.CaptionFontScalePercent = 135;

        TestAssert.True(editor.ApplyPendingEdit(),
            "Studio should persist a valid caption presentation draft.");
        GenerationOutputAsset saved = session.Current!.PrimaryAsset;
        TestAssert.Equal(
            StudioCaptionWordLimitPreset.Streamlined,
            saved.Appearance.CaptionWordLimit,
            "The selected word window must cross the output-session boundary.");
        TestAssert.Equal(72d, saved.Appearance.CaptionMaximumWidthPercent,
            "The caption maximum width must cross the output-session boundary.");
        TestAssert.Equal(135d, saved.Appearance.CaptionFontScalePercent,
            "The caption font scale must cross the output-session boundary.");

        GenerationOutputAsset afterCaptionSave = saved.WithCaptionTrack(
            saved.Captions!.WithEditedSegments(saved.Captions.Segments));
        TestAssert.Equal(
            saved.Appearance.CaptionWordLimit,
            afterCaptionSave.Appearance.CaptionWordLimit,
            "Saving caption text must not reset the chosen word window.");
        TestAssert.Equal(
            saved.Appearance.CaptionMaximumWidthPercent,
            afterCaptionSave.Appearance.CaptionMaximumWidthPercent,
            "Saving caption text must not reset caption width.");
        TestAssert.Equal(
            saved.Appearance.CaptionFontScalePercent,
            afterCaptionSave.Appearance.CaptionFontScalePercent,
            "Saving caption text must not reset caption font size.");
        return Task.CompletedTask;
    }

    private static Task CaptionCorrectionsPreserveTruthfulTiming()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        AudioTranscriptionResult transcription =
            CreateTranscription(candidate);
        var original = new GenerationCandidateCaptionTrack(
            candidate,
            new GenerationCaptionSourceSelection(
                candidate.AnalyzedSource.PreparedSource.Media.FullPath,
                candidate.AnalyzedSource.PreparedSource.Media
                    .AudioStreams[0].Index,
                CaptionAudioContentRole.CreatorCommentary),
            GenerationCaptionStylePreset.KaraokeSweep,
            transcription);
        AudioTranscriptionSegment source = original.Segments[0];
        var correctedSegment = new AudioTranscriptionSegment(
            source.Id,
            source.NeighborhoodId,
            "corrected caption remains one truthful timed segment without fabricated windows",
            TimeSpan.FromSeconds(1.1),
            TimeSpan.FromSeconds(2.4),
            candidate.Candidate.Window.Start + TimeSpan.FromSeconds(1.1),
            candidate.Candidate.Window.Start + TimeSpan.FromSeconds(2.4));
        GenerationCandidateCaptionTrack corrected =
            original.WithEditedSegments([correctedSegment]);

        AssSubtitleDocument document = AssSubtitleDocumentBuilder.Build(
            corrected,
            1080,
            1920,
            captionWordLimit:
                StudioCaptionWordLimitPreset.Streamlined);

        TestAssert.True(
            corrected.IsUserEdited,
            "A corrected track must preserve explicit edit provenance.");
        TestAssert.False(
            corrected.HasTimedWords,
            "Changed text or timing must not retain stale provider word timestamps.");
        TestAssert.Equal(
            GenerationCaptionStylePreset.KaraokeSweep,
            document.EffectiveStyle,
            "A corrected phrase must retain the explicitly selected effect at truthful phrase timing.");
        TestAssert.True(
            document.Script.Contains(
                "corrected caption remains one truthful timed segment without fabricated windows",
                StringComparison.Ordinal),
            "The final subtitle document must use the corrected text.");
        TestAssert.False(
            document.Script.Contains(
                "hello world",
                StringComparison.Ordinal),
            "The original provider text must not drift into the edited render.");
        TestAssert.Equal(
            1,
            document.Script.Split("Dialogue: ").Length - 1,
            "An edited segment without word timestamps must stay whole instead of receiving fabricated word-window timing.");
        TestAssert.True(
            document.Script.Contains(
                "{\\kf130}corrected caption remains one truthful timed segment without fabricated windows",
                StringComparison.Ordinal),
            "The retained phrase interval must drive the selected Karaoke sweep without manufacturing word boundaries.");

        var partiallyTimedSegment = new AudioTranscriptionSegment(
            source.Id,
            source.NeighborhoodId,
            "hello world again",
            source.RelativeStart,
            source.RelativeEnd,
            source.AbsoluteSourceStart,
            source.AbsoluteSourceEnd,
            source.Words);
        GenerationCandidateCaptionTrack partiallyTimed =
            original.WithEditedSegments([partiallyTimedSegment]);
        AssSubtitleDocument partialDocument =
            AssSubtitleDocumentBuilder.Build(
                partiallyTimed,
                1080,
                1920,
                captionWordLimit:
                    StudioCaptionWordLimitPreset.Streamlined);
        TestAssert.Equal(
            GenerationCaptionStylePreset.KaraokeSweep,
            partialDocument.EffectiveStyle,
            "Incomplete timed-word coverage must retain the selected effect at phrase granularity.");
        TestAssert.True(
            partialDocument.Script.Contains(
                "hello world again",
                StringComparison.Ordinal),
            "Incomplete timed-word coverage must preserve every retained caption word.");
        TestAssert.True(
            partialDocument.Warnings.Any(warning =>
                warning.Contains(
                    "not applied",
                    StringComparison.OrdinalIgnoreCase)),
            "The render manifest must explain why a selected word window was not safely applied.");
        var partialCaptions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            [partiallyTimed],
            TimeSpan.Zero);
        GenerationOutputProject partialProject =
            fixture.CreateDraft(partialCaptions);
        GenerationOutputAsset partialAsset =
            partialProject.PrimaryAsset.WithStudioEdits(
                partialProject.PrimaryAsset.SourceStart,
                partialProject.PrimaryAsset.SourceEnd,
                new StudioClipAppearance(
                    GenerationCaptionStylePreset.KaraokeSweep,
                    StudioClipAppearance.DefaultCaptionVerticalPositionPercent,
                    StudioVideoEffectPreset.None,
                    0,
                    captionWordLimit:
                        StudioCaptionWordLimitPreset.Streamlined));
        partialProject = partialProject.ReplaceAsset(partialAsset);
        using var preview = new StudioPreviewViewModel(mediaService: null);
        preview.Bind(hasProject: true, partialProject, partialAsset);
        TestAssert.True(
            preview.HasLiveCaptionPresentationWarning,
            "Studio must explain phrase-granularity effect timing before rendering.");
        preview.PreviewPositionSeconds =
            partiallyTimed.Segments[0].AbsoluteSourceStart.TotalSeconds +
            (partiallyTimed.Segments[0].RelativeEnd -
             partiallyTimed.Segments[0].RelativeStart).TotalSeconds / 2;
        TestAssert.Equal(
            GenerationCaptionStylePreset.KaraokeSweep,
            preview.LiveCaptionStyle,
            "Studio preview must retain the selected Karaoke treatment.");
        TestAssert.Equal(
            partiallyTimed.Segments[0].Text.Length,
            preview.LiveCaptionSweepLength,
            "Phrase-timed Karaoke must sweep the complete retained text.");
        TestAssert.True(
            preview.LiveCaptionAccentProgress is > 0.49 and < 0.51,
            "Studio phrase sweep progress must use the same retained phrase interval as the render.");

        var popAppearance = new StudioClipAppearance(
            GenerationCaptionStylePreset.Pop,
            partialAsset.Appearance.CaptionVerticalPositionPercent,
            partialAsset.Appearance.VideoEffect,
            partialAsset.Appearance.VideoEffectIntensityPercent,
            partialAsset.Appearance.GraphicOverlays,
            partialAsset.Appearance.CaptionWordLimit,
            partialAsset.Appearance.CaptionMaximumWidthPercent,
            partialAsset.Appearance.CaptionFontScalePercent);
        GenerationOutputAsset popAsset = partialAsset.WithStudioEdits(
            partialAsset.SourceStart,
            partialAsset.SourceEnd,
            popAppearance);
        partialProject = partialProject.ReplaceAsset(popAsset);
        preview.Bind(hasProject: true, partialProject, popAsset);
        preview.PreviewPositionSeconds =
            partiallyTimed.Segments[0].AbsoluteSourceStart.TotalSeconds +
            0.12;
        TestAssert.Equal(
            partiallyTimed.Segments[0].Text,
            preview.LiveCaptionText!,
            "Phrase-timed Pop must keep the complete caption visible.");
        TestAssert.True(
            Math.Abs(preview.LiveCaptionScale - 1.12) < 0.001,
            "Phrase-timed Pop must use the same onset animation as a timed word.");
        return Task.CompletedTask;
    }

    private static Task EditorialCaptionTranscriptsStayBounded()
    {
        string source = TestMediaFactory.CreateSourcePath(
            "bounded-editorial-caption-source.mkv");
        TimeSpan sourceWindowStart = TimeSpan.FromSeconds(10);
        const int segmentCount = 50;
        AudioTranscriptionSegment[] segments = Enumerable
            .Range(0, segmentCount)
            .Select(index =>
            {
                TimeSpan relativeStart =
                    TimeSpan.FromSeconds(index + 0.1);
                TimeSpan relativeEnd =
                    TimeSpan.FromSeconds(index + 0.9);
                string text =
                    $"Segment {index:00}: " +
                    new string((char)('a' + index % 26), 100) +
                    ".";
                return new AudioTranscriptionSegment(
                    $"bounded-editorial-{index:00}",
                    "bounded-editorial-caption-neighborhood",
                    text,
                    relativeStart,
                    relativeEnd,
                    sourceWindowStart + relativeStart,
                    sourceWindowStart + relativeEnd);
            })
            .ToArray();
        GenerationCandidateCaptionTrack track =
            GenerationCandidateCaptionTrack.RestoreStudioHandoff(
                "bounded-editorial-caption",
                "bounded-editorial-caption-neighborhood",
                new GenerationCaptionSourceSelection(
                    source,
                    1,
                    CaptionAudioContentRole.GameDialogue),
                GenerationCaptionStylePreset.Clean,
                sourceWindowStart,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(100),
                segments,
                isUserEdited: false,
                GenerationCaptionSuppressionReason.None);

        ClipEditorialTranscriptContext context =
            RetainedCaptionEditorialTranscriptProjector.Project(
                track,
                sourceWindowStart,
                sourceWindowStart + TimeSpan.FromSeconds(60))
            .Single();

        TestAssert.Equal(
            ClipEditorialTranscriptContext.MaximumTextLength,
            context.Text.Length,
            "More than four thousand retained caption characters must be bounded rather than rejected by a later Studio route.");
        TestAssert.Equal(
            ClipEditorialTranscriptContext.MaximumSpanCount,
            context.Spans.Count,
            "Editorial timing evidence must retain no more than the bounded span count.");
        TestAssert.True(
            context.Spans.All(static span =>
                span.Text.Length <=
                    ClipEditorialTranscriptSpan.MaximumTextLength),
            "Every retained editorial timing span must respect its independent text bound.");
        TestAssert.True(
            context.Spans.Zip(
                    context.Spans.Skip(1),
                    static (left, right) =>
                        right.SourceStart >= left.SourceEnd)
                .All(static ordered => ordered),
            "Bounded timing spans must remain ordered and non-overlapping.");
        TestAssert.True(
            context.Text.StartsWith(
                "Segment 00:",
                StringComparison.Ordinal) &&
            context.Text.Contains(
                "Segment 32:",
                StringComparison.Ordinal),
            "The bounded transcript must preserve source order beyond the timed-span cap.");
        TestAssert.Equal(
            AudioContentRole.GameDialogue,
            context.Role.Role,
            "The selected caption stream role must cross the shared projection seam.");
        TestAssert.Equal(
            AudioContentRoleSource.UserConfirmed,
            context.Role.Source,
            "A known caption stream role must retain user-confirmed authority.");
        TestAssert.Equal(
            ClipEditorialTranscriptAuthority.AutomaticUnreviewed,
            context.Authority,
            "An unedited retained provider transcript must remain automatic and unreviewed.");
        return Task.CompletedTask;
    }

    private static async Task
        EditorialTranscriptCutsStayIdenticalAcrossRoutes()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        AudioTranscriptionResult transcription = CreateTimedTranscription(
            candidate,
            ["Before,", "keep", "this", "exact", "order!", "After."]);
        GenerationCaptionSourceSelection selection =
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!;
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            selection,
            GenerationCaptionStylePreset.KaraokeSweep,
            transcription);
        var captions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            [track],
            TimeSpan.Zero);

        ClipEditorialTranscriptContext expectedInitial =
            RetainedCaptionEditorialTranscriptProjector.Project(
                track,
                candidate.Candidate.Window.Start,
                candidate.Candidate.Window.End)
            .Single();
        GenerationEditorialMetadataResult initialMetadata =
            await fixture.EditorialMetadata.GenerateAsync(
                fixture.Moments,
                captions,
                CancellationToken.None);
        ClipEditorialTranscriptContext initialRoute = initialMetadata
            .Find(candidate.Id)
            .Context
            .Transcripts
            .Single();
        AssertEquivalentEditorialTranscript(
            expectedInitial,
            initialRoute,
            "Initial Generate metadata");

        GenerationOutputProject project =
            GenerationOutputProject.FromResult(
                new GenerationResult(
                    fixture.GenerationRequest,
                    fixture.Moments,
                    initialMetadata,
                    captions),
                fixture.FinalDirectory);
        GenerationOutputAsset initialAsset = project.PrimaryAsset;
        GenerationOutputAsset retainedAsset = initialAsset.WithCaptionTrack(
            track.ToStudioHandoff());
        AssertEquivalentEditorialTranscript(
            expectedInitial,
            retainedAsset.EditorialContext!.Transcripts.Single(),
            "Retained Studio handoff");

        GenerationCandidateCaptionTrack editedTrack =
            track.WithEditedSegments(track.Segments);
        ClipEditorialTranscriptContext expectedEdited =
            RetainedCaptionEditorialTranscriptProjector.Project(
                editedTrack,
                candidate.Candidate.Window.Start,
                candidate.Candidate.Window.End)
            .Single();
        GenerationOutputAsset editedAsset =
            retainedAsset.WithCaptionTrack(editedTrack);
        AssertEquivalentEditorialTranscript(
            expectedEdited,
            editedAsset.EditorialContext!.Transcripts.Single(),
            "Edited caption save");
        TestAssert.Equal(
            ClipEditorialTranscriptAuthority.UserCorrected,
            editedAsset.EditorialContext.Transcripts.Single().Authority,
            "Saving caption edits must upgrade transcript authority without changing its text or timings.");

        TimeSpan cutStart = candidate.Candidate.Window.Start +
            TimeSpan.FromSeconds(1.4);
        TimeSpan cutEnd = candidate.Candidate.Window.Start +
            TimeSpan.FromSeconds(2.7);
        GenerationOutputAsset cutAsset = editedAsset.WithStudioEdits(
            cutStart,
            cutEnd,
            editedAsset.Appearance);
        ClipEditorialTranscriptContext expectedCut =
            RetainedCaptionEditorialTranscriptProjector.Project(
                cutAsset.Captions!,
                cutStart,
                cutEnd)
            .Single();
        ClipEditorialTranscriptContext currentCut = cutAsset
            .CreateCurrentCutEditorialContext()
            .Transcripts
            .Single();
        AssertEquivalentEditorialTranscript(
            expectedCut,
            currentCut,
            "Current Studio cut");
        TestAssert.Equal(
            "keep this exact order!",
            currentCut.Text,
            "A partial segment cut must filter boundary words while preserving punctuation and source order.");
        TestAssert.Equal(
            1,
            currentCut.Spans.Count,
            "The partial cut must retain one exact timed segment span.");
        TestAssert.Equal(
            cutStart,
            currentCut.Spans[0].SourceStart,
            "A word crossing the cut start must be clipped to the exact Studio boundary.");
        TestAssert.Equal(
            cutEnd,
            currentCut.Spans[0].SourceEnd,
            "A word crossing the cut end must be clipped to the exact Studio boundary.");
    }

    private static Task EditorialTranscriptsSupportExtendedStudioCuts()
    {
        string source = TestMediaFactory.CreateSourcePath(
            "extended-cut-editorial-caption-source.mkv");
        TimeSpan trackStart = TimeSpan.FromSeconds(100);
        var track = GenerationCandidateCaptionTrack.RestoreStudioHandoff(
            "extended-cut-editorial-caption",
            "extended-cut-editorial-neighborhood",
            new GenerationCaptionSourceSelection(
                source,
                1,
                CaptionAudioContentRole.CreatorCommentary),
            GenerationCaptionStylePreset.Clean,
            trackStart,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(200),
            [
                new AudioTranscriptionSegment(
                    "extended-cut-first",
                    "extended-cut-editorial-neighborhood",
                    "First retained phrase.",
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(101),
                    TimeSpan.FromSeconds(102)),
                new AudioTranscriptionSegment(
                    "extended-cut-last",
                    "extended-cut-editorial-neighborhood",
                    "Last retained phrase!",
                    TimeSpan.FromSeconds(18),
                    TimeSpan.FromSeconds(19),
                    TimeSpan.FromSeconds(118),
                    TimeSpan.FromSeconds(119)),
            ],
            isUserEdited: true,
            GenerationCaptionSuppressionReason.None);

        ClipEditorialTranscriptContext exact =
            RetainedCaptionEditorialTranscriptProjector.Project(
                track,
                trackStart,
                TimeSpan.FromSeconds(120))
            .Single();
        ClipEditorialTranscriptContext extended =
            RetainedCaptionEditorialTranscriptProjector.Project(
                track,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(160))
            .Single();
        AssertEquivalentEditorialTranscript(
            exact,
            extended,
            "Studio cut extended beyond the caption window");
        TestAssert.Equal(
            0,
            RetainedCaptionEditorialTranscriptProjector.Project(
                track,
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(80))
            .Length,
            "A valid Studio cut entirely before the retained caption window must project no transcript.");
        TestAssert.Equal(
            0,
            RetainedCaptionEditorialTranscriptProjector.Project(
                track,
                TimeSpan.FromSeconds(130),
                TimeSpan.FromSeconds(150))
            .Length,
            "A valid Studio cut entirely after the retained caption window must project no transcript.");
        return Task.CompletedTask;
    }

    private static void AssertEquivalentEditorialTranscript(
        ClipEditorialTranscriptContext expected,
        ClipEditorialTranscriptContext actual,
        string route)
    {
        TestAssert.Equal(
            expected.AbsoluteAudioStreamIndex,
            actual.AbsoluteAudioStreamIndex,
            $"{route} must retain the same caption audio stream.");
        TestAssert.Equal(
            expected.Role.Role,
            actual.Role.Role,
            $"{route} must retain the same audio-content role.");
        TestAssert.Equal(
            expected.Role.Source,
            actual.Role.Source,
            $"{route} must retain the same role provenance.");
        TestAssert.Equal(
            expected.Text,
            actual.Text,
            $"{route} must retain the same bounded transcript text.");
        TestAssert.Equal(
            expected.Authority,
            actual.Authority,
            $"{route} must retain the same transcript authority.");
        TestAssert.Equal(
            expected.Spans.Count,
            actual.Spans.Count,
            $"{route} must retain the same timed-span count.");
        for (int index = 0; index < expected.Spans.Count; index++)
        {
            TestAssert.Equal(
                expected.Spans[index].SourceStart,
                actual.Spans[index].SourceStart,
                $"{route} span {index + 1} must retain its exact source start.");
            TestAssert.Equal(
                expected.Spans[index].SourceEnd,
                actual.Spans[index].SourceEnd,
                $"{route} span {index + 1} must retain its exact source end.");
            TestAssert.Equal(
                expected.Spans[index].Text,
                actual.Spans[index].Text,
                $"{route} span {index + 1} must retain its punctuation and source order.");
        }
    }

    private static Task CaptionLanguagePolicyIsExplicit()
    {
        string source = TestMediaFactory.CreateSourcePath(
            "caption-language.mkv");
        var selection = new GenerationCaptionSourceSelection(
            source,
            2,
            CaptionAudioContentRole.MixedSpeech,
            GenerationCaptionLanguagePolicy.Spanish);
        AudioTranscriptionOptions defaults =
            AudioTranscriptionOptions.CreateDefaults();
        AudioTranscriptionOptions explicitSpanish =
            defaults.WithLanguage(
                AudioTranscriptionLanguageMode.Explicit,
                new AudioTranscriptionLanguage("es", "Spanish"));

        TestAssert.Equal(
            GenerationCaptionLanguagePolicy.Spanish,
            selection.LanguagePolicy,
            "The user-selected language must remain source-specific.");
        TestAssert.Equal(
            AudioTranscriptionLanguageMode.Explicit,
            explicitSpanish.LanguageMode,
            "The derived request must use explicit language mode.");
        TestAssert.Equal(
            "es",
            explicitSpanish.RequestedLanguage!.Code,
            "The derived request must retain the exact language code.");
        TestAssert.Equal(
            AudioTranscriptionLanguageMode.Auto,
            defaults.LanguageMode,
            "Deriving options must not mutate the shared defaults.");
        TestAssert.Equal(
            defaults.MaximumProcessDuration,
            explicitSpanish.MaximumProcessDuration,
            "Language derivation must preserve every unrelated process bound.");
        return Task.CompletedTask;
    }

    private static Task InvalidWhisperWordsFallBackToPhraseTiming()
    {
        TimeSpan relativeStart = TimeSpan.FromSeconds(10.06);
        TimeSpan relativeEnd = TimeSpan.FromSeconds(12.06);
        TimeSpan absoluteStart = TimeSpan.FromSeconds(11.06);
        TimeSpan absoluteEnd = TimeSpan.FromSeconds(13.06);
        string[] textWords =
        [
            "Okay,",
            "see?",
            "This",
            "time",
            "I",
            "worked,",
            "what",
            "the",
            "hell?",
        ];
        var words = textWords.Select(text =>
            new AudioTranscriptionWord(
                text,
                relativeStart,
                relativeStart,
                absoluteStart,
                absoluteStart));
        var segment = new AudioTranscriptionSegment(
            "zero-length-whisper-words",
            "caption-neighborhood",
            "Okay, see? This time I worked, what the hell?",
            relativeStart,
            relativeEnd,
            absoluteStart,
            absoluteEnd,
            words);

        TestAssert.False(
            StudioCaptionPresentationPolicy
                .HasCompleteTimedWordCoverage(segment),
            "Zero-length provider timestamps must not be reported as exact word timing.");
        TestAssert.False(
            StudioCaptionPresentationPolicy
                .HasPresentationTimedWordCoverage(segment),
            "Zero-length provider timestamps must not be promoted to presentation word timing.");
        IReadOnlyList<StudioCaptionCue> cues =
            StudioCaptionPresentationPolicy.ProjectCues(
                segment,
                StudioCaptionWordLimitPreset.Streamlined);
        TestAssert.Equal(
            1,
            cues.Count,
            "Invalid word timing must keep one truthful phrase instead of inventing short-form word pages.");
        TestAssert.Equal(
            0,
            cues.Single().Words.Count,
            "Phrase fallback must not expose fabricated word spans to preview or render code.");
        TestAssert.Equal(
            relativeStart,
            cues[0].RelativeStart,
            "Phrase fallback must retain the provider's segment start.");
        TestAssert.Equal(
            relativeEnd,
            cues[^1].RelativeEnd,
            "Phrase fallback must retain the provider's segment end.");
        TestAssert.Equal(
            absoluteStart,
            cues[0].AbsoluteSourceStart,
            "Phrase fallback must preserve its absolute source start.");
        TestAssert.Equal(
            absoluteEnd,
            cues[^1].AbsoluteSourceEnd,
            "Phrase fallback must preserve its absolute source end.");
        TestAssert.Equal(
            "Okay, see? This time I worked, what the hell?",
            cues.Single().Text,
            "Phrase fallback must preserve every retained caption word and punctuation mark.");

        TimeSpan absoluteOffset = TimeSpan.FromSeconds(1);
        AudioTranscriptionWord AnchoredWord(
            string text,
            double start,
            double end) => new(
                text,
                TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(end),
                absoluteOffset + TimeSpan.FromSeconds(start),
                absoluteOffset + TimeSpan.FromSeconds(end));
        var partiallyInvalid = new AudioTranscriptionSegment(
            "partially-invalid-whisper-words",
            segment.NeighborhoodId,
            "kept rebuilt anchored",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(11),
            TimeSpan.FromSeconds(13),
            [
                AnchoredWord("kept", 10.1, 10.4),
                AnchoredWord("rebuilt", 10.4, 10.4),
                AnchoredWord("anchored", 11.5, 11.8),
            ]);
        IReadOnlyList<StudioCaptionCue> partiallyInvalidCues =
            StudioCaptionPresentationPolicy
            .ProjectCues(
                partiallyInvalid,
                StudioCaptionWordLimitPreset.FullSegment);
        TestAssert.Equal(
            2,
            partiallyInvalidCues.Count,
            "A long measured pause must isolate the affected phrase from the later fully timed speech run.");
        TestAssert.Equal(
            0,
            partiallyInvalidCues[0].Words.Count,
            "The affected speech run must fall back atomically without rebuilding only the bad word.");
        TestAssert.Equal(
            "kept rebuilt",
            partiallyInvalidCues[0].Text,
            "Localized fallback must retain every word from the affected speech run.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(10.1),
            partiallyInvalidCues[0].RelativeStart,
            "Localized fallback must begin at the affected run's first measured word.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(10.4),
            partiallyInvalidCues[0].RelativeEnd,
            "Localized fallback must stop at the affected run's measured envelope instead of spanning the provider segment.");
        TestAssert.Equal(
            "anchored",
            partiallyInvalidCues[1].Text,
            "A later fully timed speech run must remain independently animated.");
        TestAssert.Equal(
            1,
            partiallyInvalidCues[1].Words.Count,
            "Valid timing after the pause must not be downgraded by an earlier invalid word.");

        string source = TestMediaFactory.CreateSourcePath(
            "zero-length-caption-source.mkv");
        var track = GenerationCandidateCaptionTrack.RestoreStudioHandoff(
            "zero-length-caption-candidate",
            segment.NeighborhoodId,
            new GenerationCaptionSourceSelection(
                source,
                1,
                CaptionAudioContentRole.CreatorCommentary),
            GenerationCaptionStylePreset.KaraokeSweep,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(30),
            [segment],
            isUserEdited: false,
            GenerationCaptionSuppressionReason.None);
        TestAssert.False(
            track.HasTimedWords,
            "The retained caption contract must not claim word timing when provider timestamps have no duration.");
        foreach (GenerationCaptionStylePreset style in new[]
        {
            GenerationCaptionStylePreset.KaraokeSweep,
            GenerationCaptionStylePreset.WordFocus,
            GenerationCaptionStylePreset.Pop,
        })
        {
            AssSubtitleDocument document = AssSubtitleDocumentBuilder.Build(
                track.WithRequestedStyle(style),
                1080,
                1920,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(20),
                captionWordLimit: StudioCaptionWordLimitPreset.Streamlined);
            TestAssert.True(
                document.Warnings.Any(static warning =>
                    warning.Contains(
                        "could not be aligned reliably",
                        StringComparison.OrdinalIgnoreCase) &&
                    warning.Contains(
                        "whole phrase",
                        StringComparison.OrdinalIgnoreCase)),
                "Every word-animated style must clearly disclose phrase fallback in plain language.");
            TestAssert.False(
                document.Warnings.Any(static warning => warning.Contains(
                    "rebuilt",
                    StringComparison.OrdinalIgnoreCase)),
                "Render warnings must never imply that Replay Foundry reconstructed accurate word timing.");
            TestAssert.Equal(
                1,
                document.Script.Split("Dialogue: ").Length - 1,
                "Invalid timing must render one phrase-level event instead of synthetic word events.");
        }
        return Task.CompletedTask;
    }

    private static Task PartiallyInvalidWhisperTimingStaysInsideSpeechRun()
    {
        TimeSpan sourceWindowStart = TimeSpan.FromSeconds(100);
        AudioTranscriptionWord Word(
            string text,
            double start,
            double end) => new(
                text,
                TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(end),
                sourceWindowStart + TimeSpan.FromSeconds(start),
                sourceWindowStart + TimeSpan.FromSeconds(end));
        AudioTranscriptionWord[] words =
        [
            Word("Oh,", 1.44, 1.78),
            Word("no.", 1.78, 2.25),
            Word("That's", 6.31, 6.48),
            Word("not", 6.48, 6.71),
            Word("good.", 6.74, 7.12),
            Word("Who's", 11.51, 11.74),
            Word("they?", 11.89, 12.31),
            Word("Who's", 12.68, 12.89),
            Word("she", 12.89, 13.01),
            Word("talking", 13.01, 13.49),
            Word("to?", 13.49, 13.76),
            Word("The", 32.05, 32.05),
            Word("gun", 32.05, 32.44),
            Word("sword?", 32.45, 32.97),
            Word("What", 32.97, 33.11),
        ];
        var segment = new AudioTranscriptionSegment(
            "review-moment-partial-word-timing",
            "caption-review-moment",
            "Oh, no. That's not good. Who's they? Who's she talking to? The gun sword? What",
            TimeSpan.FromSeconds(1.36),
            TimeSpan.FromSeconds(36.43),
            sourceWindowStart + TimeSpan.FromSeconds(1.36),
            sourceWindowStart + TimeSpan.FromSeconds(36.43),
            words);

        IReadOnlyList<StudioCaptionCue> cues =
            StudioCaptionPresentationPolicy.ProjectCues(
                segment,
                StudioCaptionWordLimitPreset.Punchy);

        TestAssert.Equal(
            6,
            cues.Count,
            "The real Review Moment pattern must retain four earlier timed Punchy pages, one localized fallback, and the timed remainder.");
        TestAssert.True(
            cues.Take(4).All(static cue => cue.Words.Count > 0),
            "Every fully timed speech run before the bad provider word must keep exact word animation.");
        StudioCaptionCue fallback = cues[^2];
        TestAssert.Equal(
            "The gun",
            fallback.Text,
            "The zero-duration word must absorb only its nearest measured neighbor.");
        TestAssert.Equal(
            0,
            fallback.Words.Count,
            "The localized fallback must not expose fabricated word spans.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(32.05),
            fallback.RelativeStart,
            "The fallback phrase must begin at its observed local boundary.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(32.44),
            fallback.RelativeEnd,
            "The fallback phrase must stop at its measured neighbor instead of consuming the timed remainder.");
        StudioCaptionCue timedRemainder = cues[^1];
        TestAssert.Equal(
            "sword? What",
            timedRemainder.Text,
            "Valid words after the local failure must remain a separate Punchy cue.");
        TestAssert.Equal(
            2,
            timedRemainder.Words.Count,
            "Valid remainder words must retain their exact karaoke timing.");
        TestAssert.True(
            timedRemainder.Words.Count <= 3,
            "A localized fallback must not reintroduce an oversized Punchy page.");
        foreach (double silencePosition in new[] { 3d, 8d, 20d })
        {
            TimeSpan position = TimeSpan.FromSeconds(silencePosition);
            TestAssert.False(
                cues.Any(cue =>
                    cue.RelativeStart <= position &&
                    cue.RelativeEnd > position),
                $"No caption may remain visible at {silencePosition:0.##} seconds inside a measured pause.");
        }

        string source = TestMediaFactory.CreateSourcePath(
            "review-moment-partial-caption-source.mkv");
        var track = GenerationCandidateCaptionTrack.RestoreStudioHandoff(
            "review-moment-partial-caption",
            segment.NeighborhoodId,
            new GenerationCaptionSourceSelection(
                source,
                1,
                CaptionAudioContentRole.CreatorCommentary),
            GenerationCaptionStylePreset.KaraokeSweep,
            sourceWindowStart,
            TimeSpan.FromSeconds(40),
            TimeSpan.FromSeconds(200),
            [segment],
            isUserEdited: false,
            GenerationCaptionSuppressionReason.None);
        var calculator = new StudioLiveCaptionFrameCalculator();
        LiveCaptionFrameState silenceFrame = calculator.Calculate(
            track,
            StudioCaptionWordLimitPreset.Punchy,
            GenerationCaptionStylePreset.KaraokeSweep,
            sourceWindowStart.TotalSeconds + 20,
            sourceWindowStart,
            sourceWindowStart + TimeSpan.FromSeconds(40));
        TestAssert.Null(
            silenceFrame.Text,
            "Studio preview must stay empty during the retained eighteen-second silence.");
        LiveCaptionFrameState fallbackFrame = calculator.Calculate(
            track,
            StudioCaptionWordLimitPreset.Punchy,
            GenerationCaptionStylePreset.KaraokeSweep,
            sourceWindowStart.TotalSeconds + 32.2,
            sourceWindowStart,
            sourceWindowStart + TimeSpan.FromSeconds(40));
        TestAssert.Equal(
            fallback.Text,
            fallbackFrame.Text!,
            "Studio preview must use the same localized fallback cue as final rendering.");

        AssSubtitleDocument document = AssSubtitleDocumentBuilder.Build(
            track,
            1080,
            1920,
            sourceWindowStart,
            TimeSpan.FromSeconds(40),
            captionWordLimit: StudioCaptionWordLimitPreset.Punchy);
        TestAssert.False(
            document.Script.Contains(
                "0:00:01.36,0:00:36.43",
                StringComparison.Ordinal),
            "Final ASS must never restore the obsolete whole-provider-segment fallback interval.");
        TestAssert.True(
            document.Script.Contains(
                "0:00:32.05,0:00:32.44",
                StringComparison.Ordinal) &&
            document.Script.Contains(
                "The gun",
                StringComparison.Ordinal),
            "Final ASS must burn only the localized phrase interval for the affected words.");
        TestAssert.True(
            document.Script.Contains(
                "0:00:32.45,0:00:32.97",
                StringComparison.Ordinal) &&
            document.Script.Contains(
                "0:00:32.97,0:00:33.11",
                StringComparison.Ordinal) &&
            document.Script.Contains(
                "sword?",
                StringComparison.Ordinal) &&
            document.Script.Contains(
                "What",
                StringComparison.Ordinal),
            "Final ASS must preserve both exact word intervals in the timed remainder after the localized fallback.");
        return Task.CompletedTask;
    }

    private static Task RepetitiveCaptionsAreSuppressed()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        AudioTranscriptionResult transcription =
            CreateTranscription(
                candidate,
                ["the echo repeats", "the echo repeats", "the echo repeats"]);
        GenerationCaptionSuppressionReason reason =
            GenerationCaptionTranscriptQuality.Assess(transcription);
        var suppressed = new GenerationCandidateCaptionTrack(
            candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep,
            transcription,
            segments: [],
            suppressionReason: reason);
        AudioTranscriptionSegment correctedSegment =
            transcription.Segments[0];
        GenerationCandidateCaptionTrack corrected =
            suppressed.WithEditedSegments([correctedSegment]);

        TestAssert.Equal(
            GenerationCaptionSuppressionReason
                .RepetitiveLowInformationTranscript,
            reason,
            "Repeated low-information text must be identified generically.");
        TestAssert.True(
            suppressed.IsSuppressed,
            "The render track must expose its typed suppression reason.");
        TestAssert.Equal(
            0,
            suppressed.Segments.Count,
            "Suppressed text must not remain renderable.");
        TestAssert.Equal(
            3,
            suppressed.Transcription.Segments.Count,
            "Raw provider output must remain available for diagnostics.");
        TestAssert.False(
            corrected.IsSuppressed,
            "An explicit user correction restores a renderable track.");
        return Task.CompletedTask;
    }

    private static Task SuppressedCaptionsDoNotReachStudio()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        AudioTranscriptionResult transcription =
            CreateTranscription(
                candidate,
                ["looped words", "looped words", "looped words"]);
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep,
            transcription,
            segments: [],
            suppressionReason:
                GenerationCaptionSuppressionReason
                    .RepetitiveLowInformationTranscript);
        var captions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            [track],
            TimeSpan.FromSeconds(1));

        GenerationOutputAsset asset =
            fixture.CreateDraft(captions).PrimaryAsset;

        TestAssert.True(
            asset.Captions is null,
            "Suppressed provider text must not reach Studio or final rendering.");
        return Task.CompletedTask;
    }

    private static Task StudioCaptionLookAppliesToAll()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            scoreSets: [[96, 91]],
            captionsEnabled: true);
        GenerationCandidateCaptionTrack[] tracks = fixture.Moments.SelectedCandidates
            .Select(candidate => new GenerationCandidateCaptionTrack(
                candidate,
                fixture.GenerationRequest.SetupOptions.CaptionSettings
                    .FindForSource(
                        candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
                GenerationCaptionStylePreset.KaraokeSweep,
                CreateTranscription(candidate)))
            .ToArray();
        var captions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            tracks,
            TimeSpan.Zero);
        GenerationOutputProject project = fixture.CreateDraft(captions);
        IReadOnlyDictionary<string, GenerationCandidateCaptionTrack>
            originalCaptionTracks = project.Assets
                .Where(static asset => asset.HasCaptions)
                .ToDictionary(
                    static asset => asset.Id,
                    static asset => asset.Captions!,
                    StringComparer.Ordinal);
        var session = new GenerationOutputSession();
        session.Publish(project);
        int changeCount = 0;
        session.CurrentChanged += (_, _) => changeCount++;
        var editor = new StudioClipEditorViewModel(session);
        editor.Bind(project, project.PrimaryAsset);
        editor.CaptionVerticalPositionPercent = 36;
        editor.CaptionMaximumWidthPercent = 62;
        editor.CaptionFontScalePercent = 135;
        editor.SelectedCaptionWordLimit = editor.CaptionWordLimitOptions.Single(
            static option =>
                option.Value == StudioCaptionWordLimitPreset.Punchy);
        editor.SelectedCaptionStyle = editor.CaptionStyleOptions.Single(
            static option =>
                option.Value == GenerationCaptionStylePreset.Pop);

        TestAssert.True(
            editor.ApplyCaptionLookToAllCommand.CanExecute(null),
            "A multi-clip caption project should enable the batch caption-look action.");
        TestAssert.False(
            editor.IsCaptionPhraseSizeEditorEnabled,
            "Pop should disable the phrase-size menu because the effect always shows one word.");
        TestAssert.False(
            editor.IsCaptionPhraseSizeSelectorVisible,
            "Pop must hide the saved 3/5/8 phrase value instead of presenting it as an active choice.");
        TestAssert.True(
            editor.IsPopCaptionPhraseSizeLocked,
            "Pop must replace the phrase selector with its explicit locked state.");
        TestAssert.Equal(
            "One word · set by Pop",
            editor.PopCaptionPhraseSizeText,
            "The locked Pop state must state the actual visible grouping.");
        TestAssert.True(
            editor.SelectedCaptionPhraseSizeDescription.Contains(
                "one spoken word",
                StringComparison.Ordinal),
            "Studio should explain why Pop owns its visible word count.");
        editor.ApplyCaptionLookToAllCommand.Execute(null);

        TestAssert.Equal(1, changeCount,
            "The aggregate edit should publish one atomic project replacement.");
        TestAssert.True(
            session.Current!.Assets
                .Where(static asset => asset.HasCaptions)
                .All(asset => Math.Abs(
                    asset.Appearance.CaptionVerticalPositionPercent - 36) < 0.01),
            "Every captioned clip should receive the selected vertical position.");
        TestAssert.True(
            session.Current.Assets
                .Where(static asset => asset.HasCaptions)
                .All(asset => Math.Abs(
                    asset.Appearance.CaptionMaximumWidthPercent - 62) < 0.01),
            "Every captioned clip should receive the selected maximum width.");
        TestAssert.True(
            session.Current.Assets
                .Where(static asset => asset.HasCaptions)
                .All(asset => Math.Abs(
                    asset.Appearance.CaptionFontScalePercent - 135) < 0.01),
            "Every captioned clip should receive the selected text size.");
        TestAssert.True(
            session.Current.Assets
                .Where(static asset => asset.HasCaptions)
                .All(asset =>
                    asset.Appearance.CaptionStyle ==
                        GenerationCaptionStylePreset.Pop),
            "Every captioned clip should receive the selected caption effect.");
        TestAssert.True(
            session.Current.Assets
                .Where(static asset => asset.HasCaptions)
                .All(asset =>
                    asset.Appearance.CaptionWordLimit ==
                        StudioCaptionWordLimitPreset.Punchy),
            "Every captioned clip should retain the copied phrase grouping for effects that use it.");

        string sourcePath = session.Current.PrimaryAsset.SourceMedia.FullPath;
        File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);
        try
        {
            var store = new JsonStudioProjectStore(
                Path.Combine(fixture.Root, "projects"));
            store.Save(session.Current, revision: 1);
            StudioProjectLoadResult reloaded =
                store.Load(session.Current.Id);
            TestAssert.Equal(
                StudioProjectLoadOutcome.Loaded,
                reloaded.Outcome,
                "The batch-applied caption look must survive the durable Studio project boundary.");
            TestAssert.True(
                reloaded.Project!.Assets
                    .Where(static asset => asset.HasCaptions)
                    .All(asset =>
                        asset.Appearance.CaptionWordLimit ==
                            StudioCaptionWordLimitPreset.Punchy),
                "Every captioned clip must still be Punchy after save and reload.");
        }
        finally
        {
            File.Delete(sourcePath);
        }
        foreach (GenerationOutputAsset updated in session.Current.Assets
                     .Where(static asset => asset.HasCaptions))
        {
            GenerationCandidateCaptionTrack original =
                originalCaptionTracks[updated.Id];
            TestAssert.True(
                updated.Captions!.Segments.SequenceEqual(original.Segments) &&
                updated.Captions.Segments
                    .SelectMany(static segment => segment.Words)
                    .SequenceEqual(original.Segments.SelectMany(
                        static segment => segment.Words)) &&
                updated.Captions.SourceWindowStart ==
                    original.SourceWindowStart &&
                updated.Captions.SourceWindowDuration ==
                    original.SourceWindowDuration,
                "Applying a caption look must preserve the exact segment and word timing graph owned by each clip.");
        }
        editor.SelectedCaptionStyle = editor.CaptionStyleOptions.Single(
            static option =>
                option.Value ==
                    GenerationCaptionStylePreset.KaraokeSweep);
        TestAssert.True(
            editor.IsCaptionPhraseSizeSelectorVisible &&
            editor.IsCaptionPhraseSizeEditorEnabled,
            "Returning to a phrase-based effect must restore the editable phrase-size selector.");
        TestAssert.Equal(
            StudioCaptionWordLimitPreset.Punchy,
            editor.SelectedCaptionWordLimit.Value,
            "Leaving Pop must restore the saved phrase size without changing it.");
        TestAssert.True(
            editor.SelectedCaptionPhraseSizeDescription.Contains(
                "word sweep animates",
                StringComparison.Ordinal),
            "The phrase-size explanation must distinguish Karaoke animation from static segmentation.");
        return Task.CompletedTask;
    }

    private static async Task StudioProjectSwitchProtectsManualSaveDrafts()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep,
            CreateTranscription(candidate));
        var captions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            [track],
            TimeSpan.Zero);
        GenerationOutputProject project = fixture.CreateDraft(captions);
        string graphicPath = Path.Combine(fixture.Root, "switch-graphic.png");
        File.WriteAllBytes(graphicPath, [0x00]);
        GenerationOutputAsset asset = project.PrimaryAsset;
        StudioClipAppearance current = asset.Appearance;
        var appearance = new StudioClipAppearance(
            current.CaptionStyle,
            current.CaptionVerticalPositionPercent,
            current.VideoEffect,
            current.VideoEffectIntensityPercent,
            [new StudioGraphicOverlay(
                "switch-graphic",
                graphicPath,
                50,
                50,
                30)],
            current.CaptionWordLimit,
            current.CaptionMaximumWidthPercent,
            current.CaptionFontScalePercent);
        project = project.ReplaceAsset(asset.WithStudioEdits(
            asset.SourceStart,
            asset.SourceEnd,
            appearance));
        GenerationOutputProject target = fixture.CreateDraft(captions);

        await AssertSwitchBlocked(
            project,
            target,
            studio =>
            {
                studio.Inspector.Caption.Segments[0].Text =
                    "manually corrected caption";
                studio.Inspector.Caption.Segments[0].StartSeconds += 0.1;
                TestAssert.True(
                    studio.Inspector.Caption.HasUnsavedChanges,
                    "Caption text and timing edits must be tracked as an unsaved manual draft.");
            },
            "caption");
        await AssertSwitchBlocked(
            project,
            target,
            studio =>
            {
                studio.Inspector.Graphics.CenterXPercent = 63;
                TestAssert.True(
                    studio.Inspector.Graphics.HasUnsavedChanges,
                    "Graphic placement must remain dirty until Apply is used.");
            },
            "graphic");
        await AssertSwitchBlocked(
            project,
            target,
            studio =>
            {
                studio.Inspector.Editorial.NamingGuidance =
                    "Prefer the action before the location.";
                TestAssert.True(
                    studio.Inspector.Editorial.HasUnsavedProfileChanges,
                    "Reusable wording must remain dirty until its explicit save action is used.");
            },
            "writing preferences");

        static async Task AssertSwitchBlocked(
            GenerationOutputProject source,
            GenerationOutputProject destination,
            Action<StudioViewModel> mutate,
            string expectedMessageTerm)
        {
            var session = new GenerationOutputSession();
            session.Publish(source);
            using var studio = new StudioViewModel(session);
            mutate(studio);

            StudioProjectSwitchResult result =
                await studio.TrySwitchProjectAsync(destination);

            TestAssert.Equal(
                StudioProjectSwitchOutcome.BlockedUnsavedDraft,
                result.Outcome,
                "A manual-save draft must block project switching instead of being silently discarded.");
            TestAssert.True(
                result.Message.Contains(
                    expectedMessageTerm,
                    StringComparison.OrdinalIgnoreCase),
                "The blocked switch must identify the save action that remains.");
            TestAssert.Equal(
                source.Id,
                session.Current?.Id,
                "A blocked switch must leave the original Studio project active.");
        }
    }

    private static async Task StudioProjectSwitchPreservesSimultaneousDrafts()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(
            candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings
                .FindForSource(
                    candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep,
            CreateTranscription(candidate));
        var captions = new GenerationCaptionPreparationResult(
            fixture.Moments,
            [track],
            TimeSpan.Zero);
        GenerationOutputProject project = fixture.CreateDraft(captions);
        string graphicPath = Path.Combine(
            fixture.Root,
            "combined-switch-graphic.png");
        File.WriteAllBytes(graphicPath, [0x00]);
        GenerationOutputAsset asset = project.PrimaryAsset;
        StudioClipAppearance current = asset.Appearance;
        project = project.ReplaceAsset(asset.WithStudioEdits(
            asset.SourceStart,
            asset.SourceEnd,
            new StudioClipAppearance(
                current.CaptionStyle,
                current.CaptionVerticalPositionPercent,
                current.VideoEffect,
                current.VideoEffectIntensityPercent,
                [new StudioGraphicOverlay(
                    "combined-switch-graphic",
                    graphicPath,
                    50,
                    50,
                    30)],
                current.CaptionWordLimit,
                current.CaptionMaximumWidthPercent,
                current.CaptionFontScalePercent)));
        GenerationOutputProject target = fixture.CreateDraft(captions);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var studio = new StudioViewModel(
            session,
            session,
            new UnusedSwitchRenderer(),
            new ClipEditorialMetadataGenerationService(
                new HeuristicClipEditorialMetadataGenerator()),
            new ClipEditorialProfileSession());

        studio.Inspector.Caption.Segments[0].Text =
            "combined manual caption";
        studio.Inspector.Graphics.CenterXPercent = 63;
        studio.Inspector.Clip.SelectedVideoEffect =
            studio.Inspector.Clip.VideoEffectOptions.Single(option =>
                option.Value == StudioVideoEffectPreset.Noir);
        studio.Inspector.Clip.StartAdjustmentSeconds = -2;
        studio.Inspector.Clip.CaptionMaximumWidthPercent = 60;
        studio.Inspector.Clip.VideoEffectIntensityPercent = 72;
        studio.Inspector.Editorial.NamingGuidance =
            "Prefer the concrete action.";

        StudioProjectSwitchResult blocked =
            await studio.TrySwitchProjectAsync(target);
        TestAssert.Equal(
            StudioProjectSwitchOutcome.BlockedUnsavedDraft,
            blocked.Outcome,
            "Any simultaneous manual draft must block the project switch.");

        await Task.Delay(650);
        TestAssert.Equal(
            StudioVideoEffectPreset.None,
            session.Current!.PrimaryAsset.Appearance.VideoEffect,
            "A blocked switch must cancel the delayed appearance commit.");

        studio.Inspector.Caption.SaveCommand.Execute(null);
        TestAssert.False(
            studio.Inspector.Caption.HasUnsavedChanges,
            "The explicitly saved caption draft should become current.");
        AssertRemainingDrafts(studio);
        TestAssert.Equal(
            project.PrimaryAsset.SourceStart.TotalSeconds - 2,
            studio.Preview.PreviewPositionMinimumSeconds,
            "Preview must retain the restored pending clip start after a same-project rebind.");
        StudioCaptionFrameLayout pendingLayout =
            StudioCaptionPresentationPolicy.CalculateFrameLayout(
                checked((int)studio.Preview.PreviewCanvasWidth),
                checked((int)studio.Preview.PreviewCanvasHeight),
                studio.Inspector.Clip.SelectedCaptionStyle.Value,
                60,
                studio.Inspector.Clip.CaptionFontScalePercent);
        TestAssert.Equal(
            (double)pendingLayout.MaximumWidthPixels,
            studio.Preview.LiveCaptionMaximumWidthPixels,
            "Preview must retain the restored pending caption width after a same-project rebind.");

        studio.Inspector.Clip.ApplyPendingEdit();
        TestAssert.False(
            studio.Inspector.Clip.HasPendingEdit,
            "The explicitly applied clip appearance should become current.");
        TestAssert.True(
            studio.Inspector.Graphics.HasUnsavedChanges,
            "Applying clip appearance must preserve the graphic placement draft.");
        TestAssert.True(
            studio.Inspector.Editorial.HasUnsavedProfileChanges,
            "Applying clip appearance must preserve reusable wording.");

        studio.Inspector.Graphics.ApplyPlacementCommand.Execute(null);
        studio.Inspector.Editorial.SaveProfileCommand.Execute(null);
        StudioProjectSwitchResult switched =
            await studio.TrySwitchProjectAsync(target);
        TestAssert.True(
            switched.Succeeded,
            "The switch should succeed after every retained draft is explicitly saved.");
        TestAssert.Equal(
            target.Id,
            session.Current?.Id,
            "The requested recent project should become active after safe saves.");

        static void AssertRemainingDrafts(StudioViewModel studio)
        {
            TestAssert.True(
                studio.Inspector.Graphics.HasUnsavedChanges,
                "Saving captions must preserve unsaved graphic placement.");
            TestAssert.Equal(
                63d,
                studio.Inspector.Graphics.CenterXPercent,
                "The exact graphic placement draft must survive the rebind.");
            TestAssert.True(
                studio.Inspector.Clip.HasPendingEdit,
                "Saving captions must preserve pending clip appearance.");
            TestAssert.Equal(
                -2d,
                studio.Inspector.Clip.StartAdjustmentSeconds,
                "The exact pending trim must survive the rebind.");
            TestAssert.Equal(
                60d,
                studio.Inspector.Clip.CaptionMaximumWidthPercent,
                "The exact pending caption width must survive the rebind.");
            TestAssert.Equal(
                StudioVideoEffectPreset.Noir,
                studio.Inspector.Clip.SelectedVideoEffect.Value,
                "The exact pending video treatment must survive the rebind.");
            TestAssert.Equal(
                72d,
                studio.Inspector.Clip.VideoEffectIntensityPercent,
                "The exact pending video intensity must survive the rebind.");
            TestAssert.True(
                studio.Inspector.Editorial.HasUnsavedProfileChanges,
                "Saving captions must preserve reusable wording.");
            TestAssert.Equal(
                "Prefer the concrete action.",
                studio.Inspector.Editorial.NamingGuidance,
                "The exact reusable wording draft must survive the rebind.");
        }
    }

    private sealed class UnusedSwitchRenderer :
        IStudioProjectRenderingService
    {
        public Task<StudioProjectRenderResult> FinalizeAsync(
            GenerationOutputProject draft,
            IProgress<StudioProjectRenderProgress> progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The project-switch regression test must not render media.");

        public void AcceptCompletedRender(
            StudioProjectRenderResult result) =>
            throw new InvalidOperationException(
                "The project-switch regression test must not accept media.");

        public void DiscardCompletedRender(
            StudioProjectRenderResult result) =>
            throw new InvalidOperationException(
                "The project-switch regression test must not discard media.");
    }

    private static Task NonSpeechTokensDoNotRender()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        AudioTranscriptionResult mixed = CreateTranscription(
            candidate,
            ["[Music]", "spoken words", "[BLANK_AUDIO]"]);
        AudioTranscriptionSegment[] renderable =
            GenerationCaptionTranscriptQuality.SelectRenderableSegments(mixed);

        TestAssert.Equal(
            1,
            renderable.Length,
            "Only lexical provider segments may enter the creator-facing caption track.");
        TestAssert.Equal(
            "spoken words",
            renderable[0].Text,
            "Filtering must preserve the lexical provider text verbatim.");
        TestAssert.Equal(
            3,
            mixed.Segments.Count,
            "Filtering display captions must not alter raw transcription provenance.");
        TestAssert.Equal(
            GenerationCaptionSuppressionReason.None,
            GenerationCaptionTranscriptQuality.Assess(mixed),
            "A mixed transcript remains renderable through its lexical segments.");

        AudioTranscriptionResult nonSpeechOnly = CreateTranscription(
            candidate,
            ["[Music]", "[BLANK_AUDIO]"]);
        TestAssert.Equal(
            GenerationCaptionSuppressionReason.NonSpeechOnlyTranscript,
            GenerationCaptionTranscriptQuality.Assess(nonSpeechOnly),
            "A provider result containing only non-speech labels must be typed as suppressed.");
        TestAssert.Equal(
            0,
            GenerationCaptionTranscriptQuality
                .SelectRenderableSegments(nonSpeechOnly).Length,
            "No bracketed diagnostic token may be burned into rendered video.");
        return Task.CompletedTask;
    }

    private static Task LowConfidenceSparseSegmentsDoNotRender()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        AudioTranscriptionResult baseline = CreateTranscription(candidate);
        TimeSpan sourceStart = baseline.Manifest.AbsoluteSourceOffset;
        TimeSpan sparseStart = TimeSpan.FromSeconds(10);
        TimeSpan sparseEnd = TimeSpan.FromSeconds(18);
        var lowProbabilityWord = new AudioTranscriptionWord(
            "if",
            sparseStart,
            sparseEnd,
            sourceStart + sparseStart,
            sourceStart + sparseEnd,
            providerReportedProbability: 0.03);
        var sparse = new AudioTranscriptionSegment(
            "sparse-segment",
            baseline.NeighborhoodId,
            "if",
            sparseStart,
            sparseEnd,
            sourceStart + sparseStart,
            sourceStart + sparseEnd,
            [lowProbabilityWord]);
        AudioTranscriptionSegment grounded = baseline.Segments[0];
        var transcription = new AudioTranscriptionResult(
            baseline.NeighborhoodId,
            baseline.AbsoluteAudioStreamIndex,
            [grounded, sparse],
            baseline.Manifest,
            baseline.DetectedLanguage,
            baseline.Warnings);

        AudioTranscriptionSegment[] renderable =
            GenerationCaptionTranscriptQuality.SelectRenderableSegments(
                transcription);

        TestAssert.Equal(
            1,
            renderable.Length,
            "The sparse low-probability segment must not enter display captions.");
        TestAssert.Equal(
            grounded.Id,
            renderable[0].Id,
            "Filtering must retain the grounded provider segment unchanged.");
        TestAssert.Equal(
            2,
            transcription.Segments.Count,
            "Raw provider provenance must remain complete.");
        return Task.CompletedTask;
    }

    private static Task AggregateWhisperCaptionDefersToFragments()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            captionsEnabled: true);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        AudioTranscriptionResult baseline = CreateTranscription(candidate);
        TimeSpan sourceStart = baseline.Manifest.AbsoluteSourceOffset;

        AudioTranscriptionSegment Segment(
            string id,
            string text,
            double start,
            double end) => new(
                id,
                baseline.NeighborhoodId,
                text,
                TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(end),
                sourceStart + TimeSpan.FromSeconds(start),
                sourceStart + TimeSpan.FromSeconds(end));

        AudioTranscriptionSegment aggregate = Segment(
            "aggregate",
            "Okay, this time it worked, what happened?",
            10,
            20);
        AudioTranscriptionSegment firstFragment = Segment(
            "fragment-1",
            "Okay, this time it worked.",
            20,
            22);
        AudioTranscriptionSegment secondFragment = Segment(
            "fragment-2",
            "What happened?",
            22,
            24);
        var transcription = new AudioTranscriptionResult(
            baseline.NeighborhoodId,
            baseline.AbsoluteAudioStreamIndex,
            [aggregate, firstFragment, secondFragment],
            baseline.Manifest,
            baseline.DetectedLanguage,
            baseline.Warnings);

        AudioTranscriptionSegment[] renderable =
            GenerationCaptionTranscriptQuality.SelectRenderableSegments(
                transcription);

        TestAssert.Equal(
            2,
            renderable.Length,
            "A broad aggregate immediately reconstructed by tighter fragments must not render twice.");
        TestAssert.Equal(
            "fragment-1",
            renderable[0].Id,
            "The first tighter provider fragment should retain its original identity and timing.");
        TestAssert.Equal(
            "fragment-2",
            renderable[1].Id,
            "The second tighter provider fragment should retain its original identity and timing.");
        TestAssert.Equal(
            3,
            transcription.Segments.Count,
            "Caption canonicalization must preserve complete raw provider provenance.");

        AudioTranscriptionSegment shortRepeat = Segment(
            "short-repeat",
            "Okay, this time it worked, what happened?",
            10,
            13);
        AudioTranscriptionSegment shortFirstFragment = Segment(
            "short-fragment-1",
            "Okay, this time it worked.",
            13,
            15);
        AudioTranscriptionSegment shortSecondFragment = Segment(
            "short-fragment-2",
            "What happened?",
            15,
            17);
        var legitimateRepeat = new AudioTranscriptionResult(
            baseline.NeighborhoodId,
            baseline.AbsoluteAudioStreamIndex,
            [shortRepeat, shortFirstFragment, shortSecondFragment],
            baseline.Manifest,
            baseline.DetectedLanguage,
            baseline.Warnings);
        TestAssert.Equal(
            3,
            GenerationCaptionTranscriptQuality
                .SelectRenderableSegments(legitimateRepeat).Length,
            "A normally timed repeated phrase must not be mistaken for a long aggregate timestamp defect.");
        return Task.CompletedTask;
    }

    private static Task VideoTreatmentsMapToFilters()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips);
        GenerationMomentCandidate candidate =
            fixture.Moments.SelectedCandidates[0];
        var expectedFilters = new Dictionary<StudioVideoEffectPreset, string>
        {
            [StudioVideoEffectPreset.Noir] = "curves=preset=increase_contrast",
            [StudioVideoEffectPreset.Chromatic] = "rgbashift=",
            [StudioVideoEffectPreset.SoftBloom] = "gblur=sigma=",
            [StudioVideoEffectPreset.Vivid] = "vibrance=intensity=",
        };

        foreach ((StudioVideoEffectPreset preset, string expected) in
                 expectedFilters)
        {
            FfmpegClipRenderCommand command =
                FfmpegClipRenderCommandBuilder.BuildSegment(
                    candidate.AnalyzedSource.PreparedSource.Media,
                    candidate.Candidate.Window.Start,
                    candidate.Candidate.Window.End,
                    GenerationClipOutputProfile.FromReference(
                        candidate.AnalyzedSource.PreparedSource.Media
                            .PrimaryVideoStream),
                    Path.Combine(fixture.Root, $"{preset}.mp4"),
                    videoEffect: preset,
                    videoEffectIntensityPercent: 50);
            string filter = command.Arguments[
                command.Arguments.ToList().IndexOf("-vf") + 1];
            TestAssert.True(
                filter.Contains(expected, StringComparison.Ordinal),
                $"{preset} must add its explicit FFmpeg treatment.");
            TestAssert.False(
                filter.Contains("eq=", StringComparison.Ordinal),
                $"{preset} must remain compatible with the pinned media runtime, which does not ship FFmpeg's eq filter.");
        }

        FfmpegClipRenderCommand neutral =
            FfmpegClipRenderCommandBuilder.BuildSegment(
                candidate.AnalyzedSource.PreparedSource.Media,
                candidate.Candidate.Window.Start,
                candidate.Candidate.Window.End,
                GenerationClipOutputProfile.FromReference(
                    candidate.AnalyzedSource.PreparedSource.Media
                        .PrimaryVideoStream),
                Path.Combine(fixture.Root, "neutral.mp4"));
        string neutralFilter = neutral.Arguments[
            neutral.Arguments.ToList().IndexOf("-vf") + 1];
        TestAssert.False(
            expectedFilters.Values.Any(
                token => neutralFilter.Contains(
                    token,
                    StringComparison.Ordinal)),
            "The neutral treatment must preserve the historical filter chain.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => FfmpegClipRenderCommandBuilder.BuildSegment(
                candidate.AnalyzedSource.PreparedSource.Media,
                candidate.Candidate.Window.Start,
                candidate.Candidate.Window.End,
                GenerationClipOutputProfile.FromReference(
                    candidate.AnalyzedSource.PreparedSource.Media
                        .PrimaryVideoStream),
                Path.Combine(fixture.Root, "invalid.mp4"),
                videoEffect: StudioVideoEffectPreset.Noir,
                videoEffectIntensityPercent: 101),
            "Treatment intensity must remain bounded.");
        return Task.CompletedTask;
    }

    private static Task StudioBoundaryContextIsBounded()
    {
        string source = TestMediaFactory.CreateSourcePath(
            "studio-boundary.mkv");
        var asset = new GenerationOutputAsset(
            "studio-1",
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
        TimeSpan start = TimeSpan.FromMinutes(2);
        TimeSpan end = TimeSpan.FromMinutes(5);
        StudioClipBoundaryPolicy.Validate(asset, start, end);
        TestAssert.Equal(
            TimeSpan.FromMinutes(-1),
            start - asset.OriginalSourceStart,
            "The start may move one minute earlier.");
        TestAssert.Equal(
            TimeSpan.FromMinutes(1),
            end - asset.OriginalSourceEnd,
            "The end may move one minute later.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () =>
                StudioClipBoundaryPolicy.Validate(
                    asset,
                    TimeSpan.FromSeconds(119),
                    TimeSpan.FromMinutes(5)),
            "An edit beyond one minute must be rejected.");
        return Task.CompletedTask;
    }

    private static Task GraphicOverlayContractsAreValidated()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        string image = Path.Combine(fixture.Root, "overlay.png");
        File.WriteAllBytes(image, [137, 80, 78, 71]);
        var first = new StudioGraphicOverlay("graphic-1", image, 25, 40, 30);
        var second = new StudioGraphicOverlay("graphic-2", image, 75, 60, 20);
        StudioGraphicOverlay[] callerOwned = [first, second];
        var appearance = new StudioClipAppearance(
            GenerationCaptionStylePreset.Clean,
            82,
            StudioVideoEffectPreset.None,
            0,
            callerOwned);
        callerOwned[0] = second;

        TestAssert.Equal("graphic-1", appearance.GraphicOverlays[0].Id, "Appearance must snapshot caller-owned overlays.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<StudioGraphicOverlay>)appearance.GraphicOverlays).Add(first),
            "Overlay collections must remain immutable.");
        TestAssert.Throws<ArgumentException>(
            () => new StudioClipAppearance(
                GenerationCaptionStylePreset.Clean,
                82,
                StudioVideoEffectPreset.None,
                0,
                [first, first]),
            "Duplicate case-insensitive overlay IDs must be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => first.WithPlacement(50, 50, 101),
            "Overlay width must remain bounded.");
        return Task.CompletedTask;
    }

    private static Task GraphicOverlaysEnterRenderGraph()
    {
        using PipelineFixture fixture = CreateFixture(
            GenerationMode.IndividualClips,
            hasAudio: true,
            audioStreamCount: 2,
            sourceName: "graphic-source.mkv");
        string image = Path.Combine(fixture.Root, "graphic with spaces.png");
        File.WriteAllBytes(image, [137, 80, 78, 71]);
        var overlay = new StudioGraphicOverlay("raw-id-[];,", image, 25, 75, 30);
        GenerationMomentCandidate candidate = fixture.Moments.SelectedCandidates[0];
        FfmpegClipRenderCommand command = FfmpegClipRenderCommandBuilder.BuildSegment(
            candidate.AnalyzedSource.PreparedSource.Media,
            candidate.Candidate.Window.Start,
            candidate.Candidate.Window.End,
            GenerationClipOutputProfile.FromReference(
                candidate.AnalyzedSource.PreparedSource.Media.PrimaryVideoStream),
            Path.Combine(fixture.Root, "graphic-output.mp4"),
            graphicOverlays: [overlay]);

        int filterIndex = command.Arguments.ToList().IndexOf("-filter_complex");
        string graph = command.Arguments[filterIndex + 1];
        TestAssert.True(command.Arguments.Contains(image), "The graphic path must remain one atomic input argument.");
        TestAssert.True(graph.Contains("overlay=x=", StringComparison.Ordinal), "The render graph must composite the validated overlay.");
        TestAssert.True(graph.Contains("[0:1][0:2]amix=inputs=2", StringComparison.Ordinal), "Graphics must not duplicate or remove source audio.");
        TestAssert.False(graph.Contains(image, StringComparison.Ordinal), "Raw graphic paths must never enter FFmpeg filter syntax.");
        TestAssert.False(graph.Contains(overlay.Id, StringComparison.Ordinal), "Raw graphic IDs must never enter FFmpeg syntax.");
        TestAssert.True(ContainsPair(command.Arguments, "-map", "[vstage1]"), "The composited video stream must be mapped exactly once.");
        return Task.CompletedTask;
    }

    private static Task GraphicEditorPersistsOverlay()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        GenerationOutputProject draft = fixture.CreateDraft();
        var session = new GenerationOutputSession();
        session.Publish(draft);
        var editor = new StudioGraphicOverlayEditorViewModel(session);
        editor.Bind(session.Current, session.Current!.PrimaryAsset);
        string image = Path.Combine(fixture.Root, "editor-graphic.png");
        File.WriteAllBytes(image, [137, 80, 78, 71]);

        TestAssert.True(editor.TryAddFile(image), "A validated dropped image should be accepted.");
        TestAssert.Equal(
            1,
            session.Current!.PrimaryAsset.Appearance.GraphicOverlays.Count,
            "The overlay must persist through GenerationOutputSession replacement.");
        TestAssert.Equal(
            draft.Id,
            session.Current.Id,
            "Adding a graphic must preserve the Studio project identity.");
        TestAssert.True(
            session.Current.PrimaryAsset.OutputFullPath is null,
            "Adding a graphic must remain nondestructive until final render.");
        return Task.CompletedTask;
    }

}
