using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlFailureArtifactReader
{
    internal static async Task<(
        Qwen3VlHostFailureEnvelope? HostFailure,
        Exception? ParseFailure)> TryReadHostFailureAsync(
            Qwen3VlBatchHostSettings settings,
            VisualSemanticBatchRequest request,
            Qwen3VlHostCommand command,
            ProcessRunResult processResult,
            CancellationToken cancellationToken)
    {
        try
        {
            return (
                await Qwen3VlHostFailureFile
                    .ReadIfPresentAsync(
                        settings.FailureOutputPath,
                        settings.MaximumStructuredOutputBytes,
                        command,
                        request,
                        processResult.ExitCode,
                        cancellationToken),
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  Qwen3VlOutputParseException or
                  IOException or
                  UnauthorizedAccessException)
        {
            return (null, exception);
        }
    }

    internal static async Task<Exception?>
        CapturePostFailureIntegrityFailureAsync(
            Qwen3VlInitializationCoordinator initializationCoordinator,
            VisualSemanticBatchRequest request,
            Qwen3VlInitialization initialization,
            CancellationToken cancellationToken)
    {
        try
        {
            await initializationCoordinator.VerifyPostFailureIntegrityAsync(
                request,
                initialization,
                cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  InvalidDataException or
                  UnauthorizedAccessException or
                  Qwen3VlInitializationException or
                  Qwen3VlInferenceException)
        {
            return exception;
        }
    }

    internal static async Task<Exception?>
        CaptureInitializationFailureIntegrityAsync(
        Qwen3VlBatchHostSettings settings,
        Qwen3VlRuntimeIntegrityVerifier integrityVerifier,
        VisualSemanticBatchRequest request,
        string expectedPythonSha256,
        string expectedHostSha256,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            integrityVerifier.VerifyFfmpegSharedLibraries();
            request.Model.VerifyInstalledFiles(
                cancellationToken);

            if (!string.Equals(
                    ModelArtifactManifest.ComputeSha256(
                        settings.PythonExecutablePath),
                    expectedPythonSha256,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    ModelArtifactManifest.ComputeSha256(
                        settings.HostScriptPath),
                    expectedHostSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The configured Python executable or Qwen host script changed during capability probing.");
            }

            await Qwen3VlRuntimeIntegrityVerifier.VerifyInputsAsync(
                request,
                cancellationToken);

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  InvalidDataException or
                  UnauthorizedAccessException or
                  Qwen3VlInitializationException)
        {
            return exception;
        }
    }

    internal static Exception? CombineFailures(
        params Exception?[] failures)
    {
        Exception[] present =
            failures
                .Where(static value => value is not null)
                .Cast<Exception>()
                .ToArray();

        return present.Length switch
        {
            0 => null,
            1 => present[0],
            _ => new AggregateException(
                "Several host-output or post-failure integrity checks failed.",
                present),
        };
    }

}
