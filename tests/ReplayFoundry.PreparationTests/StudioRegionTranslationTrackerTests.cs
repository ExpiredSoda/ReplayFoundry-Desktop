using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.PreparationTests;

internal static class StudioRegionTranslationTrackerTests
{
    private const int Width = 128;
    private const int Height = 96;
    private const int Patch = 16;
    private static readonly NormalizedRectangle Seed = new(32d / Width, 32d / Height, Patch / (double)Width, Patch / (double)Height);
    private static readonly NormalizedRectangle Viewport = new(.1, .1, .4, .5);

    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Source tracker follows distinctive integer translation with exact source clocks", TracksKnownTranslation),
        new("Source tracker rejects a duplicate feature instead of silently changing targets", RejectsAmbiguousDuplicate),
        new("Source tracker rejects uninformative seeds and never reacquires after scene change or loss", StopsAfterUncertainty),
        new("Source tracker rejects motion exceeding the saved knot budget instead of truncating", PreservesSimplificationBudget),
        new("Source tracker enforces decoder bounds and observes cancellation", ValidatesBoundsAndCancellation),
    ];

    private static Task TracksKnownTranslation()
    {
        byte[][] frames = Enumerable.Range(0, 8).Select(index => Frame((32 + index * 3, 32 + index))).ToArray();
        TimeSpan start = TimeSpan.FromSeconds(101.125);
        var result = StudioRegionTranslationTracker.Analyze(frames, Width, Height, start, Seed,
            NormalizedRectangle.FullFrame, Viewport, CancellationToken.None);
        TestAssert.Equal(8, result.Samples.Count, "Every decoded frame must retain a diagnostic sample.");
        TestAssert.Equal(1, result.Runs.Count, "A distinctive uninterrupted translation should yield one accepted run.");
        for (int index = 0; index < frames.Length; index++)
        {
            var sample = result.Samples[index];
            TestAssert.Equal(start + TimeSpan.FromTicks(index * TimeSpan.TicksPerSecond / 8), sample.SourcePosition,
                "The tracker must preserve the input source offset and exact 8 Hz sampling clock.");
            TestAssert.Equal(index == 0 ? StudioCropTrackingState.Seed : StudioCropTrackingState.Tracked, sample.State,
                "Known clean motion must be accepted, with the first frame explicitly identified as a seed.");
            TestAssert.NearlyEqual((32 + index * 3d) / Width, sample.Feature!.X, 1e-12, "Feature X must follow the measured integer translation.");
            TestAssert.NearlyEqual((32 + index) / (double)Height, sample.Feature.Y, 1e-12, "Feature Y must follow the measured integer translation.");
            TestAssert.NearlyEqual(Viewport.X + index * 3d / Width, sample.Viewport!.X, 1e-12,
                "The viewport must follow the feature delta without changing its size.");
            TestAssert.Equal(Viewport.Width, sample.Viewport.Width, "Translation must not invent scale changes.");
        }
        TestAssert.Equal(2, result.Runs[0].Knots.Count, "Exactly linear movement should simplify to its true endpoints.");
        return Task.CompletedTask;
    }

    private static Task RejectsAmbiguousDuplicate()
    {
        var result = Analyze([Frame((32, 32)), Frame((32, 32), (52, 32)), Frame((35, 32))]);
        TestAssert.Equal(StudioCropTrackingState.Ambiguous, result.Samples[1].State,
            "Two equally distinctive nearby patches must be reported as ambiguous, not arbitrarily selected.");
        TestAssert.True(result.Samples[1].AmbiguityMargin < .1, "The reported ambiguity margin must reflect the competing match.");
        TestAssert.Equal(StudioCropTrackingState.Ambiguous, result.Samples[2].State,
            "A later recognizable feature cannot silently reacquire after an ambiguous frame.");
        TestAssert.True(result.Samples.Skip(1).All(static sample => sample.Viewport is null) && result.Runs.Count == 0,
            "A one-frame seed cannot create an accepted interval across ambiguity.");
        return Task.CompletedTask;
    }

    private static Task StopsAfterUncertainty()
    {
        var blank = Analyze([new byte[Width * Height], new byte[Width * Height]]);
        TestAssert.True(blank.Runs.Count == 0 && blank.Samples.All(static sample =>
            sample.State == StudioCropTrackingState.Uninformative && sample.Viewport is null),
            "Uniform seeds must leave the manual layout active.");

        var changed = Analyze([Frame((32, 32)), Frame((34, 32)),
            Enumerable.Repeat((byte)255, Width * Height).ToArray(), Frame((36, 32))]);
        TestAssert.Equal(StudioCropTrackingState.SceneChanged, changed.Samples[2].State,
            "A major decoded scene change must end the accepted run.");
        TestAssert.Equal(StudioCropTrackingState.SceneChanged, changed.Samples[3].State,
            "The original template returning later must not bridge a scene change.");
        TestAssert.Equal(TimeSpan.FromSeconds(.125), changed.Runs.Single().Knots[^1].SourcePosition,
            "Saved knots must end at the last accepted source frame, before scene loss.");
        TestAssert.True(changed.Samples.Skip(2).All(static sample => sample.Viewport is null),
            "No inferred viewport may cross the rejected interval.");

        var lost = Analyze([Frame((32, 32)), Frame((34, 32)), new byte[Width * Height], Frame((36, 32))]);
        TestAssert.Equal(StudioCropTrackingState.Lost, lost.Samples[2].State,
            "A vanished small feature without a global scene change must be reported as loss.");
        TestAssert.Equal(StudioCropTrackingState.Lost, lost.Samples[3].State, "Loss must remain explicit without automatic reacquisition.");
        return Task.CompletedTask;
    }

    private static Task PreservesSimplificationBudget()
    {
        // Alternating 16-pixel translations are individually trackable, but
        // removing any reversal would exceed the one-analysis-pixel tolerance.
        byte[][] frames = Enumerable.Range(0, 20).Select(index => Frame((32 + index % 2 * 16, 32))).ToArray();
        var result = Analyze(frames);
        TestAssert.True(result.Samples.All(static sample => sample.Viewport is not null),
            "This fixture must fail the storage budget, not tracking confidence.");
        TestAssert.True(StudioRegionTranslationTracker.Simplify(result.Samples, Width, Height, CancellationToken.None).Count > 16,
            "The fixture must require more than sixteen faithful motion knots.");
        TestAssert.Equal(0, result.Runs.Count, "Over-budget motion must be rejected whole, never silently truncated to sixteen knots.");
        TestAssert.True(result.Summary.Contains("No motion was truncated or applied", StringComparison.Ordinal),
            "The reviewer must know that the analyzed motion was not applied.");
        return Task.CompletedTask;
    }

    private static Task ValidatesBoundsAndCancellation()
    {
        byte[][] frames = [Frame((32, 32)), Frame((34, 32))];
        TestAssert.Throws<OperationCanceledException>(() => StudioRegionTranslationTracker.Analyze(
            frames, Width, Height, TimeSpan.Zero, Seed, NormalizedRectangle.FullFrame, Viewport, new CancellationToken(true)),
            "Cancellation must stop analysis before a motion result is published.");
        TestAssert.Throws<ArgumentException>(() => Analyze([new byte[Width * Height - 1], frames[1]]),
            "Partial raw frames must fail instead of indexing incomplete decoder data.");
        TestAssert.Throws<ArgumentException>(() => StudioRegionTranslationTracker.Analyze(frames, Width, Height,
            TimeSpan.Zero, Seed, new NormalizedRectangle(.5, .5, .5, .5), Viewport, CancellationToken.None),
            "The feature and viewport must both remain inside the allowed tracking bounds.");
        return Task.CompletedTask;
    }

    private static StudioTranslationAnalysis Analyze(IReadOnlyList<byte[]> frames) =>
        StudioRegionTranslationTracker.Analyze(frames, Width, Height, TimeSpan.Zero, Seed,
            NormalizedRectangle.FullFrame, Viewport, CancellationToken.None);

    private static byte[] Frame(params (int X, int Y)[] origins)
    {
        var frame = new byte[Width * Height];
        foreach (var origin in origins)
        {
            uint random = 123456789;
            for (int y = 0; y < Patch; y++)
                for (int x = 0; x < Patch; x++)
                {
                    random = unchecked(random * 1664525 + 1013904223);
                    frame[(origin.Y + y) * Width + origin.X + x] = (byte)(32 + (random >> 24) % 192);
                }
        }
        return frame;
    }
}
