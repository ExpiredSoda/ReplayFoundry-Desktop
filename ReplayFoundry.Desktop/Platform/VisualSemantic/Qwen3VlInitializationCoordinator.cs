using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed class Qwen3VlInitializationCoordinator
{
    private readonly Qwen3VlBatchHostSettings _settings;
    private readonly IProcessRunner _processRunner;
    private readonly IQwen3VlBatchWorkspaceFactory _workspaceFactory;
    private readonly Qwen3VlRuntimeIntegrityVerifier _integrityVerifier;
    private readonly object _sync = new();
    private Task<Qwen3VlInitialization>? _task;
    private string? _modelManifestSha256;

    internal Qwen3VlInitializationCoordinator(
        Qwen3VlBatchHostSettings settings,
        IProcessRunner processRunner,
        IQwen3VlBatchWorkspaceFactory workspaceFactory,
        Qwen3VlRuntimeIntegrityVerifier integrityVerifier)
    {
        _settings = settings;
        _processRunner = processRunner;
        _workspaceFactory = workspaceFactory;
        _integrityVerifier = integrityVerifier;
    }

    internal async Task VerifyPostFailureIntegrityAsync(
        VisualSemanticBatchRequest request,
        Qwen3VlInitialization initialization,
        CancellationToken cancellationToken)
    {
        await Qwen3VlRuntimeIntegrityVerifier.VerifyInputsAsync(
            request,
            cancellationToken);
        _integrityVerifier.VerifyRuntimeIntegrity(initialization);
        _integrityVerifier.VerifyFfmpegSharedLibraries();
        request.Model.VerifyInstalledFiles(
            cancellationToken);
    }

    internal Task<Qwen3VlInitialization> GetInitializationTask(
        VisualSemanticBatchRequest request,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_task is null ||
                _task.IsFaulted ||
                _task.IsCanceled ||
                !string.Equals(
                    _modelManifestSha256,
                    request.Model.ManifestSha256,
                    StringComparison.Ordinal))
            {
                _modelManifestSha256 =
                    request.Model.ManifestSha256;
                _task =
                    InitializeAsync(
                        request,
                        cancellationToken);
            }

            return _task;
        }
    }

    private async Task<Qwen3VlInitialization> InitializeAsync(
        VisualSemanticBatchRequest request,
        CancellationToken cancellationToken)
    {
        VisualSemanticModelManifest model = request.Model;
        _integrityVerifier.ValidateRequestModel(model);

        if (!File.Exists(_settings.PythonExecutablePath))
        {
            throw new Qwen3VlInitializationException(
                $"The configured Python executable does not exist: '{_settings.PythonExecutablePath}'.");
        }

        if (!File.Exists(_settings.HostScriptPath))
        {
            throw new Qwen3VlInitializationException(
                $"The configured Qwen host script does not exist: '{_settings.HostScriptPath}'.");
        }

        if (!Directory.Exists(_settings.ModelDirectoryPath))
        {
            throw new Qwen3VlInitializationException(
                $"The configured Qwen model directory does not exist: '{_settings.ModelDirectoryPath}'.");
        }

        _integrityVerifier.VerifyFfmpegSharedLibraries();
        Qwen3VlBatchWorkspace? workspace = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            model.VerifyInstalledFiles(cancellationToken);
            string pythonSha256 =
                ModelArtifactManifest.ComputeSha256(
                    _settings.PythonExecutablePath);
            string scriptSha256 =
                ModelArtifactManifest.ComputeSha256(
                    _settings.HostScriptPath);
            workspace = _workspaceFactory.Create();
            await Qwen3VlBatchRequestJsonWriter.WriteAsync(
                workspace.InputBatchPath,
                request,
                cancellationToken);
            Qwen3VlHostFailureFile.RequireAvailable(
                _settings.FailureOutputPath);
            Qwen3VlBatchCommand command =
                Qwen3VlBatchCommandBuilder.BuildProbe(
                    _settings,
                    workspace);
            ProcessRunResult result =
                await _processRunner.RunAsync(
                    CreateProcessRequest(
                        command,
                        workspace.DirectoryPath),
                    cancellationToken);

            if (!result.Succeeded)
            {
                (
                    Qwen3VlHostFailureEnvelope? hostFailure,
                    Exception? envelopeFailure
                ) =
                    await Qwen3VlFailureArtifactReader.TryReadHostFailureAsync(
                        _settings,
                        request,
                        Qwen3VlHostCommand.Probe,
                        result,
                        cancellationToken);
                Exception? integrityFailure =
                    await Qwen3VlFailureArtifactReader.CaptureInitializationFailureIntegrityAsync(
                        _settings,
                        _integrityVerifier,
                        request,
                        pythonSha256,
                        scriptSha256,
                        cancellationToken);
                throw new Qwen3VlInitializationException(
                    "The Qwen3-VL host capability probe failed.",
                    Qwen3VlProcessOutputReader.Diagnostics(
                        result),
                    innerException:
                        Qwen3VlFailureArtifactReader.CombineFailures(
                            envelopeFailure,
                            integrityFailure),
                    hostFailure: hostFailure,
                    failureEnvelopeParseException:
                        envelopeFailure,
                    postFailureIntegrityException:
                        integrityFailure);
            }

            Qwen3VlHostFailureFile.RequireAbsentAfterSuccess(
                _settings.FailureOutputPath);
            string probeOutput =
                await Qwen3VlProcessOutputReader.ReadAsync(
                    workspace.ProbeOutputPath,
                    maximumBytes: 64 * 1024,
                    cancellationToken);
            Qwen3VlProbeResult probe =
                Qwen3VlBatchResultParser.ParseProbe(
                    probeOutput,
                    model);

            if (!string.Equals(
                    probe.Backend,
                    Qwen3VlRuntimeContract.ExecutionBackend,
                    StringComparison.Ordinal) ||
                !probe.Packages.ContainsKey(
                    Qwen3VlBatchHostSettings
                        .SupportedVideoBackend) ||
                !probe.Packages.TryGetValue(
                    "hostVersion",
                    out string? hostVersion) ||
                !string.Equals(
                    hostVersion,
                    Qwen3VlRuntimeContract.HostVersion,
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "generationPolicyVersion",
                    out string? generationPolicyVersion) ||
                !string.Equals(
                    generationPolicyVersion,
                    VisualSemanticGenerationBudgetPolicy.Version,
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "generationPolicySha256",
                    out string? generationPolicySha256) ||
                !string.Equals(
                    generationPolicySha256,
                    VisualSemanticGenerationBudgetPolicy.Sha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !probe.Packages.TryGetValue(
                    "generationPolicyMaximumNewTokens",
                    out string? generationPolicyMaximum) ||
                !string.Equals(
                    generationPolicyMaximum,
                    VisualSemanticGenerationBudgetPolicy
                        .ActiveMaximumNewTokens
                        .ToString(
                            System.Globalization.CultureInfo
                                .InvariantCulture),
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "generationRuntimeMaximumNewTokens",
                    out string? generationRuntimeMaximum) ||
                !string.Equals(
                    generationRuntimeMaximum,
                    VisualSemanticGenerationBudgetPolicy
                        .ActiveMaximumNewTokens
                        .ToString(
                            System.Globalization.CultureInfo
                                .InvariantCulture),
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "generationDoSample",
                    out string? generationDoSample) ||
                !string.Equals(
                    generationDoSample,
                    "false",
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "generationNumberOfBeams",
                    out string? generationNumberOfBeams) ||
                !string.Equals(
                    generationNumberOfBeams,
                    VisualSemanticGenerationBudgetPolicy
                        .NumberOfBeams
                        .ToString(
                            System.Globalization.CultureInfo
                                .InvariantCulture),
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "generationUseCache",
                    out string? generationUseCache) ||
                !string.Equals(
                    generationUseCache,
                    "true",
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "generationPhaseADiagnosticGateActive",
                    out string? generationDiagnosticGate) ||
                !string.Equals(
                    generationDiagnosticGate,
                    "false",
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "trustedIdentityBindingPolicyVersion",
                    out string? identityPolicyVersion) ||
                !string.Equals(
                    identityPolicyVersion,
                    VisualSemanticIdentityBindingAudit
                        .SupportedPolicyVersion,
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "trustedIdentityBindingPolicySha256",
                    out string? identityPolicySha256) ||
                !string.Equals(
                    identityPolicySha256,
                    VisualSemanticIdentityBindingAudit
                        .SupportedPolicySha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !probe.Packages.TryGetValue(
                    "normalizationPolicyVersion",
                    out string? policyVersion) ||
                !string.Equals(
                    policyVersion,
                    VisualSemanticOutputNormalizationAudit
                        .SupportedPolicyVersion,
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "normalizationPolicySha256",
                    out string? policySha256) ||
                !string.Equals(
                    policySha256,
                    VisualSemanticOutputNormalizationAudit
                        .SupportedPolicySha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !probe.Packages.TryGetValue(
                    "executionTimingSchemaVersion",
                    out string? timingSchema) ||
                !string.Equals(
                    timingSchema,
                    VisualSemanticExecutionTimingManifest
                        .SupportedSchemaVersion,
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "candidateSamplingCoveragePolicyVersion",
                    out string? coveragePolicy) ||
                !string.Equals(
                    coveragePolicy,
                    VisualSemanticExecutionTimingManifest
                        .SupportedCoveragePolicyVersion,
                    StringComparison.Ordinal) ||
                !probe.Packages.TryGetValue(
                    "authoritativeSamplingTimingSource",
                    out string? timingSource) ||
                !string.Equals(
                    timingSource,
                    VisualSemanticExecutionTimingManifest
                        .SupportedTimingSource,
                    StringComparison.Ordinal))
            {
                throw new Qwen3VlInitializationException(
                    $"The Qwen3-VL capability probe must report host '{Qwen3VlRuntimeContract.HostVersion}', the frozen generation, output-normalization, and actual-PTS coverage policies, an inactive Phase-A diagnostic gate, the exact '{Qwen3VlRuntimeContract.ExecutionBackend}' backend, and TorchCodec; fallback is not permitted.");
            }

            return new Qwen3VlInitialization(
                pythonSha256,
                scriptSha256,
                probeOutput,
                probe);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Qwen3VlInitializationException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  UnauthorizedAccessException or
                  ProcessExecutionException)
        {
            throw new Qwen3VlInitializationException(
                "Replay Foundry could not initialize the local Qwen3-VL batch host.",
                innerException: exception);
        }
        finally
        {
            workspace?.Cleanup();
        }
    }

    private ProcessRunRequest CreateProcessRequest(
        Qwen3VlBatchCommand command,
        string workingDirectory) =>
        new(
            _settings.PythonExecutablePath,
            command.Arguments,
            _settings.ProcessTimeout,
            workingDirectory,
            _settings.MaximumStandardOutputCharacters,
            _settings.MaximumStandardErrorCharacters,
            _settings.EnvironmentVariables);

}

internal sealed record Qwen3VlInitialization(
    string PythonExecutableSha256,
    string HostScriptSha256,
    string ProbeOutput,
    Qwen3VlProbeResult Probe);
