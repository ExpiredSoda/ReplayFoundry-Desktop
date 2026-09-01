using System.IO;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Composition;

internal static class LocalDataComposition
{
    public static ReplayFoundryLocalDataMaintenanceService Initialize()
    {
        var maintenance = new ReplayFoundryLocalDataMaintenanceService();
        ApplyScheduledReset(maintenance);
        return maintenance;
    }

    private static void ApplyScheduledReset(
        IReplayFoundryLocalDataMaintenance maintenance)
    {
        try
        {
            ReplayFoundryLocalDataCleanupResult result = maintenance
                .ApplyScheduledResetAsync()
                .GetAwaiter()
                .GetResult();
            foreach (string warning in result.Warnings)
            {
                SafeDiagnosticTrace.Write(
                    "Scheduled local-data reset warning",
                    warning);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            SafeDiagnosticTrace.Write(
                "The scheduled local-data reset could not be applied",
                exception);
        }
    }
}
