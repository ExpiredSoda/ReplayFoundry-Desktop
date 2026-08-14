using System.Globalization;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataFailureSummary
{
    internal static string For(Qwen3VlHostFailureEnvelope? failure)
    {
        Qwen3VlGroundedMemoryPolicyAudit? memory =
            failure?.GroundedMemoryPolicy;
        if (memory is null)
        {
            return string.Empty;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $" Memory telemetry: outcome={memory.RuntimeOutcome}; " +
            $"allocatorLimitBytes={memory.AllocatorLimitBytes}; " +
            $"peakAllocatedGpuBytes={Value(memory.PeakAllocatedGpuBytes)}; " +
            $"peakReservedGpuBytes={Value(memory.PeakReservedGpuBytes)}; " +
            $"endFreeDeviceMemoryBytes={Value(memory.EndFreeDeviceMemoryBytes)}.");
    }

    private static string Value(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unavailable";
}
