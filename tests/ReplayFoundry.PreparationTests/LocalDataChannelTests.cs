using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static class LocalDataChannelTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Build channels isolate mutable and temporary application data",
            BuildChannelsIsolateMutableData),
        new(
            "Test executables resolve only test-owned mutable state",
            TestExecutableUsesTestChannel),
        new(
            "Temporary workspace paths cannot escape their channel root",
            TemporaryPathsStayInsideChannel),
    ];

    private static Task BuildChannelsIsolateMutableData()
    {
        string baseRoot = Path.Combine(Path.GetTempPath(), "ReplayFoundry-ChannelFixture");
        string local = Path.Combine(baseRoot, "Local");
        string temporary = Path.Combine(baseRoot, "Temporary");
        ReplayFoundryDataRoots production = ReplayFoundryLocalDataPaths.CreateRoots(
            ReplayFoundryDataChannel.Production,
            local,
            temporary,
            "unused");
        ReplayFoundryDataRoots development = ReplayFoundryLocalDataPaths.CreateRoots(
            ReplayFoundryDataChannel.Development,
            local,
            temporary,
            "unused");
        ReplayFoundryDataRoots test = ReplayFoundryLocalDataPaths.CreateRoots(
            ReplayFoundryDataChannel.Test,
            local,
            temporary,
            "fixture-process");

        TestAssert.False(
            production.MutableRoot.Equals(
                development.MutableRoot,
                StringComparison.OrdinalIgnoreCase),
            "Debug and installed builds must never share mutable state.");
        TestAssert.False(
            production.TemporaryRoot.Equals(
                development.TemporaryRoot,
                StringComparison.OrdinalIgnoreCase),
            "Debug and installed builds must never share temporary workspaces.");
        TestAssert.Equal(
            production.SharedRuntimeRoot,
            development.SharedRuntimeRoot,
            "Development and Production should reuse only the immutable runtime store.");
        TestAssert.Equal(
            production.SharedRuntimeRoot,
            test.SharedRuntimeRoot,
            "Tests may discover the immutable runtime store without owning its mutable state.");
        TestAssert.Equal(
            production.SharedInstallerRoot,
            development.SharedInstallerRoot,
            "Development and Production should reuse the retained signed installer.");
        TestAssert.True(
            test.MutableRoot.StartsWith(
                Path.Combine(temporary, "ReplayFoundry.Tests") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase),
            "Test mutable data must remain beneath the test temporary root.");
        TestAssert.False(
            test.MutableRoot.StartsWith(local, StringComparison.OrdinalIgnoreCase),
            "Tests must not write into LocalAppData.");
        return Task.CompletedTask;
    }

    private static Task TestExecutableUsesTestChannel()
    {
        ReplayFoundryDataRoots current = ReplayFoundryLocalDataPaths.Current;
        TestAssert.Equal(
            ReplayFoundryDataChannel.Test,
            current.Channel,
            "A test entry assembly must stamp the Test data channel.");
        TestAssert.True(
            current.MutableRoot.Contains(
                $"{Path.DirectorySeparatorChar}ReplayFoundry.Tests{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase),
            "The active test process must not resolve Production or Development state.");
        return Task.CompletedTask;
    }

    private static Task TemporaryPathsStayInsideChannel()
    {
        string child = ReplayFoundryLocalDataPaths.ResolveTemporary(
            Path.Combine("VisualSemantic", "fixture"));
        TestAssert.True(
            child.StartsWith(
                Path.TrimEndingDirectorySeparator(
                    ReplayFoundryLocalDataPaths.Current.TemporaryRoot) +
                Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase),
            "A temporary workspace must remain under its active channel root.");
        TestAssert.Throws<ArgumentException>(
            () => ReplayFoundryLocalDataPaths.ResolveTemporary(
                Path.Combine("..", "escape")),
            "A temporary workspace cannot escape through a parent segment.");
        TestAssert.Throws<ArgumentException>(
            () => ReplayFoundryLocalDataPaths.Resolve(
                "relative-state.json",
                "ignored.json"),
            "A mutable-store override must be fully qualified.");
        return Task.CompletedTask;
    }
}
