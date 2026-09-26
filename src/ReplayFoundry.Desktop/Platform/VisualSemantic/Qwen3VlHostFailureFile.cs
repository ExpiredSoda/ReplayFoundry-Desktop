using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlHostFailureFile
{
    public static void RequireAvailable(string? path)
    {
        if (path is not null &&
            File.Exists(path))
        {
            throw new Qwen3VlInferenceException(
                $"The configured Qwen host failure-output path already exists and will not be overwritten: '{path}'.");
        }
    }

    public static void RequireAbsentAfterSuccess(
        string? path)
    {
        if (path is not null &&
            File.Exists(path))
        {
            throw new Qwen3VlInferenceException(
                "The Qwen host reported process success but created a failure envelope.");
        }
    }

    public static async Task<Qwen3VlHostFailureEnvelope?>
        ReadIfPresentAsync(
            string? path,
            int maximumBytes,
            Qwen3VlHostCommand expectedCommand,
            VisualSemanticBatchRequest request,
            int expectedExitCode,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ReadIfPresentAsync(
            path,
            maximumBytes,
            expectedCommand,
            Qwen3VlHostFailureParseContext.FromBatchRequest(request),
            expectedExitCode,
            cancellationToken);
    }

    internal static async Task<Qwen3VlHostFailureEnvelope?>
        ReadIfPresentAsync(
            string? path,
            int maximumBytes,
            Qwen3VlHostCommand expectedCommand,
            Qwen3VlHostFailureParseContext request,
            int expectedExitCode,
            CancellationToken cancellationToken)
    {
        if (path is null ||
            !File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);

        if (info.Length <= 0 ||
            info.Length > maximumBytes)
        {
            throw new Qwen3VlOutputParseException(
                $"The structured Qwen host failure envelope must contain 1 to {maximumBytes:N0} bytes.");
        }

        string json =
            await File.ReadAllTextAsync(
                path,
                cancellationToken);

        return Qwen3VlHostFailureParser.Parse(
            json,
            expectedCommand,
            request,
            expectedExitCode);
    }
}
