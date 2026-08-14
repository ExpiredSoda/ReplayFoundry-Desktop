using ReplayFoundry.RuntimePacks;

namespace ReplayFoundry.RuntimePacks.Tests;

internal sealed record TestCase(string Name, Func<Task> ExecuteAsync);

internal static class Program
{
    private static async Task<int> Main()
    {
        IReadOnlyList<TestCase> tests =
        [
            .. RuntimePackTests.GetTests(),
            .. AppRuntimePackIntegrationTests.GetTests(),
            .. MediaToolResolutionTests.GetTests(),
            .. QwenRuntimeResolutionTests.GetTests(),
        ];
        int passed = 0;
        int failed = 0;
        Console.WriteLine("Replay Foundry Runtime Pack Tests");
        Console.WriteLine(new string('=', 60));
        foreach (TestCase test in tests)
        {
            try
            {
                await test.ExecuteAsync();
                passed++;
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"FAIL  {test.Name}");
                Console.WriteLine($"      {exception.Message}");
            }
        }
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Total:  {tests.Count}");
        return failed == 0 ? 0 : 1;
    }
}
