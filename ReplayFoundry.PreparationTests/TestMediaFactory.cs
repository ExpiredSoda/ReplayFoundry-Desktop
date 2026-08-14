using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Signals;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Summaries;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Preparation;

namespace ReplayFoundry.PreparationTests;

internal static class TestMediaFactory
{
    public static MediaProbeResult Create(
        string fullPath,
        TimeSpan? duration = null,
        int width = 1920,
        int height = 1080,
        MediaRational? displayAspectRatio = null,
        MediaValueSource displayAspectRatioSource =
            MediaValueSource.ReportedByProbe,
        double? rotationDegrees = null,
        int videoStreamIndex = 0,
        bool hasAudio = false,
        int audioStreamCount = 1)
    {
        if (hasAudio && audioStreamCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioStreamCount));
        }

        TimeSpan actualDuration =
            duration ??
            TimeSpan.FromMinutes(10);

        MediaRational actualDisplayAspectRatio =
            displayAspectRatio ??
            new MediaRational(
                width,
                height);

        var container =
            new MediaContainerInfo(
                "matroska",
                "Matroska",
                actualDuration,
                TimeSpan.Zero,
                1024,
                8_000_000,
                100,
                null);

        var video =
            new VideoStreamInfo(
                videoStreamIndex,
                "h264",
                "H.264",
                null,
                width,
                height,
                width,
                height,
                new MediaRational(60, 1),
                new MediaRational(60, 1),
                "yuv420p",
                8,
                MediaValueSource.ReportedByProbe,
                new MediaRational(1, 1),
                MediaValueSource.ReportedByProbe,
                actualDisplayAspectRatio,
                displayAspectRatioSource,
                rotationDegrees,
                null,
                "tv",
                "bt709",
                "bt709",
                "bt709",
                "left",
                null,
                actualDuration,
                true);

        var manifest =
            new MediaInspectionManifest(
                "TestProbe",
                "1.0",
                "ffprobe",
                "ffprobe test",
                Path.Combine(
                    Path.GetTempPath(),
                    "ffprobe.exe"),
                DateTimeOffset.UtcNow);

        AudioStreamInfo[] audioStreams =
            hasAudio
                ? Enumerable.Range(1, audioStreamCount)
                    .Select(
                        index =>
                            new AudioStreamInfo(
                                index,
                                "aac",
                                "AAC",
                                null,
                                48000,
                                2,
                                "stereo",
                                16,
                                192000,
                                actualDuration,
                                "eng",
                                $"Misleading title {index}",
                                index == 1))
                    .ToArray()
                : [];

        return new MediaProbeResult(
            fullPath,
            container,
            [video],
            audioStreams,
            manifest);
    }

    public static string CreateSourcePath(
        string fileName)
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundryPreparationTests");

        Directory.CreateDirectory(directory);

        return Path.Combine(
            directory,
            fileName);
    }

    public static string CreateExistingSourcePath(
        string fileName)
    {
        string uniqueFileName =
            $"{Guid.NewGuid():N}-{fileName}";

        string path =
            CreateSourcePath(uniqueFileName);

        File.WriteAllBytes(
            path,
            [1, 2, 3, 4]);

        return path;
    }

    public static GenerationSourceFileSnapshot CreateSnapshot(
        string fullPath,
        long fileLength = 1024,
        DateTimeOffset? lastWriteTimeUtc = null)
    {
        return new GenerationSourceFileSnapshot(
            fullPath,
            fileLength,
            lastWriteTimeUtc ??
            new DateTimeOffset(
                2026,
                7,
                25,
                12,
                0,
                0,
                TimeSpan.Zero));
    }

    public static byte[] CreatePngHeader(
        int width,
        int height)
    {
        byte[] bytes =
            new byte[33];

        byte[] signature =
        [
            0x89,
            0x50,
            0x4E,
            0x47,
            0x0D,
            0x0A,
            0x1A,
            0x0A,
        ];

        signature.CopyTo(
            bytes,
            0);

        bytes[11] = 13;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';

        WriteBigEndian(
            bytes,
            16,
            width);

        WriteBigEndian(
            bytes,
            20,
            height);

        bytes[24] = 8;
        bytes[25] = 2;

        return bytes;
    }

    public static GenerationEvidenceAnalysisResult
        CreateEvidenceAnalysisResult(
            GenerationEvidenceAnalysisRequest request,
            string analyzerName = "test-analyzer",
            string analyzerVersion = "3.0.0")
    {
        ArgumentNullException.ThrowIfNull(request);

        var analyzed =
            new List<AnalyzedGenerationSource>(
                request.SourceCount);

        for (int index = 0;
             index < request.SourceCount;
             index++)
        {
            PreparedGenerationSource prepared =
                request.PreparedSources[index];

            var mediaRequest =
                MediaEvidenceAnalysisRequest
                    .CreateCompositionAware(
                        prepared.Media,
                        request.SourcePlans[index].Plan,
                        request.Settings.Options,
                        request.Settings
                            .IncludedRegionRoles);

            MediaEvidenceResult evidence =
                CreateMediaEvidenceResult(
                    mediaRequest,
                    analyzerName,
                    analyzerVersion);

            MediaEvidenceSummary summary =
                MediaEvidenceSummaryBuilder.Build(
                    prepared.Media,
                    evidence,
                    request.Settings.SummaryOptions);

            analyzed.Add(
                new AnalyzedGenerationSource(
                    prepared,
                    request.SourcePlans[index],
                    evidence,
                    summary,
                    request.Settings));
        }

        return new GenerationEvidenceAnalysisResult(
            request,
            analyzed);
    }

    public static MediaEvidenceResult
        CreateMediaEvidenceResult(
            MediaEvidenceAnalysisRequest request,
            string analyzerName = "test-analyzer",
            string analyzerVersion = "3.0.0")
    {
        ArgumentNullException.ThrowIfNull(request);

        VisualEvidenceTargetPlan targetPlan =
            VisualEvidenceTargetPlanner.Create(
                request);

        VisualTargetEvidenceResult[] targetResults =
            targetPlan.Targets
                .Select(
                    target =>
                        new VisualTargetEvidenceResult(
                            target,
                            [],
                            [],
                            [],
                            [],
                            CreateEmptyVisualCoverage(
                                target,
                                request.Options
                                    .VisualSignalSampleInterval)))
                .ToArray();

        AudioSignalCoverage[] audioCoverages =
            request.Media.AudioStreams
                .Select(
                    stream =>
                        CreateEmptyAudioCoverage(
                            stream,
                            request.Media.Duration,
                            request.Options
                                .AudioSignalWindowDuration))
                .ToArray();

        var manifest =
            new MediaEvidenceAnalysisManifest(
                analyzerName,
                analyzerVersion,
                "ffmpeg",
                "ffmpeg test",
                Path.Combine(
                    Path.GetTempPath(),
                    "ffmpeg.exe"),
                new DateTimeOffset(
                    2026,
                    7,
                    26,
                    12,
                    0,
                    0,
                    TimeSpan.Zero),
                AnalysisCoverage.FullTimeline,
                request.Options,
                request.Composition!
                    .Manifest.SchemaVersion,
                request.Composition!
                    .Manifest
                    .CoordinateSpaceVersion,
                request.Composition!
                    .Manifest.Origin,
                request.IncludedRegionRoles,
                targetPlan.Targets,
                targetPlan.SkippedRegions,
                targetPlan.DisplayGeometry.Width,
                targetPlan.DisplayGeometry.Height,
                MediaSignalEvidencePolicy
                    .CurrentSchemaVersion,
                targetResults.Select(
                    static result =>
                        result.SignalCoverage),
                audioCoverages,
                visualPassCount: 2,
                audioPassCount:
                    request.Media.AudioStreams.Count,
                passTimings: Enumerable.Range(
                        0,
                        2 +
                        request.Media
                            .AudioStreams.Count)
                    .Select(
                        passIndex =>
                            new AnalysisPassTiming(
                                $"test-pass-{passIndex}",
                                TimeSpan.Zero)),
                totalElapsed: TimeSpan.Zero);

        VisualTargetEvidenceResult fullFrame =
            targetResults.Single(
                static target =>
                    target.Target.Kind ==
                    VisualEvidenceTargetKind.FullFrame);

        VisualTargetEvidenceResult[] regions =
            targetResults
                .Where(
                    static target =>
                        target.Target.Kind ==
                        VisualEvidenceTargetKind
                            .CompositionRegion)
                .ToArray();

        return new MediaEvidenceResult(
            request.Media.FullPath,
            request.Media.Duration,
            fullFrame,
            regions,
            [],
            [],
            audioCoverages,
            manifest);
    }

    private static VisualSignalCoverage
        CreateEmptyVisualCoverage(
            VisualEvidenceTarget target,
            TimeSpan requestedInterval)
    {
        return new VisualSignalCoverage(
            target.TargetKey,
            target.Start,
            target.End,
            requestedInterval,
            [],
            expectedSampleCount: null,
            targetIntervalTraversed: true,
            MediaSignalEvidencePolicy
                .CurrentSchemaVersion);
    }

    private static AudioSignalCoverage
        CreateEmptyAudioCoverage(
            AudioStreamInfo stream,
            TimeSpan sourceDuration,
            TimeSpan requestedWindow)
    {
        int sampleRate =
            stream.SampleRate ??
            throw new InvalidOperationException(
                "Test audio streams require a sample rate.");

        int samplesPerWindow =
            Math.Max(
                1,
                (int)Math.Round(
                    sampleRate *
                    requestedWindow.TotalSeconds,
                    MidpointRounding.AwayFromZero));
        TimeSpan actualWindow =
            TimeSpan.FromSeconds(
                samplesPerWindow /
                (double)sampleRate);

        return new AudioSignalCoverage(
            stream.Index,
            sourceDuration,
            requestedWindow,
            actualWindow,
            sampleRate,
            samplesPerWindow,
            actualWindowCount: 0,
            totalCoveredDuration: TimeSpan.Zero,
            maximumObservedGap: sourceDuration,
            AudioFinalPartialWindowPolicy
                .IncludeWithoutPadding,
            finalPartialWindowSampleCount: null,
            uncoveredTailDuration: sourceDuration,
            sourceTimelineTraversed: true,
            MediaSignalEvidencePolicy
                .CurrentSchemaVersion);
    }

    private static void WriteBigEndian(
        byte[] target,
        int offset,
        int value)
    {
        target[offset] =
            (byte)(value >> 24);
        target[offset + 1] =
            (byte)(value >> 16);
        target[offset + 2] =
            (byte)(value >> 8);
        target[offset + 3] =
            (byte)value;
    }
}
