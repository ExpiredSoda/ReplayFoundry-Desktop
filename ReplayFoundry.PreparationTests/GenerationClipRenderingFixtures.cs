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
    private static AudioTranscriptionResult CreateTranscription(
        GenerationMomentCandidate candidate)
    {
        TimeSpan sourceStart = candidate.Candidate.Window.Start;
        TimeSpan duration = candidate.Candidate.Window.Duration;
        string neighborhoodId = "caption-" + candidate.Id;
        var words = new[]
        {
            new AudioTranscriptionWord(
                "hello",
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1.6),
                sourceStart + TimeSpan.FromSeconds(1),
                sourceStart + TimeSpan.FromSeconds(1.6)),
            new AudioTranscriptionWord(
                "world",
                TimeSpan.FromSeconds(1.6),
                TimeSpan.FromSeconds(2.3),
                sourceStart + TimeSpan.FromSeconds(1.6),
                sourceStart + TimeSpan.FromSeconds(2.3)),
        };
        var segment = new AudioTranscriptionSegment(
            "segment-1",
            neighborhoodId,
            "hello world",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2.3),
            sourceStart + TimeSpan.FromSeconds(1),
            sourceStart + TimeSpan.FromSeconds(2.3),
            words);
        string path = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-test-model.bin");
        var model = new ModelArtifactManifest(
            "test model",
            path,
            new string('A', 64),
            1,
            DateTimeOffset.UnixEpoch,
            "test");
        var options = new AudioTranscriptionOptions(
            AudioTranscriptionLanguageMode.Auto,
            null,
            false,
            true,
            true,
            0,
            null,
            AudioTranscriptionProcessorHint.Cpu,
            TimeSpan.FromMinutes(1),
            AudioTranscriptionOutputFormatPolicy.StructuredJson);
        var execution = new InferenceExecutionManifest(
            new InferenceProviderIdentity("test", "1", "1"),
            Path.Combine(Path.GetTempPath(), "whisper.exe"),
            new string('B', 64),
            "test version",
            model,
            new Dictionary<string, string>(),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            TimeSpan.Zero,
            false);
        var manifest = new AudioTranscriptionManifest(
            neighborhoodId,
            duration,
            sourceStart,
            candidate.AnalyzedSource.PreparedSource.Media.Duration,
            candidate.AnalyzedSource.PreparedSource.Media.AudioStreams[0].Index,
            options,
            execution);
        return new AudioTranscriptionResult(
            neighborhoodId,
            candidate.AnalyzedSource.PreparedSource.Media.AudioStreams[0].Index,
            [segment],
            manifest,
            new AudioTranscriptionLanguage("en"));
    }

    private static AudioTranscriptionResult CreateTranscription(
        GenerationMomentCandidate candidate,
        IReadOnlyList<string> segmentTexts)
    {
        AudioTranscriptionResult baseline = CreateTranscription(candidate);
        AudioTranscriptionSegment[] segments = segmentTexts
            .Select((text, index) =>
            {
                TimeSpan relativeStart =
                    TimeSpan.FromSeconds(index * 2 + 1);
                TimeSpan relativeEnd = relativeStart +
                    TimeSpan.FromSeconds(1);
                return new AudioTranscriptionSegment(
                    $"segment-{index + 1}",
                    baseline.NeighborhoodId,
                    text,
                    relativeStart,
                    relativeEnd,
                    baseline.Manifest.AbsoluteSourceOffset + relativeStart,
                    baseline.Manifest.AbsoluteSourceOffset + relativeEnd);
            })
            .ToArray();
        return new AudioTranscriptionResult(
            baseline.NeighborhoodId,
            baseline.AbsoluteAudioStreamIndex,
            segments,
            baseline.Manifest,
            baseline.DetectedLanguage,
            baseline.Warnings);
    }

    private static AudioTranscriptionResult CreateTimedTranscription(
        GenerationMomentCandidate candidate,
        IReadOnlyList<string> wordTexts)
    {
        if (wordTexts.Count == 0)
        {
            throw new ArgumentException(
                "A timed test transcription requires at least one word.",
                nameof(wordTexts));
        }
        AudioTranscriptionResult baseline = CreateTranscription(candidate);
        TimeSpan relativeStart = TimeSpan.FromSeconds(1);
        TimeSpan wordDuration = TimeSpan.FromMilliseconds(350);
        AudioTranscriptionWord[] words = wordTexts
            .Select((text, index) =>
            {
                TimeSpan start = relativeStart + wordDuration * index;
                TimeSpan end = start + wordDuration;
                return new AudioTranscriptionWord(
                    text,
                    start,
                    end,
                    baseline.Manifest.AbsoluteSourceOffset + start,
                    baseline.Manifest.AbsoluteSourceOffset + end);
            })
            .ToArray();
        TimeSpan relativeEnd = words[^1].RelativeEnd;
        var segment = new AudioTranscriptionSegment(
            "long-timed-segment",
            baseline.NeighborhoodId,
            string.Join(" ", wordTexts),
            relativeStart,
            relativeEnd,
            baseline.Manifest.AbsoluteSourceOffset + relativeStart,
            baseline.Manifest.AbsoluteSourceOffset + relativeEnd,
            words);
        return new AudioTranscriptionResult(
            baseline.NeighborhoodId,
            baseline.AbsoluteAudioStreamIndex,
            [segment],
            baseline.Manifest,
            baseline.DetectedLanguage,
            baseline.Warnings);
    }

    private static PipelineFixture CreateFixture(
        GenerationMode mode,
        bool hasAudio = true,
        int audioStreamCount = 1,
        string sourceName = "render-source.mkv",
        IReadOnlyList<IReadOnlyList<double>>? scoreSets = null,
        bool captionsEnabled = false)
    {
        string root = CreateRoot();
        string source = TestMediaFactory.CreateSourcePath(sourceName);
        var selected = new SelectedVideoSource(
            source,
            isReference: true);
        var preparationRequest =
            new GenerationSourcePreparationRequest([selected]);
        var preparation =
            new GenerationSourcePreparationResult(
                preparationRequest,
                [
                    new PreparedGenerationSource(
                        selected,
                        TestMediaFactory.Create(
                            source,
                            hasAudio: hasAudio,
                            audioStreamCount: audioStreamCount),
                        TestMediaFactory.CreateSnapshot(source)),
                ]);
        GenerationSetupOptions setup =
            PreparedGenerationWorkflowTests.CreateOptions(mode);
        setup = new GenerationSetupOptions(
            setup.Mode,
            setup.DetectionMethod,
            setup.AudioSelectionMode,
            setup.DesiredResultCount,
            setup.QualityThreshold,
            setup.ContentEmphasis,
            setup.ClipFulfillmentPreference,
            setup.MomentGuidance,
            setup.CaptionSettings,
            setup.ResultCountMode,
            GenerationAnalysisDepth.Fast);
        if (captionsEnabled)
        {
            setup = new GenerationSetupOptions(
                setup.Mode,
                setup.DetectionMethod,
                setup.AudioSelectionMode,
                setup.DesiredResultCount,
                setup.QualityThreshold,
                setup.ContentEmphasis,
                setup.ClipFulfillmentPreference,
                setup.MomentGuidance,
                new GenerationCaptionSettings(
                    isEnabled: true,
                    GenerationCaptionStylePreset.KaraokeSweep,
                    [
                        new GenerationCaptionSourceSelection(
                            preparation.ReferenceSource.Media.FullPath,
                            preparation.ReferenceSource.Media.AudioStreams[0].Index,
                            CaptionAudioContentRole.CreatorCommentary),
                    ]),
                setup.ResultCountMode,
                GenerationAnalysisDepth.Fast);
        }
        GenerationRequest generation =
            PreparedGenerationWorkflowTests.CreateGenerationRequest(
                preparation,
                setup);
        var momentService = new GenerationMomentFindingService(
            new GenerationMomentFindingTests.RecordingMomentFinder(
                scoreSets ?? [[95]]));
        GenerationMomentFindingResult moments =
            momentService.Find(
                new GenerationMomentFindingRequest(
                    generation.EvidenceAnalysis,
                    generation.SetupOptions));
        return new(
            root,
            generation,
            momentService,
            moments);
    }

    private static bool ContainsPair(
        IReadOnlyList<string> arguments,
        string first,
        string second)
    {
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == first &&
                arguments[index + 1] == second)
            {
                return true;
            }
        }
        return false;
    }

    private static int Count(
        IReadOnlyList<string> arguments,
        string value) =>
        arguments.Count(argument => argument == value);

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundryRenderingTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void AssertNoStaging(string root) =>
        TestAssert.False(
            Directory.EnumerateDirectories(
                root,
                "*.rendering-*",
                SearchOption.TopDirectoryOnly).Any(),
            "No run-owned staging directory may remain.");

    private sealed class PipelineFixture : IDisposable
    {
        private readonly EnvironmentVariableScope _environment;

        public PipelineFixture(
            string root,
            GenerationRequest generationRequest,
            GenerationMomentFindingService momentService,
            GenerationMomentFindingResult moments)
        {
            Root = root;
            GenerationRequest = generationRequest;
            MomentService = momentService;
            Moments = moments;
            string executable = Path.Combine(root, "ffmpeg.exe");
            File.WriteAllText(executable, "test");
            _environment = new(
                "REPLAYFOUNDRY_FFMPEG_PATH",
                executable);
            FinalDirectory = Path.Combine(root, "published clips");
        }

        public string Root { get; }
        public string FinalDirectory { get; }
        public GenerationRequest GenerationRequest { get; }
        public GenerationMomentFindingService MomentService { get; }
        public GenerationMomentFindingResult Moments { get; }

        public GenerationOutputProject CreateDraft(
            GenerationCaptionPreparationResult? captions = null) =>
            GenerationOutputProject.FromResult(
                new GenerationResult(
                    GenerationRequest,
                    Moments,
                    captions),
                FinalDirectory);

        public FfmpegStudioProjectRenderingService CreateStudioRenderer(
            IProcessRunner runner) =>
            new(
                runner,
                new FfmpegToolLocator());

        public void Dispose()
        {
            _environment.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FixedOutputPathProvider(
        string path) : IGenerationOutputPathProvider
    {
        public string CreateOutputDirectoryPath(
            GenerationMomentFindingResult moments) => path;
    }

    private sealed class WritingProcessRunner(
        int? failOnCall = null,
        int? cancelOnCall = null,
        CancellationTokenSource? cancellation = null) : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            int call = Requests.Count;
            if (call == cancelOnCall)
            {
                cancellation?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (call == failOnCall)
            {
                return Task.FromResult(
                    new ProcessRunResult(
                        1,
                        "",
                        "intentional failure",
                        TimeSpan.Zero));
            }

            string output = request.Arguments[^1];
            Directory.CreateDirectory(
                Path.GetDirectoryName(output)!);
            File.WriteAllBytes(output, [1, 2, 3]);
            return Task.FromResult(
                new ProcessRunResult(
                    0,
                    "",
                    "",
                    TimeSpan.Zero));
        }
    }

}
