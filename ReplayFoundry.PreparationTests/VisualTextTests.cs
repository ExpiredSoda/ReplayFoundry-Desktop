using ReplayFoundry.Desktop.Features.Generate.Editorial.VisualText;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.PreparationTests;

internal static class VisualTextTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Visual text contracts are immutable and provenance bound", ContractsAreImmutable),
        new("Visual text stability requires separate sampled frames", StabilityRequiresSeparateFrames),
        new("Visual text grounding authority matches exact wire wording", GroundingAuthorityMatchesWireWording),
        new("Visual text sampling preserves priority and bounded timeline coverage", SamplingIsBounded),
        new("Visual text service reuses preview frames and enriches editorial context", ServiceEnrichesContext),
        new("Windows OCR exposes installed-language availability without downloads", WindowsProviderReportsAvailability),
    ];

    private static Task ContractsAreImmutable()
    {
        VisualTextAnchor anchor = new(
            "storm gate",
            "Storm Gate",
            VisualTextAnchorAuthority.RepeatedAcrossFrames,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
        TestAssert.True(anchor.MayGroundAudienceCopy, "Repeated authority.");
        TestAssert.True(
            anchor.EvidenceId.StartsWith("visual-text-", StringComparison.Ordinal),
            "Stable evidence identity.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<TimeSpan>)anchor.SourceTimestamps).Add(TimeSpan.Zero),
            "Anchor timestamps must be immutable.");
        TestAssert.Throws<ArgumentException>(
            () => _ = new VisualTextAnchor(
                "storm gate",
                "Storm Gate",
                VisualTextAnchorAuthority.RepeatedAcrossFrames,
                [TimeSpan.FromSeconds(1)]),
            "Repeated authority requires separate frames.");
        return Task.CompletedTask;
    }

    private static Task StabilityRequiresSeparateFrames()
    {
        VisualTextFrameObservation[] observations =
        [
            FrameObservation(TimeSpan.FromSeconds(11), "Storm Gate", "42"),
            FrameObservation(TimeSpan.FromSeconds(12), "storm gate", "43"),
            FrameObservation(TimeSpan.FromSeconds(13), "One Frame Only"),
        ];
        VisualTextAnchor[] anchors =
            GenerationVisualTextAnalysisService.BuildAnchors(observations);

        VisualTextAnchor stable = anchors.Single(value =>
            value.NormalizedText == "storm gate");
        VisualTextAnchor diagnostic = anchors.Single(value =>
            value.NormalizedText == "one frame only");
        TestAssert.True(stable.MayGroundAudienceCopy, "Repeated text authority.");
        VisualTextAnchor repeatedWord = new(
            "storm",
            "Storm",
            VisualTextAnchorAuthority.RepeatedAcrossFrames,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)],
            VisualTextAnchorSourceKind.Word);
        TestAssert.False(
            repeatedWord.MayGroundAudienceCopy,
            "A repeated word without line context remains diagnostic.");
        TestAssert.False(
            diagnostic.MayGroundAudienceCopy,
            "One-frame text remains diagnostic.");
        TestAssert.False(
            anchors.Any(value => value.NormalizedText == "42"),
            "Pure counters must not become anchors.");
        return Task.CompletedTask;
    }

    private static Task GroundingAuthorityMatchesWireWording()
    {
        TimeSpan[] timestamps =
        [
            TimeSpan.FromSeconds(11),
            TimeSpan.FromSeconds(12),
        ];
        VisualTextAnchor authorized = new(
            "objective updated",
            "Objective Updated",
            VisualTextAnchorAuthority.RepeatedAcrossFrames,
            timestamps);
        VisualTextAnchor punctuationSeparated = new(
            "mission updated",
            "MISSION:UPDATED",
            VisualTextAnchorAuthority.RepeatedAcrossFrames,
            timestamps);

        TestAssert.True(
            authorized.MayGroundAudienceCopy,
            "Two exact whitespace-separated display words may ground copy.");
        TestAssert.False(
            punctuationSeparated.MayGroundAudienceCopy,
            "Normalization must not turn punctuation into wire-level grounding authority.");

        string sourcePath = TestMediaFactory.CreateSourcePath(
            "visual-text-authority.mkv");
        var context = new ClipVisualTextContext(
            "candidate-visual-text-authority",
            sourcePath,
            NormalizedRectangle.FullFrame,
            frames: [],
            anchors: [punctuationSeparated, authorized]);
        TestAssert.Equal(
            1,
            context.GroundingAnchors.Count,
            "Only wire-authorized anchors belong in the grounding collection.");
        TestAssert.Equal(
            "Objective Updated",
            context.GroundingAnchors[0].DisplayText,
            "The exact display text crossing the wire must retain authority.");
        TestAssert.True(
            context.Anchors.Any(anchor =>
                anchor.DisplayText == "MISSION:UPDATED" &&
                !anchor.MayGroundAudienceCopy),
            "Punctuation-separated OCR remains available as diagnostic evidence.");
        return Task.CompletedTask;
    }

    private static Task SamplingIsBounded()
    {
        GenerationVisualTextAnalysisRequest request = CreateRequest(
            [TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(19)]);
        IReadOnlyList<TimeSpan> samples =
            GenerationVisualTextAnalysisService.BuildSampleTimestamps(request);

        TestAssert.True(samples.Contains(TimeSpan.FromSeconds(12)), "Priority sample.");
        TestAssert.True(samples.Contains(TimeSpan.FromSeconds(19)), "Second priority.");
        TestAssert.True(samples.All(value =>
            value >= request.Context.SourceStart &&
            value < request.Context.SourceEnd), "Exclusive clip bounds.");
        TestAssert.True(
            samples.SequenceEqual(samples.OrderBy(static value => value)),
            "Deterministic sample order.");
        TestAssert.True(
            samples.Count <= GenerationVisualTextAnalysisService.MaximumSampleCount,
            "Bounded sample count.");
        return Task.CompletedTask;
    }

    private static async Task ServiceEnrichesContext()
    {
        GenerationVisualTextAnalysisRequest request = CreateRequest();
        var preview = new FakePreviewProvider();
        var provider = new FakeVisualTextProvider();
        var service = new GenerationVisualTextAnalysisService(preview, provider);

        ClipEditorialContext result = await service.EnrichAsync(
            request,
            CancellationToken.None);

        TestAssert.True(result.VisualText is not null, "Visual text result.");
        TestAssert.Equal(
            preview.Calls,
            provider.Calls,
            "One OCR invocation per extracted preview frame.");
        TestAssert.Equal(
            GenerationVisualTextAnalysisService.BuildSampleTimestamps(request).Count,
            preview.Calls,
            "One shared preview decode per sample.");
        TestAssert.True(
            result.VisualText!.GroundingAnchors.Any(value =>
                value.NormalizedText == "storm gate"),
            "Repeated frame text should be available for local grounding.");
        TestAssert.True(
            preview.Regions.All(value => ReferenceEquals(
                request.Context.GameplayRegion,
                value)),
            "Preview extraction must use confirmed Gameplay geometry.");
    }

    private static Task WindowsProviderReportsAvailability()
    {
        var provider = new ReplayFoundry.Desktop.Platform.VisualText
            .WindowsMediaOcrProvider();
        TestAssert.Equal(
            "ReplayFoundry.WindowsMediaOcrProvider",
            provider.Name,
            "Provider identity.");
        TestAssert.True(
            provider.IsAvailable,
            "The current validation machine has an installed Windows OCR language.");
        return Task.CompletedTask;
    }

    private static GenerationVisualTextAnalysisRequest CreateRequest(
        IEnumerable<TimeSpan>? priorities = null)
    {
        MediaProbeResult media = TestMediaFactory.Create(
            TestMediaFactory.CreateSourcePath("ocr-source.mkv"),
            TimeSpan.FromSeconds(40));
        var context = new ClipEditorialContext(
            "candidate-ocr",
            media.FullPath,
            "Example",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            media.Duration,
            70,
            "Deterministic evidence.",
            gameplayRegion: new NormalizedRectangle(.075, .125, .85, .425));
        return new GenerationVisualTextAnalysisRequest(context, media, priorities);
    }

    private static VisualTextFrameObservation FrameObservation(
        TimeSpan timestamp,
        params string[] lines)
    {
        MediaProbeResult media = TestMediaFactory.Create(
            TestMediaFactory.CreateSourcePath("ocr-observation.mkv"),
            TimeSpan.FromSeconds(40));
        var frame = new VideoPreviewFrame(
            media.FullPath,
            media.Duration,
            media.PrimaryVideoStream.Index,
            timestamp,
            decodedTimestamp: null,
            1280,
            720,
            CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop,
            [1],
            Manifest());
        return new VisualTextFrameObservation(
            new VisualTextFrameRequest(frame),
            Identity(),
            lines.Select(line => new VisualTextLine(
                line,
                line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select((word, index) => new VisualTextWord(
                        word,
                        new VisualTextBoundingBox(
                            index * .1,
                            .1,
                            .08,
                            .05))))),
            TimeSpan.FromMilliseconds(2));
    }

    private static VideoPreviewFrameManifest Manifest() => new(
        "Fake preview",
        "1.0",
        "ffmpeg",
        "test",
        TestMediaFactory.CreateSourcePath("ffmpeg.exe"),
        DateTimeOffset.UnixEpoch,
        TimeSpan.Zero);

    private static VisualTextProviderIdentity Identity() => new(
        "Fake OCR",
        "1.0",
        "CPU",
        "test",
        "en-US");

    private sealed class FakePreviewProvider : IVideoPreviewFrameProvider
    {
        public int Calls { get; private set; }
        public List<NormalizedRectangle> Regions { get; } = [];

        public Task<VideoPreviewFrame> GetFrameAsync(
            VideoPreviewFrameRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Regions.Add(request.ContentRegion!);
            return Task.FromResult(new VideoPreviewFrame(
                request.Media.FullPath,
                request.Media.Duration,
                request.Media.PrimaryVideoStream.Index,
                request.Timestamp,
                decodedTimestamp: null,
                1280,
                720,
                CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop,
                [1],
                Manifest()));
        }
    }

    private sealed class FakeVisualTextProvider : IVisualTextProvider
    {
        public string Name => "Fake OCR";
        public string Version => "1.0";
        public bool IsAvailable => true;
        public int Calls { get; private set; }

        public Task<VisualTextFrameObservation> RecognizeAsync(
            VisualTextFrameRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new VisualTextFrameObservation(
                request,
                Identity(),
                [new VisualTextLine(
                    "Storm Gate",
                    [new VisualTextWord(
                        "Storm",
                        new VisualTextBoundingBox(.1, .1, .2, .1)),
                     new VisualTextWord(
                        "Gate",
                        new VisualTextBoundingBox(.31, .1, .15, .1))])],
                TimeSpan.FromMilliseconds(1)));
        }
    }
}
