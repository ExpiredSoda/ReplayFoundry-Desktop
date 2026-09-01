using ReplayFoundry.Desktop;
using ReplayFoundry.Desktop.Features.Diagnostics;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Platform.Diagnostics;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task ApplicationCompositionDisposesEditorialProvider()
    {
        var session = new GenerationOutputSession();
        var catalog = new GenerationLibraryCatalog(
            session,
            new InMemoryLibraryCatalogStore());
        var reports = new UserReportCoordinator(
            new UserReportConsentState(
                new InMemoryUserReportConsentStore()),
            new InMemoryUserReportOutbox(),
            new ReplayFoundryDiagnosticCollector(),
            new UserReportSanitizer(),
            new UnavailableUserReportTransport());
        var provider = new RecordingOwnedEditorialProvider();
        var composition = new ApplicationComposition(
            CreateShell(),
            catalog,
            reports,
            ownedEditorialMetadataProvider: provider);

        composition.Dispose();
        composition.Dispose();

        TestAssert.Equal(
            1,
            provider.DisposeCount,
            "Application composition must dispose its owned local editorial provider exactly once.");
        return Task.CompletedTask;
    }

    private sealed class RecordingOwnedEditorialProvider : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
