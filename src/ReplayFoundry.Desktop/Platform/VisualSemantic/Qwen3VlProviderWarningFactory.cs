using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlProviderWarningFactory
{
    public static IReadOnlyList<VisualSemanticWarning> CreateBatchWarnings(
        long? peakAllocatedGpuBytes)
    {
        if (peakAllocatedGpuBytes.HasValue)
        {
            return Array.Empty<VisualSemanticWarning>();
        }

        return
        [
            new VisualSemanticWarning(
                VisualSemanticWarningCode.PeakMemoryUnavailable,
                "The local Qwen host did not report peak allocated GPU memory."),
        ];
    }
}
