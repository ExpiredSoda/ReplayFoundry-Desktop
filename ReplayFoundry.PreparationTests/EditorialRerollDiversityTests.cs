using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static class EditorialRerollDiversityTests
{
    private const string Attempt0 =
        "We climbed a ladder and entered a house together. #TheLastofUs";
    private const string Attempt1 =
        "We climbed a ladder and entered a house together. #TheLastofUs";
    private const string Attempt2 =
        "We climbed the ladder and entered the house together. #TheLastofUs";
    private const string Attempt3 =
        "We climbed a ladder into a house with Bill inside. #TheLastofUs";

    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Real reroll titles collapse equivalent Last of Us attempts",
            RealTitlesUseMeaningfulDiversity),
        new(
            "Reroll diversity compares every prior title on the exact cut",
            EveryPriorAndExactCut),
        new(
            "Editorial requests preserve bounded exclusion-only title history",
            RequestAndDraftHistory),
        new(
            "Prior titles normalize a mixed-case canonical hashtag once",
            MixedCaseHashtagNormalizesOnce),
        new(
            "Qwen batch parsing retains external and in-batch title history",
            QwenBatchHistoryIsRetained),
    ];

    private static Task RealTitlesUseMeaningfulDiversity()
    {
        var scope = new Qwen3VlGroundedMetadataRerollTitleScope(
            "last-of-us-ladder-house",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(40));
        Qwen3VlGroundedMetadataRerollTitleReference first =
            new(scope, Attempt0, "#TheLastofUs");

        TestAssert.Equal(
            Qwen3VlGroundedMetadataRerollTitleDiversityCode.ExactCanonicalTitle,
            Qwen3VlGroundedMetadataRerollDiversityPolicy.Evaluate(
                new(scope, Attempt1, "#TheLastofUs"),
                [first]).Code,
            "An exact repeated title must not satisfy a reroll.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataRerollTitleDiversityCode.ExactCanonicalTitle,
            Qwen3VlGroundedMetadataRerollDiversityPolicy.Evaluate(
                new(scope, Attempt2, "#TheLastofUs"),
                [first]).Code,
            "Changing only closed-class words must not masquerade as diversity.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataRerollTitleDiversityCode.MateriallyDistinct,
            Qwen3VlGroundedMetadataRerollDiversityPolicy.Evaluate(
                new(scope, Attempt3, "#TheLastofUs"),
                [first]).Code,
            "The Bill-focused fourth title must remain a supported distinct angle.");
        return Task.CompletedTask;
    }

    private static Task EveryPriorAndExactCut()
    {
        var scope = new Qwen3VlGroundedMetadataRerollTitleScope(
            "candidate",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30));
        var otherCut = scope with { SourceEnd = TimeSpan.FromSeconds(31) };
        Qwen3VlGroundedMetadataRerollTitleReference candidate =
            new(scope, Attempt0, "#TheLastofUs");
        Qwen3VlGroundedMetadataRerollTitleReference priorA =
            new(scope, Attempt0, "#TheLastofUs");
        Qwen3VlGroundedMetadataRerollTitleReference priorB =
            new(scope, Attempt3, "#TheLastofUs");

        TestAssert.False(
            Qwen3VlGroundedMetadataRerollDiversityPolicy.Evaluate(
                candidate,
                [priorA, priorB]).IsMateriallyDistinct,
            "A→B→A must compare against all accepted titles, not only the latest.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataRerollTitleDiversityCode.NoComparablePrior,
            Qwen3VlGroundedMetadataRerollDiversityPolicy.Evaluate(
                candidate,
                [new(otherCut, Attempt0, "#TheLastofUs")]).Code,
            "A changed cut must reset title exclusions rather than leak semantics across windows.");
        return Task.CompletedTask;
    }

    private static Task RequestAndDraftHistory()
    {
        ClipEditorialContext context = CreateContext();
        var original = new ClipEditorialMetadataDraft(
            Attempt0,
            "We climbed into the house.",
            ["gameplay"],
            ClipEditorialMetadataOrigin.AiAssisted,
            new ClipEditorialMetadataGeneratorIdentity("test", "1.0"),
            attempt: 0);
        ClipEditorialMetadataDraft edited = original.WithUserEdits(
            Attempt3,
            original.Description,
            original.Tags);
        IReadOnlyList<ClipEditorialPriorTitleExclusion> exclusions =
            edited.CreatePriorTitleExclusions(context);
        var request = new ClipEditorialMetadataRequest(
            context,
            new ClipEditorialProfile("Chat"),
            attempt: 2,
            priorAcceptedTitleExclusions: exclusions);

        TestAssert.Equal(
            2,
            request.PriorAcceptedTitleExclusions.Count,
            "The original and current accepted title must both reach a reroll request.");
        TestAssert.Equal(
            request.PriorAcceptedTitleExclusions.Count,
            request.WithAttempt(3).PriorAcceptedTitleExclusions.Count,
            "Provider retry attempts must preserve the original exclusion history.");
        var bounded = new ClipEditorialMetadataDraft(
            Attempt3,
            original.Description,
            original.Tags,
            ClipEditorialMetadataOrigin.AiAssisted,
            original.Generator,
            attempt: 4,
            priorAcceptedTitles: Enumerable.Range(1, 10)
                .Select(index => $"Supported title {index} #TheLastofUs"));
        TestAssert.Equal(
            ClipEditorialPriorTitleExclusion.MaximumRetainedTitles,
            bounded.PriorAcceptedTitles.Count,
            "Long reroll sessions must retain only the bounded all-prior window.");
        TestAssert.Equal(
            "Supported title 3 #TheLastofUs",
            bounded.PriorAcceptedTitles[0],
            "The bounded history must discard the oldest accepted title first.");
        TestAssert.Throws<ArgumentException>(
            () => new ClipEditorialMetadataRequest(
                context,
                new ClipEditorialProfile("Chat"),
                1,
                priorAcceptedTitleExclusions:
                [
                    new ClipEditorialPriorTitleExclusion(
                        context.CandidateId,
                        context.SourceStart,
                        context.SourceEnd + TimeSpan.FromSeconds(1),
                        Attempt0),
                ]),
            "A prior title from another cut must be rejected at the provider-neutral boundary.");
        return Task.CompletedTask;
    }

    private static Task MixedCaseHashtagNormalizesOnce()
    {
        ClipEditorialContext context = CreateContext();
        ClipEditorialPriorTitleExclusion lower =
            ClipEditorialPriorTitleExclusion.ForContext(
                context,
                "We entered the house #thelastofus");
        ClipEditorialPriorTitleExclusion mixed =
            ClipEditorialPriorTitleExclusion.ForContext(
                context,
                "We found Bill inside #ThElAsToFuS");

        TestAssert.Equal(
            "We entered the house #TheLastofUs",
            lower.Title,
            "A lowercase canonical suffix must be replaced rather than duplicated.");
        TestAssert.Equal(
            "We found Bill inside #TheLastofUs",
            mixed.Title,
            "Mixed-case canonical suffixes must normalize to the retained game hashtag exactly once.");
        return Task.CompletedTask;
    }

    private static Task QwenBatchHistoryIsRetained()
    {
        ClipEditorialContext context = CreateContext();
        ClipEditorialPriorTitleExclusion external =
            ClipEditorialPriorTitleExclusion.ForContext(context, Attempt0);
        var request = new ClipEditorialMetadataRequest(
            context,
            ClipEditorialProfile.Default,
            attempt: 2,
            priorAcceptedTitleExclusions: [external]);
        Qwen3VlGroundedMetadataRerollTitleReference inBatch =
            Qwen3VlGroundedMetadataRerollDiversityPolicy.Reference(
                context.CandidateId,
                context.SourceStart,
                context.SourceEnd,
                Attempt3,
                context.GameContext.GameHashtag);

        Qwen3VlGroundedMetadataRerollTitleReference[] retained =
            Qwen3VlGroundedMetadataResultParser.RetainPriorTitleReferences(
                request,
                [inBatch, inBatch]);

        TestAssert.Equal(2, retained.Length,
            "The parser must combine request history with prior successful batch results and deduplicate repeats.");
        TestAssert.True(retained.Any(value => value.Title == Attempt0) &&
            retained.Any(value => value.Title == Attempt3),
            "The next returned draft must be able to persist both external and in-batch accepted titles.");
        return Task.CompletedTask;
    }

    private static ClipEditorialContext CreateContext() =>
        new(
            "candidate",
            Path.GetFullPath("the-last-of-us-source.mkv"),
            "The Last of Us",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            80,
            "Deterministic activity support.");
}
