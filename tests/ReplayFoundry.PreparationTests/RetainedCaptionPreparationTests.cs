using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static async Task RetainedCaptionPreparationUsesExactWindow()
    {
        string sourcePath = TestMediaFactory.CreateSourcePath(
            "reopened-caption-source.mkv");
        MediaProbeResult sourceMedia = TestMediaFactory.Create(
            sourcePath,
            TimeSpan.FromMinutes(2),
            hasAudio: true);
        TimeSpan sourceStart = TimeSpan.FromSeconds(31);
        TimeSpan sourceEnd = TimeSpan.FromSeconds(43);
        int streamIndex = sourceMedia.AudioStreams[0].Index;
        var selection = new GenerationCaptionSourceSelection(
            sourcePath,
            streamIndex,
            CaptionAudioContentRole.CreatorCommentary,
            GenerationCaptionLanguagePolicy.English);
        var extractor = new RecordingRetainedAudioExtractor();
        var transcriber = new RecordingRetainedTranscriptionProvider();
        AudioTranscriptionOptions defaults =
            AudioTranscriptionOptions.CreateDefaults();
        var options = new AudioTranscriptionOptions(
            defaults.LanguageMode,
            defaults.RequestedLanguage,
            defaults.TranslateToEnglish,
            defaults.RequireSegmentTimestamps,
            requestWordTimestamps: true,
            defaults.Temperature,
            defaults.ThreadCount,
            defaults.ProcessorHint,
            defaults.MaximumProcessDuration,
            defaults.OutputFormatPolicy,
            defaults.PolicyVersion);
        var model = new AudioTranscriptionModelSettings(
            Path.Combine(Path.GetTempPath(), "retained-caption-model.bin"),
            "Retained caption test model",
            "ggml");
        var service = new GenerationCaptionPreparationService(
            extractor,
            transcriber,
            options,
            model);

        GenerationCandidateCaptionTrack track =
            await service.PrepareRetainedCandidateAsync(
                "reopened-hidden-candidate",
                sourceMedia,
                sourceStart,
                sourceEnd,
                selection,
                GenerationCaptionStylePreset.KaraokeSweep,
                CancellationToken.None);

        AudioSegmentExtractionRequest extraction =
            extractor.Request ?? throw new InvalidOperationException(
                "The retained caption path did not extract audio.");
        AudioTranscriptionRequest transcription =
            transcriber.Request ?? throw new InvalidOperationException(
                "The retained caption path did not transcribe audio.");
        TestAssert.Equal(
            Path.GetFullPath(sourcePath),
            extraction.SourcePath,
            "Retained caption extraction must use the persisted source path.");
        TestAssert.Equal(streamIndex, extraction.AbsoluteAudioStreamIndex,
            "Retained caption extraction must use the stored absolute stream.");
        TestAssert.Equal(sourceStart, extraction.Start,
            "Retained caption extraction must start at the persisted cut.");
        TestAssert.Equal(sourceEnd, extraction.End,
            "Retained caption extraction must end at the persisted cut.");
        TestAssert.Equal(sourceEnd - sourceStart, transcription.InputDuration,
            "Transcription must receive exactly the retained clip duration.");
        TestAssert.Equal(sourceStart, transcription.AbsoluteSourceOffset,
            "Transcription timing must remain anchored to the source cut.");
        TestAssert.Equal(streamIndex, transcription.AbsoluteAudioStreamIndex,
            "Transcription must receive the stored absolute stream.");
        TestAssert.Equal(
            "en",
            transcription.Options.RequestedLanguage?.Code,
            "The persisted language policy must reach transcription.");
        TestAssert.Equal(
            GenerationCaptionStylePreset.KaraokeSweep,
            track.RequestedStyle,
            "The accepted clip must retain its requested Karaoke Sweep style.");
        TestAssert.Equal(sourceStart, track.SourceWindowStart,
            "The Studio caption track must retain its absolute cut start.");
        TestAssert.Equal(sourceEnd - sourceStart, track.SourceWindowDuration,
            "The Studio caption track must retain its exact cut duration.");
        TestAssert.True(track.HasTimedWords,
            "The retained path must return renderable word-level timing.");
        TestAssert.Equal(
            sourceStart + TimeSpan.FromSeconds(1),
            track.Segments[0].Words[0].AbsoluteSourceStart,
            "Word timing must remain absolute to the original source.");
        TestAssert.Equal(1, extractor.CleanupCount,
            "The accepted-caption path must release its extracted audio.");
    }

    private sealed class RecordingRetainedAudioExtractor :
        IAudioSegmentExtractor
    {
        public AudioSegmentExtractionRequest? Request { get; private set; }
        public int CleanupCount { get; private set; }

        public Task<ExtractedAudioSegment> ExtractAsync(
            AudioSegmentExtractionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            DateTimeOffset started = DateTimeOffset.UnixEpoch;
            var manifest = new AudioSegmentExtractionManifest(
                "test extractor",
                "1.0",
                Path.Combine(Path.GetTempPath(), "ffmpeg.exe"),
                new string('A', 64),
                "test ffmpeg",
                [],
                request.SourcePath,
                request.Start,
                request.End,
                request.AbsoluteAudioStreamIndex,
                16_000,
                1,
                16,
                started,
                started.AddMilliseconds(1),
                TimeSpan.FromMilliseconds(1));
            return Task.FromResult(new ExtractedAudioSegment(
                request.NeighborhoodId,
                Path.Combine(Path.GetTempPath(), "retained-caption.wav"),
                request.Duration,
                1024,
                manifest,
                () => CleanupCount++));
        }
    }

    private sealed class RecordingRetainedTranscriptionProvider :
        IAudioTranscriptionProvider
    {
        public InferenceProviderIdentity Identity { get; } =
            new("retained-caption-test", "1.0", "1.0");

        public AudioTranscriptionRequest? Request { get; private set; }

        public Task<AudioTranscriptionProviderCapabilities>
            GetCapabilitiesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AudioTranscriptionProviderCapabilities(
                supportsAutomaticLanguage: true,
                supportsExplicitLanguage: true,
                supportsTranslationToEnglish: false,
                supportsSegmentTimestamps: true,
                supportsWordTimestamps: true,
                supportsTemperature: true,
                supportsThreadCount: true,
                reportsExecutionBackend: true));

        public Task<AudioTranscriptionResult> TranscribeAsync(
            AudioTranscriptionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            TimeSpan relativeStart = TimeSpan.FromSeconds(1);
            TimeSpan relativeMiddle = TimeSpan.FromSeconds(1.5);
            TimeSpan relativeEnd = TimeSpan.FromSeconds(2);
            AudioTranscriptionWord[] words =
            [
                new(
                    "rubber",
                    relativeStart,
                    relativeMiddle,
                    request.AbsoluteSourceOffset + relativeStart,
                    request.AbsoluteSourceOffset + relativeMiddle),
                new(
                    "duckies",
                    relativeMiddle,
                    relativeEnd,
                    request.AbsoluteSourceOffset + relativeMiddle,
                    request.AbsoluteSourceOffset + relativeEnd),
            ];
            var segment = new AudioTranscriptionSegment(
                "retained-caption-segment",
                request.NeighborhoodId,
                "rubber duckies",
                relativeStart,
                relativeEnd,
                request.AbsoluteSourceOffset + relativeStart,
                request.AbsoluteSourceOffset + relativeEnd,
                words);
            DateTimeOffset started = DateTimeOffset.UnixEpoch;
            var model = new ModelArtifactManifest(
                request.ModelSettings.DisplayName,
                request.ModelSettings.ModelPath,
                new string('B', 64),
                1,
                started,
                request.ModelSettings.ModelFormat);
            var execution = new InferenceExecutionManifest(
                Identity,
                Path.Combine(Path.GetTempPath(), "transcriber.exe"),
                new string('C', 64),
                "test transcriber",
                model,
                new Dictionary<string, string>(),
                started,
                started.AddMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
                wasCancelled: false,
                executionBackend: "CPU");
            var manifest = new AudioTranscriptionManifest(
                request.NeighborhoodId,
                request.InputDuration,
                request.AbsoluteSourceOffset,
                request.SourceDuration,
                request.AbsoluteAudioStreamIndex,
                request.Options,
                execution);
            return Task.FromResult(new AudioTranscriptionResult(
                request.NeighborhoodId,
                request.AbsoluteAudioStreamIndex,
                [segment],
                manifest,
                new AudioTranscriptionLanguage("en", "English")));
        }
    }
}
