namespace ReplayFoundry.CompositionTests;

internal static class Program
{
    private static int Main()
    {
        IReadOnlyList<TestCase> tests =
        [
            .. NormalizedRectangleTests.GetTests(),
            .. CompositionRegionTests.GetTests(),
            .. CompositionCoverageTests.GetTests(),
            .. CompositionPlanTests.GetTests(),
            .. ManualCompositionPlanFactoryTests.GetTests(),
            .. CompositionReviewTests.GetTests(),
        ];

        return TestRunner.Run("Replay Foundry Composition Tests", tests);
    }
}
