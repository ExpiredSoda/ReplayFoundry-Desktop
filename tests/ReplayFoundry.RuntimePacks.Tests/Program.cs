using ReplayFoundry.RuntimePacks;

namespace ReplayFoundry.RuntimePacks.Tests;

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
        return await TestRunner.RunAsync("Replay Foundry Runtime Pack Tests", tests);
    }
}
