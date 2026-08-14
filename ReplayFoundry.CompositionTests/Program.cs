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

        var passed = 0;
        var failed = 0;

        Console.WriteLine("Replay Foundry Composition Tests");
        Console.WriteLine(new string('=', 60));

        foreach (var test in tests)
        {
            try
            {
                test.Execute();
                passed++;
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"FAIL  {test.Name}");
                Console.WriteLine($"      {exception.Message}");
                Console.WriteLine();
                Console.WriteLine(exception);
            }
        }

        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {tests.Count}");

        return failed == 0 ? 0 : 1;
    }
}
