namespace ReplayFoundry.Testing;

public static class TestRunner
{
    private const int HeadingWidth = 60;

    public static int Run(string suiteName, IReadOnlyList<TestCase> tests) =>
        RunAsync(suiteName, tests).GetAwaiter().GetResult();

    public static async Task<int> RunAsync(
        string suiteName,
        IReadOnlyList<TestCase> tests)
    {
        WriteHeading(suiteName);
        int failed = await ExecuteAsync(tests);
        WriteSummary(tests.Count - failed, failed, tests.Count);
        return failed == 0 ? 0 : 1;
    }

    private static async Task<int> ExecuteAsync(IReadOnlyList<TestCase> tests)
    {
        int failed = 0;
        foreach (TestCase test in tests)
        {
            failed += await ExecuteAsync(test);
        }

        return failed;
    }

    private static async Task<int> ExecuteAsync(TestCase test)
    {
        try
        {
            await test.ExecuteAsync();
            Console.WriteLine($"PASS  {test.Name}");
            return 0;
        }
        catch (Exception exception)
        {
            WriteFailure(test.Name, exception);
            return 1;
        }
    }

    private static void WriteHeading(string suiteName)
    {
        Console.WriteLine(suiteName);
        Console.WriteLine(new string('=', HeadingWidth));
    }

    private static void WriteFailure(string name, Exception exception)
    {
        Console.WriteLine($"FAIL  {name}");
        Console.WriteLine($"      {exception.Message}");
        Console.WriteLine();
        Console.WriteLine(exception);
    }

    private static void WriteSummary(int passed, int failed, int total)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', HeadingWidth));
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {total}");
    }
}
