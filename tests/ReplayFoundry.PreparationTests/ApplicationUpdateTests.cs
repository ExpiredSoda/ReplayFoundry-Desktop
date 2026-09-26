using ReplayFoundry.Desktop.Composition;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.PreparationTests;

internal static class ApplicationUpdateTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Update restart protects work and becomes available after it finishes", RestartProtectsWork),
        new("Settings updates reflect availability, checks and preference changes", SettingsReflectService),
    ];

    private static Task RestartProtectsWork()
    {
        var idle = new ApplicationUpdateActivity(false, false, false, false, false, false);
        TestAssert.Equal<string?>(null, ApplicationUpdateReadiness.GetBlockReason(idle), "An idle app can restart.");
        foreach (var busy in new[]
        {
            idle with { Generation = true }, idle with { Rendering = true },
            idle with { Publishing = true }, idle with { Learning = true },
            idle with { UnsavedEdits = true }, idle with { OpenWorkflow = true },
        })
            TestAssert.True(!string.IsNullOrWhiteSpace(ApplicationUpdateReadiness.GetBlockReason(busy)), "Each active or unsaved workflow must defer installation with a reason.");
        return Task.CompletedTask;
    }

    private static Task SettingsReflectService()
    {
        using var viewModel = new ApplicationUpdateViewModel();
        TestAssert.False(viewModel.CheckForUpdatesCommand.CanExecute(null), "Design/test builds do not pretend to check.");
        var service = new FakeUpdates();
        viewModel.Attach(service);
        TestAssert.True(viewModel.CheckForUpdatesCommand.CanExecute(null), "Installed updater enables the check.");
        viewModel.CheckForUpdatesCommand.Execute(null);
        TestAssert.Equal(1, service.Checks, "One click starts one check.");
        viewModel.AutomaticChecksEnabled = true;
        TestAssert.True(service.AutomaticChecksEnabled, "The choice reaches the update service.");
        TestAssert.Equal("Checking", viewModel.Status, "Settings shows the current result, not a fixed success label.");
        service.Available = false;
        service.Notify();
        TestAssert.False(viewModel.CheckForUpdatesCommand.CanExecute(null), "Unavailable services disable checks again.");
        return Task.CompletedTask;
    }

    private sealed class FakeUpdates : IApplicationUpdateService
    {
        public event EventHandler? Changed;
        public bool Available { get; set; } = true;
        public bool IsAvailable => Available;
        public bool AutomaticChecksEnabled { get; set; }
        public string Status { get; private set; } = "Ready";
        public int Checks { get; private set; }
        public void CheckForUpdates() { Checks++; Status = "Checking"; Notify(); }
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
