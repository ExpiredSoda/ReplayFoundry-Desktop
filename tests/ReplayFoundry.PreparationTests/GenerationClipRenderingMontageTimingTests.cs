using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static Task MontageClockCommandsAreBounded()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.Montage);
        var asset = fixture.CreateDraft().PrimaryAsset;
        var profile = new GenerationClipOutputProfile(720, 1280, 30000d / 1001);
        var segment = FfmpegClipRenderCommandBuilder.BuildSegment(asset.SourceMedia, asset.SourceStart,
            asset.SourceStart + TimeSpan.FromTicks(21_373_000), profile, Path.Combine(fixture.Root, "segment.mp4"),
            renderSettings: asset.RenderSettings);
        TestAssert.True(segment.Arguments.Any(argument => argument.Contains(":start_time=0:eof_action=pass", StringComparison.Ordinal)),
            "Fractional source seeks must pad the first displayed frame onto the requested zero-origin output clock.");
        TestAssert.False(segment.Arguments.Contains("-shortest"),
            "AAC draining must not prematurely truncate the last video frame of a bounded cut.");
        TestAssert.True(ContainsPair(segment.Arguments, "-t", "2.1373"), "The original requested duration must remain authoritative.");
        string list = Path.Combine(fixture.Root, "fractional-concat.txt"); File.WriteAllText(list, "file 'segment.mp4'\nduration 2.1373\n");
        var join = FfmpegClipRenderCommandBuilder.BuildNormalizedConcatenation(list,
            Path.Combine(fixture.Root, "montage.mp4"), TimeSpan.FromSeconds(4.351), profile);
        TestAssert.False(ContainsPair(join.Arguments, "-c", "copy"), "Only the validated recovery path may re-encode a discontinuous packet timeline.");
        TestAssert.True(ContainsPair(join.Arguments, "-t", "4.351") &&
            join.Arguments.Any(argument => argument.Contains("atrim=duration=4.351", StringComparison.Ordinal)),
            "Audio must end at the sum of actual cut durations, not a sum rounded independently to video frames.");
        TestAssert.True(join.Arguments.Any(argument => argument.Contains("fps=29.97002997002997:start_time=0", StringComparison.Ordinal)),
            "Recovered video must use the requested fractional cadence without weakening validation.");
        TestAssert.False(join.Arguments.Contains("-ss"), "The join must not reinterpret original source timestamps or rerun source cuts.");
        return Task.CompletedTask;
    }

    private static async Task MontageValidationFailureRecoversOnce()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.Montage, scoreSets: [[95, 90]]);
        GenerationOutputProject draft = fixture.CreateDraft();
        for (int index = 0; index < draft.Assets.Count; index++)
        {
            var asset = draft.Assets[index];
            draft = draft.ReplaceAsset(asset.WithStudioEdits(asset.SourceStart,
                asset.SourceStart + TimeSpan.FromTicks(index == 0 ? 21_373_000 : 22_137_000), asset.Appearance)
                .WithRenderSettings(new StudioRenderSettings(asset.RenderSettings.Canvas,
                    quality: index == 0 ? StudioExportQuality.Compact : StudioExportQuality.High)));
        }
        var runner = new MontageTimingRunner();
        var validator = new MontageTimingValidator();
        var renderer = new FfmpegStudioProjectRenderingService(runner,
            new FixedFfmpegToolLocator(Path.Combine(fixture.Root, "ffmpeg.exe")), validator: validator);
        StudioProjectRenderResult result = await renderer.FinalizeAsync(draft,
            new RecordingProgress<StudioProjectRenderProgress>(), CancellationToken.None);
        TestAssert.Equal(5, runner.Requests.Count, "Two segments, failed copy inspection, one normalized join and thumbnail must run.");
        TestAssert.Equal(2, validator.MontageChecks, "The fallback must pass the same final validation as the copied file.");
        TestAssert.True(ContainsPair(runner.Requests[2].Arguments, "-c", "copy"), "Compatible packet-copy remains the first attempt.");
        TestAssert.True(runner.ConcatLists.All(static list => list.Contains("duration 2.1373", StringComparison.Ordinal) &&
                list.Contains("duration 2.2137", StringComparison.Ordinal)),
            "Both joins must place segments at the exact source-cut offsets used by caption sidecars.");
        TestAssert.True(ContainsPair(runner.Requests[3].Arguments, "-t", "4.351"), "The recovery must retain the complete fractional timeline.");
        TestAssert.Equal(runner.Requests[1].Arguments[runner.Requests[1].Arguments.ToList().IndexOf("-b:v") + 1],
            runner.Requests[3].Arguments[runner.Requests[3].Arguments.ToList().IndexOf("-b:v") + 1],
            "Recovery must retain the strongest included quality, even when the first segment requested Compact.");
        TestAssert.True(result.FinalizedProject.Assets.Zip(draft.Assets).All(static pair =>
            pair.First.SourceStart == pair.Second.SourceStart && pair.First.SourceEnd == pair.Second.SourceEnd),
            "Montage recovery must never rewrite source or caption clocks.");
        renderer.AcceptCompletedRender(result);
    }

    private static async Task MontageCopyFailureAndCancellationDoNotRecover()
    {
        using (PipelineFixture fixture = CreateFixture(GenerationMode.Montage, scoreSets: [[95, 90]]))
        {
            var runner = new WritingProcessRunner(failOnCall: 3);
            await TestAssert.ThrowsAsync<InvalidOperationException>(() => new FfmpegStudioProjectRenderingService(runner,
                new FixedFfmpegToolLocator(Path.Combine(fixture.Root, "ffmpeg.exe")), validator: new MontageTimingValidator())
                .FinalizeAsync(fixture.CreateDraft(), new RecordingProgress<StudioProjectRenderProgress>(), CancellationToken.None),
                "A failed copy process is not a successful copy with invalid media and must not trigger normalization.");
            TestAssert.Equal(3, runner.Requests.Count, "Process failure cannot start an additional encoder.");
            TestAssert.False(Directory.Exists(fixture.FinalDirectory), "No failed montage may be published.");
        }
        using (PipelineFixture fixture = CreateFixture(GenerationMode.Montage, scoreSets: [[95, 90]]))
        using (var cancellation = new CancellationTokenSource())
        {
            var runner = new WritingProcessRunner();
            await TestAssert.ThrowsAsync<OperationCanceledException>(() => new FfmpegStudioProjectRenderingService(runner,
                new FixedFfmpegToolLocator(Path.Combine(fixture.Root, "ffmpeg.exe")), validator: new MontageTimingValidator(cancellation))
                .FinalizeAsync(fixture.CreateDraft(), new RecordingProgress<StudioProjectRenderProgress>(), cancellation.Token),
                "Cancellation during copy inspection must stop before normalization.");
            TestAssert.Equal(3, runner.Requests.Count, "Cancellation cannot initiate recovery work.");
            TestAssert.False(Directory.Exists(fixture.FinalDirectory), "Cancellation must remove owned staging output.");
        }
    }

    private sealed class MontageTimingValidator(CancellationTokenSource? cancellation = null) : IStudioRenderedMediaValidator
    {
        public int MontageChecks { get; private set; }
        public Task ValidateAsync(string path, TimeSpan expectedDuration, GenerationClipOutputProfile profile,
            bool requiresBt709ToneMap, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFileName(path) == "ReplayFoundry-Montage.mp4" && ++MontageChecks == 1)
            {
                cancellation?.Cancel();
                throw new StudioRenderedMediaValidationException("Fixture: copied fractional streams have a cadence gap.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class MontageTimingRunner : IProcessRunner
    {
        private readonly WritingProcessRunner _writer = new();
        public IReadOnlyList<ProcessRunRequest> Requests => _writer.Requests;
        public List<string> ConcatLists { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            if (ContainsPair(request.Arguments, "-f", "concat"))
                ConcatLists.Add(File.ReadAllText(request.Arguments[request.Arguments.ToList().IndexOf("-i") + 1]));
            return _writer.RunAsync(request, cancellationToken);
        }
    }
}
