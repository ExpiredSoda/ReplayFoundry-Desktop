using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.MomentGuidance;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.PreparationTests;

internal static class GenerationMomentGuidanceTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Human moment guidance identities are stable and collections immutable", StableImmutableGuidance),
        new("Human ranges under three minutes reserve a candidate search", ShortRangeReservationPolicy),
        new("Generation moment service maps guidance to the exact source", ServiceMapsExactSourceGuidance),
        new("Human priority precedes a higher automatic score", HumanPriorityPrecedesAutomaticScore),
        new("Human priority can override quality-first threshold without hiding the score", HumanPriorityOverridesQualityTarget),
        new("Empty human guidance preserves automatic ordering", EmptyGuidancePreservesOrdering),
        new("Priority source selector displays the source name", PrioritySourceDisplaysName),
    ];

    private static Task StableImmutableGuidance()
    {
        string source = Path.GetFullPath("guidance-source.mkv");
        TimeSpan duration = TimeSpan.FromMinutes(10);
        UserMomentGuidance first = UserMomentGuidance.CreatePoint(
            source,
            duration,
            TimeSpan.FromSeconds(42));
        UserMomentGuidance repeated = UserMomentGuidance.CreatePoint(
            source,
            duration,
            TimeSpan.FromSeconds(42));
        var mutable = new List<UserMomentGuidance> { first };
        var guidance = new GenerationMomentGuidance(mutable);
        mutable.Clear();

        TestAssert.Equal(first.Id, repeated.Id, "Stable point identity.");
        TestAssert.Equal(1, guidance.Count, "Caller mutation must not alter guidance.");
        TestAssert.True(
            guidance.Items is not List<UserMomentGuidance>,
            "Guidance must expose an immutable snapshot.");
        return Task.CompletedTask;
    }

    private static Task ShortRangeReservationPolicy()
    {
        string source = Path.GetFullPath("guidance-source.mkv");
        TimeSpan duration = TimeSpan.FromMinutes(30);
        UserMomentGuidance shortRange = UserMomentGuidance.CreateRange(
            source,
            duration,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(4));
        UserMomentGuidance longRange = UserMomentGuidance.CreateRange(
            source,
            duration,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(8) + TimeSpan.FromMilliseconds(1));

        TestAssert.True(shortRange.ReservesCandidateSearch, "Three-minute range.");
        TestAssert.False(longRange.ReservesCandidateSearch, "Range over three minutes.");
        return Task.CompletedTask;
    }

    private static Task ServiceMapsExactSourceGuidance()
    {
        var context = CreateGuidedRequest(
            [90, 80],
            TimeSpan.FromSeconds(60),
            desiredCount: 2,
            threshold: 0,
            ClipFulfillmentPreference.FillRequestedCount);
        var finder = new GenerationMomentFindingTests.RecordingMomentFinder([[90, 80]]);
        var service = new GenerationMomentFindingService(finder);

        service.Find(context.Request);

        TestAssert.Equal(1, finder.Requests[0].Guidance.Items.Count, "Mapped item count.");
        TestAssert.Equal(
            context.Guidance.Items[0].Id,
            finder.Requests[0].Guidance.Items[0].Id,
            "Mapped identity.");
        return Task.CompletedTask;
    }

    private static Task HumanPriorityPrecedesAutomaticScore()
    {
        var context = CreateGuidedRequest(
            [99, 40],
            TimeSpan.FromSeconds(60),
            desiredCount: 1,
            threshold: 0,
            ClipFulfillmentPreference.FillRequestedCount);
        var service = new GenerationMomentFindingService(
            new GenerationMomentFindingTests.RecordingMomentFinder([[99, 40]]));

        GenerationMomentFindingResult result = service.Find(context.Request);

        TestAssert.Equal(
            TimeSpan.FromSeconds(60),
            result.SelectedCandidates[0].Candidate.Window.Start,
            "Human-guided window must rank first.");
        TestAssert.Equal(
            GenerationCandidateSelectionReason.UserPriority,
            result.SelectedCandidates[0].SelectionReason,
            "Selection reason.");
        return Task.CompletedTask;
    }

    private static Task HumanPriorityOverridesQualityTarget()
    {
        var context = CreateGuidedRequest(
            [99, 40],
            TimeSpan.FromSeconds(60),
            desiredCount: 1,
            threshold: 70,
            ClipFulfillmentPreference.QualityFirst);
        var service = new GenerationMomentFindingService(
            new GenerationMomentFindingTests.RecordingMomentFinder([[99, 40]]));

        GenerationMomentFindingResult result = service.Find(context.Request);

        TestAssert.Equal(1, result.HumanPriorityCount, "Human priority count.");
        TestAssert.Equal(1, result.BelowQualityTargetCount, "Score remains truthful.");
        TestAssert.Equal(40d, result.SelectedCandidates[0].Candidate.HeuristicScore, "Raw score.");
        return Task.CompletedTask;
    }

    private static Task EmptyGuidancePreservesOrdering()
    {
        GenerationMomentFindingRequest request =
            GenerationMomentFindingTests.CreateRequest(
                sourceCount: 1,
                desiredCount: 2);
        var service = new GenerationMomentFindingService(
            new GenerationMomentFindingTests.RecordingMomentFinder([[80, 90]]));

        GenerationMomentFindingResult result = service.Find(request);

        TestAssert.Equal(90d, result.SelectedCandidates[0].Candidate.HeuristicScore, "Automatic rank one.");
        TestAssert.Equal(80d, result.SelectedCandidates[1].Candidate.HeuristicScore, "Automatic rank two.");
        return Task.CompletedTask;
    }

    private static Task PrioritySourceDisplaysName()
    {
        string path = TestMediaFactory.CreateSourcePath("guidance-display.mkv");
        var selected = new SelectedVideoSource(path, isReference: true);
        var prepared = new PreparedGenerationSource(
            selected,
            TestMediaFactory.Create(path, TimeSpan.FromMinutes(2)),
            TestMediaFactory.CreateSnapshot(path));
        var viewModel = new MomentGuidanceSourceViewModel(
            prepared,
            [],
            static () => { });

        TestAssert.Equal(
            "guidance-display.mkv · reference",
            viewModel.DisplayName,
            "Display name.");
        TestAssert.Equal(
            viewModel.DisplayName,
            viewModel.ToString(),
            "Selector presentation.");
        return Task.CompletedTask;
    }

    private static (
        GenerationMomentFindingRequest Request,
        GenerationMomentGuidance Guidance) CreateGuidedRequest(
        IReadOnlyList<double> scores,
        TimeSpan point,
        int desiredCount,
        double threshold,
        ClipFulfillmentPreference fulfillment)
    {
        var evidence = GenerationMomentFindingTests.CreateEvidence(1);
        var source = evidence.Sources[0].PreparedSource.Media;
        var guidance = new GenerationMomentGuidance(
        [
            UserMomentGuidance.CreatePoint(
                source.FullPath,
                source.Duration,
                point),
        ]);
        var setup = new GenerationSetupOptions(
            GenerationMode.IndividualClips,
            DetectionMethod.Heuristics,
            AudioSelectionMode.Auto,
            desiredCount,
            threshold,
            ContentEmphasis.Balanced,
            fulfillment,
            guidance);
        return (new GenerationMomentFindingRequest(evidence, setup), guidance);
    }
}
