using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static Task InitialPortraitCaptionsAvoidConfirmedPresenter()
    {
        var media = TestMediaFactory.Create(TestMediaFactory.CreateSourcePath("portrait-caption-placement.mkv"),
            width: 1080, height: 1920);
        CompositionRegion Region(string id, NormalizedRectangle rectangle, CompositionRegionRole role) =>
            new(id, rectangle, role, CompositionRegionTraits.Dynamic, CompositionConfidence.Certain,
                CompositionConfidence.Certain, CompositionValueSource.UserConfirmed, CompositionValueSource.UserConfirmed);
        var plan = ManualCompositionPlanFactory.CreateUserConfirmedSingleInterval(media.FullPath, media.Duration,
            [Region("gameplay", new(.1, .14, .8, .4), CompositionRegionRole.Gameplay),
             Region("presenter", new(.1, .69, .8, .25), CompositionRegionRole.Presenter)], DateTimeOffset.UnixEpoch);
        var render = StudioRenderSettings.FromComposition(plan, TimeSpan.Zero, media.PrimaryVideoStream);
        var appearance = StudioInitialCaptionPlacement.Create(GenerationCaptionStylePreset.WordFocus, plan,
            TimeSpan.Zero, TimeSpan.FromSeconds(30), media.PrimaryVideoStream, render);
        TestAssert.Equal(48d, appearance.CaptionVerticalPositionPercent,
            "A portrait recording with its presenter below gameplay should place initial captions near gameplay's bottom.");
        var savedLook = new StudioCaptionLook(GenerationCaptionStylePreset.Clean, 82,
            StudioCaptionWordLimitPreset.Punchy, 75, 110, new StudioCaptionTypography(safeArea: StudioCaptionSafeArea.TikTok));
        var saved = StudioInitialCaptionPlacement.Create(GenerationCaptionStylePreset.WordFocus, plan,
            TimeSpan.Zero, TimeSpan.FromSeconds(30), media.PrimaryVideoStream, render, savedLook);
        TestAssert.Equal(savedLook, StudioCaptionLook.FromAppearance(saved), "Named or explicitly saved looks are never repositioned automatically.");
        var landscape = TestMediaFactory.Create(media.FullPath, width: 1920, height: 1080);
        var stacked = StudioInitialCaptionPlacement.Create(GenerationCaptionStylePreset.WordFocus, plan,
            TimeSpan.Zero, TimeSpan.FromSeconds(30), landscape.PrimaryVideoStream,
            StudioRenderSettings.FromComposition(plan, TimeSpan.Zero, landscape.PrimaryVideoStream));
        TestAssert.Equal(82d, stacked.CaptionVerticalPositionPercent, "Landscape facecam-top output keeps its unobstructed default placement.");
        var unknown = ManualCompositionPlanFactory.CreateFullFrameGameplay(media.FullPath, media.Duration, DateTimeOffset.UnixEpoch);
        var unconfirmed = StudioInitialCaptionPlacement.Create(GenerationCaptionStylePreset.WordFocus, unknown,
            TimeSpan.Zero, TimeSpan.FromSeconds(30), media.PrimaryVideoStream, render);
        TestAssert.Equal(82d, unconfirmed.CaptionVerticalPositionPercent, "Unconfirmed composition cannot invent a presenter position.");
        return Task.CompletedTask;
    }

    private static Task CutCaptionsAgreeAcrossPreviewAndExports()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.Clean, CreateTranscription(candidate));
        TimeSpan start = candidate.Candidate.Window.Start + TimeSpan.FromSeconds(1.6);
        TimeSpan end = candidate.Candidate.Window.End;
        var projected = StudioCaptionCutProjection.Project(track, start, end);
        TestAssert.Equal("world", projected.Track.Segments.Single().Text,
            "A split phrase must display only measured words fully inside the new cut.");
        var preview = new StudioLiveCaptionFrameCalculator().Calculate(track, StudioCaptionWordLimitPreset.FullSegment,
            GenerationCaptionStylePreset.Clean, start.TotalSeconds + 0.1, start, end);
        TestAssert.Equal("world", preview.Text!, "Preview and exported captions must share the cut projection.");
        var ass = AssSubtitleDocumentBuilder.Build(track, 1080, 1920, start, end - start);
        TestAssert.False(ass.Script.Contains("hello", StringComparison.Ordinal), "Burned captions must omit words before the cut.");
        TestAssert.Equal("world", SubtitleSidecarSerializer.Project(track, start, end - start).Single().Text,
            "Sidecars must omit the same out-of-cut words as preview and final rendering.");
        TestAssert.Equal("hello world", track.Segments.Single().Text, "Projection must preserve editable source text for trim extension.");
        var segment = track.Segments.Single();
        var approximate = track.WithEditedSegments([new AudioTranscriptionSegment(segment.Id, segment.NeighborhoodId,
            segment.Text, segment.RelativeStart, segment.RelativeEnd, segment.AbsoluteSourceStart, segment.AbsoluteSourceEnd)]);
        var approximateAss = AssSubtitleDocumentBuilder.Build(approximate, 1080, 1920, start, end - start);
        TestAssert.True(approximateAss.Warnings.Contains(StudioCaptionCutProjection.ApproximateBoundaryWarning),
            "Approximate boundary phrases must be reviewable and explicitly warned, never assigned fabricated word timing.");
        TestAssert.Equal("hello world", SubtitleSidecarSerializer.Project(approximate, start, end - start).Single().Text,
            "Unaligned approximate text uses the same complete-phrase fallback in exports.");
        return Task.CompletedTask;
    }

    private static async Task SourceTranscriptCacheReusesBoundedChunks()
    {
        string sourcePath = Path.GetTempFileName();
        string modelPath = Path.GetTempFileName();
        try
        {
            var media = TestMediaFactory.Create(sourcePath, TimeSpan.FromMinutes(5), hasAudio: true);
            int stream = media.AudioStreams[0].Index;
            var extractor = new RecordingRetainedAudioExtractor();
            var provider = new RecordingRetainedTranscriptionProvider();
            var service = new SourceAudioTranscriptionService(extractor, provider,
                new AudioTranscriptionModelSettings(modelPath, "cache fixture", "ggml"));
            var options = AudioTranscriptionOptions.CreateDefaults();
            var first = await service.TranscribeSourceChunkAsync("source-reading", media, stream,
                TimeSpan.Zero, TimeSpan.FromMinutes(2), options, CancellationToken.None);
            var clip = await service.TranscribeWindowAsync("clip-reading", media, stream,
                TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3), options, CancellationToken.None);
            TestAssert.Equal(1, extractor.CleanupCount, "An overlapping caption cut reuses discovery transcription.");
            TestAssert.Equal("duckies", clip.Segments.Single().Text, "Only fully bounded words survive a partial segment cut.");
            TestAssert.Equal(TimeSpan.Zero, clip.Segments.Single().RelativeStart, "Projected words are rebased to the new cut.");
            TestAssert.Equal(first.Manifest.SourceManifests.Single(), clip.Manifest.SourceManifests.Single(),
                "Projection retains the original inference provenance, not a fabricated second execution.");
            await service.TranscribeWindowAsync("vocabulary-change", media, stream, TimeSpan.Zero,
                TimeSpan.FromSeconds(5), options.WithInitialPrompt("duckies"), CancellationToken.None);
            TestAssert.Equal(2, extractor.CleanupCount, "Vocabulary corrections invalidate cached inference.");
            await service.TranscribeSourceChunkAsync("second-chunk", media, stream, TimeSpan.FromMinutes(2),
                TimeSpan.FromMinutes(4), options, CancellationToken.None);
            TestAssert.Equal(3, extractor.CleanupCount, "Each distinct bounded source chunk is transcribed once.");
            File.AppendAllText(sourcePath, "changed");
            await service.TranscribeWindowAsync("source-change", media, stream, TimeSpan.Zero,
                TimeSpan.FromSeconds(5), options, CancellationToken.None);
            TestAssert.Equal(4, extractor.CleanupCount, "Source identity changes invalidate cached text.");
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(modelPath);
        }
    }

    private static Task SemanticTranscriptContextRespectsCut()
    {
        var source = new GenerationSourceTranscript("source", 1,
            [new AudioTranscriptionSegment("clipped", "source", "Never include this entire partial sentence",
                TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromSeconds(10)),
             new AudioTranscriptionSegment("inside", "source", "Here is a complete grounded spoken thought",
                TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(18), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(18))], []);
        var context = GenerationVisualTranscriptContextBuilder.Build(source, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));
        TestAssert.Equal(VisualSemanticTranscriptContextPolicy.FullContextV1, context.Policy,
            "Available lexical content must reach semantic analysis through the full-context contract.");
        TestAssert.Equal(1, context.Spans.Count, "Approximate partial segments cannot leak text outside the review.");
        TestAssert.Equal(TimeSpan.FromSeconds(7), context.Spans.Single().ReviewRelativeStart,
            "Semantic transcript context follows the materialized review timeline.");
        return Task.CompletedTask;
    }

    private static async Task ApproximateTranscriptRecoversExactCut()
    {
        string sourcePath = Path.GetTempFileName();
        string modelPath = Path.GetTempFileName();
        try
        {
            var source = TestMediaFactory.Create(sourcePath, TimeSpan.FromMinutes(4), hasAudio: true);
            var extractor = new RecordingRetainedAudioExtractor();
            var service = new SourceAudioTranscriptionService(extractor,
                new RecordingRetainedTranscriptionProvider { EmitWords = false },
                new AudioTranscriptionModelSettings(modelPath, "approximate fixture", "ggml"));
            var options = AudioTranscriptionOptions.CreateDefaults();
            await service.TranscribeSourceChunkAsync("discovery", source, source.AudioStreams[0].Index,
                TimeSpan.Zero, TimeSpan.FromMinutes(2), options, CancellationToken.None);
            var result = await service.TranscribeWindowAsync("recovered-cut", source, source.AudioStreams[0].Index,
                TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4), options, CancellationToken.None);
            TestAssert.Equal(2, extractor.CleanupCount, "Approximate boundary speech requires one exact-cut inference recovery.");
            TestAssert.Equal(1, result.Segments.Count, "Recovery must retain speech that would be lost by chunk clipping.");
            TestAssert.Equal(TimeSpan.FromSeconds(1.5), result.Manifest.SourceManifests.Single().AbsoluteSourceOffset,
                "Recovered captions must preserve the exact-cut execution provenance.");
            await service.TranscribeWindowAsync("same-recovered-cut", source, source.AudioStreams[0].Index,
                TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4), options, CancellationToken.None);
            TestAssert.Equal(2, extractor.CleanupCount, "The recovered exact cut is cached as well.");
        }
        finally { File.Delete(sourcePath); File.Delete(modelPath); }
    }

    private static async Task CaptionCacheMissReadsOnlyExactCut()
    {
        string sourcePath = Path.GetTempFileName();
        string modelPath = Path.GetTempFileName();
        try
        {
            var source = TestMediaFactory.Create(sourcePath, TimeSpan.FromMinutes(20), hasAudio: true);
            var extractor = new RecordingRetainedAudioExtractor();
            var diagnostics = new List<SourceTranscriptDiagnostic>();
            var service = new SourceAudioTranscriptionService(extractor,
                new RecordingRetainedTranscriptionProvider { EmitWords = false },
                new AudioTranscriptionModelSettings(modelPath, "exact caption fixture", "ggml"), diagnostics.Add);
            var options = AudioTranscriptionOptions.CreateDefaults();
            var first = await service.TranscribeWindowAsync("caption-only", source, source.AudioStreams[0].Index,
                TimeSpan.FromSeconds(264), TimeSpan.FromSeconds(302), options, CancellationToken.None);
            TestAssert.Equal(1, extractor.CleanupCount, "Caption-only inference must not first transcribe a two-minute chunk.");
            TestAssert.Equal(TimeSpan.FromSeconds(38), first.Manifest.SourceManifests.Single().InputDuration,
                "A cache miss pays for the requested 38-second cut only.");
            TestAssert.True(diagnostics.All(static diagnostic => diagnostic.Stage == "exact-window"),
                "Caption-only diagnostics must show exact inference, not source discovery or double inference recovery.");
            await service.TranscribeWindowAsync("caption-again", source, source.AudioStreams[0].Index,
                TimeSpan.FromSeconds(264), TimeSpan.FromSeconds(302), options, CancellationToken.None);
            TestAssert.Equal(1, extractor.CleanupCount, "Repeated captions reuse the exact cut independent of candidate identity.");
            TestAssert.True(diagnostics.Last().CacheHit, "Memoized exact inference is reported accurately.");
        }
        finally { File.Delete(sourcePath); File.Delete(modelPath); }
    }

    private static Task ManualClipCanReachUnselectedSourceAndPersist()
    {
        string firstPath = Path.GetTempFileName();
        string secondPath = Path.GetTempFileName();
        try
        {
            var first = TestMediaFactory.Create(firstPath, TimeSpan.FromMinutes(20));
            var second = TestMediaFactory.Create(secondPath, TimeSpan.FromMinutes(20));
            var asset = new GenerationOutputAsset("suggested", 1, first, null, TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(40), 80, 70, GenerationCandidateSelectionReason.QualityQualified, "Fixture candidate.",
                renderSettings: new StudioRenderSettings(StudioOutputCanvas.Portrait));
            var project = new GenerationOutputProject("manual-project", GenerationMode.IndividualClips,
                Path.GetTempPath(), 1, ClipFulfillmentPreference.QualityFirst,
                GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget, [asset], DateTimeOffset.UtcNow,
                sourceMedia: [first, second]);
            var session = new GenerationOutputSession();
            session.Publish(project);
            using var editor = new StudioManualClipViewModel(session, null, () => true);
            editor.Bind(project);
            editor.SelectedSource = editor.Sources.Single(value => value.FullPath == secondPath);
            editor.StartText = "00:15:00";
            editor.EndText = "00:15:30";
            TestAssert.True(editor.AddCommand.CanExecute(null), "Any inspected source is available even if no original candidate used it.");
            editor.AddCommand.Execute(null);
            GenerationOutputProject updated = session.Current!;
            var manual = updated.Assets.Last();
            TestAssert.Equal(TimeSpan.FromMinutes(15), manual.SourceStart, "Manual creation reaches well beyond every suggested cut.");
            TestAssert.Equal(GenerationCandidateSelectionReason.ManualSourceCut, manual.SelectionReason, "Manual cuts have an honest distinct selection reason.");
            TestAssert.True(manual.PreferenceFeatures is null && manual.Score == 0, "Manual cuts must not fabricate learned or heuristic evidence.");
            TestAssert.Equal(StudioOutputCanvas.Portrait, manual.RenderSettings.Canvas, "Manual clips inherit the chosen output format.");
            var restored = StudioProjectDocumentMapper.Restore(StudioProjectDocumentMapper.Capture(updated, 1, DateTimeOffset.UtcNow));
            TestAssert.Equal(2, restored.SourceMedia.Count, "The complete source list survives Studio save/reopen.");
            TestAssert.Equal(TimeSpan.FromMinutes(15), restored.Assets.Last().SourceStart, "The manual cut survives Studio save/reopen.");
        }
        finally { File.Delete(firstPath); File.Delete(secondPath); }
        return Task.CompletedTask;
    }
}
