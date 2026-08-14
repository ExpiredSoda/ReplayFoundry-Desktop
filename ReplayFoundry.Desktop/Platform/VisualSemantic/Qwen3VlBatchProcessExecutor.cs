using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed class Qwen3VlBatchProcessExecutor

{
    private readonly Qwen3VlBatchHostSettings _settings;
    private readonly IProcessRunner _processRunner;
    private readonly IQwen3VlBatchWorkspaceFactory _workspaceFactory;
    private readonly Qwen3VlRuntimeIntegrityVerifier _integrityVerifier;
    private readonly Qwen3VlInitializationCoordinator _initializationCoordinator;

    internal Qwen3VlBatchProcessExecutor(
        Qwen3VlBatchHostSettings settings)
        : this(
            settings,
            new WindowsProcessRunner(),
            new SystemQwen3VlBatchWorkspaceFactory())
    {
    }

    internal Qwen3VlBatchProcessExecutor(
        Qwen3VlBatchHostSettings settings,
        IProcessRunner processRunner,
        IQwen3VlBatchWorkspaceFactory workspaceFactory)
    {
        _settings =
            settings ??
            throw new ArgumentNullException(nameof(settings));
        _processRunner =
            processRunner ??
            throw new ArgumentNullException(nameof(processRunner));
        _workspaceFactory =
            workspaceFactory ??
            throw new ArgumentNullException(nameof(workspaceFactory));
        _integrityVerifier =
            new Qwen3VlRuntimeIntegrityVerifier(_settings);
        _initializationCoordinator =
            new Qwen3VlInitializationCoordinator(
                _settings,
                _processRunner,
                _workspaceFactory,
                _integrityVerifier);
    }

    public InferenceProviderIdentity Identity { get; } =
        new(
            "Qwen3-VL Transformers batch host",
            "runtime-probed",
            Qwen3VlRuntimeContract.AdapterVersion);

    public async Task<VisualSemanticBatchResult> ObserveAsync(
        VisualSemanticBatchRequest request,
        CancellationToken cancellationToken)
    {
        Qwen3VlObservationWithAttemptResult result =
            await ObserveWithAttemptAsync(
                request,
                cancellationToken);
        return result.Result;
    }

    internal async Task<Qwen3VlObservationWithAttemptResult>
        ObserveWithAttemptAsync(
            VisualSemanticBatchRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _integrityVerifier.ValidateRequestModel(request.Model);
        _integrityVerifier.ValidateRawAuditSourceSeparation(request);
        _integrityVerifier.ValidateFailureOutputSourceSeparation(request);
        Qwen3VlBatchWorkspace? workspace = null;

        try
        {
            await Qwen3VlRuntimeIntegrityVerifier.VerifyInputsAsync(
                request,
                cancellationToken);
            Qwen3VlInitialization initialization =
                await _initializationCoordinator.GetInitializationTask(
                        request,
                        cancellationToken)
                    .WaitAsync(cancellationToken);
            _integrityVerifier.VerifyRuntimeIntegrity(initialization);
            request.Model.VerifyInstalledFiles(cancellationToken);
            workspace = _workspaceFactory.Create();

            await Qwen3VlBatchRequestJsonWriter.WriteAsync(
                workspace.InputBatchPath,
                request,
                cancellationToken);
            Qwen3VlHostFailureFile.RequireAvailable(
                _settings.FailureOutputPath);
            Qwen3VlBatchCommand command =
                Qwen3VlBatchCommandBuilder.BuildRun(
                    _settings,
                    workspace);
            DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
            ProcessRunResult processResult =
                await _processRunner.RunAsync(
                    CreateProcessRequest(
                        command,
                        workspace.DirectoryPath),
                    cancellationToken);
            DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
            bool providerCaseFailuresDetected =
                processResult.ExitCode == 9;
            Qwen3VlProviderAttemptBatch? attemptBatch = null;
            Exception? attemptParseFailure = null;

            if (processResult.Succeeded ||
                providerCaseFailuresDetected)
            {
                try
                {
                    string attemptJson =
                        await Qwen3VlProcessOutputReader.ReadAsync(
                            workspace.AttemptOutputPath,
                            _settings.MaximumStructuredOutputBytes,
                            cancellationToken);
                    Qwen3VlProviderAttemptBatch parsedAttempt =
                        Qwen3VlProviderAttemptBatchParser.Parse(
                            attemptJson,
                            request);
                    Qwen3VlAttemptResultCoordinator.RequireAttemptExecutionMatchesInitialization(
                        parsedAttempt,
                        initialization);
                    attemptBatch = parsedAttempt;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                    when (exception is
                          Qwen3VlInferenceException or
                          IOException or
                          UnauthorizedAccessException)
                {
                    attemptParseFailure = exception;
                }
            }

            if (!processResult.Succeeded)
            {
                (
                    Qwen3VlHostFailureEnvelope? hostFailure,
                    Exception? envelopeFailure
                ) =
                    await Qwen3VlFailureArtifactReader.TryReadHostFailureAsync(
                        _settings,
                        request,
                        Qwen3VlHostCommand.Run,
                        processResult,
                        cancellationToken);
                Exception? integrityFailure =
                    await Qwen3VlFailureArtifactReader.CapturePostFailureIntegrityFailureAsync(
                        _initializationCoordinator,
                        request,
                        initialization,
                        cancellationToken);
                Exception? attemptConsistencyFailure = null;

                if (providerCaseFailuresDetected)
                {
                    bool acceptedAggregateEnvelope =
                        envelopeFailure is null &&
                        hostFailure is
                        {
                            Stage:
                                Qwen3VlHostFailureStage
                                    .OutputValidation,
                            Failure.ErrorCode:
                                Qwen3VlHostErrorCode
                                    .ProviderCaseFailuresDetected,
                        };

                    if (!acceptedAggregateEnvelope)
                    {
                        attemptConsistencyFailure =
                            new Qwen3VlOutputParseException(
                                "ProviderCaseFailuresDetected requires its complete typed aggregate failure envelope.");
                    }

                    if (attemptBatch is null)
                    {
                        attemptConsistencyFailure =
                            Qwen3VlFailureArtifactReader.CombineFailures(
                                attemptConsistencyFailure,
                                attemptParseFailure ??
                                new Qwen3VlOutputParseException(
                                    "ProviderCaseFailuresDetected requires a complete provider-attempt batch."));
                    }
                    else if (attemptBatch.IsCompleteSuccess)
                    {
                        attemptConsistencyFailure =
                            Qwen3VlFailureArtifactReader.CombineFailures(
                                attemptConsistencyFailure,
                                new Qwen3VlOutputParseException(
                                    "ProviderCaseFailuresDetected requires at least one failed provider attempt."));
                    }
                    else if (File.Exists(
                                 workspace.OutputBatchPath))
                    {
                        attemptConsistencyFailure =
                            Qwen3VlFailureArtifactReader.CombineFailures(
                                attemptConsistencyFailure,
                                new Qwen3VlOutputParseException(
                                    "A partial provider-attempt batch must not create completed observation output."));
                    }

                    bool attemptIsSafeToExpose =
                        attemptBatch is not null &&
                        !attemptBatch.IsCompleteSuccess &&
                        acceptedAggregateEnvelope &&
                        integrityFailure is null &&
                        attemptConsistencyFailure is null;
                    var caseFailureException =
                        new Qwen3VlInferenceException(
                            "The Qwen3-VL host completed the exhaustive attempt but one or more cases failed.",
                            Qwen3VlProcessOutputReader.Diagnostics(
                                processResult),
                            innerException:
                                Qwen3VlFailureArtifactReader.CombineFailures(
                                    envelopeFailure,
                                    integrityFailure,
                                    attemptConsistencyFailure),
                            hostFailure: hostFailure,
                            failureEnvelopeParseException:
                                envelopeFailure,
                            postFailureIntegrityException:
                                integrityFailure)
                        {
                            AttemptBatch =
                                attemptIsSafeToExpose
                                    ? attemptBatch
                                    : null,
                        };
                    throw caseFailureException;
                }

                throw new Qwen3VlInferenceException(
                    "The Qwen3-VL host could not observe the bounded visual-semantic batch.",
                    Qwen3VlProcessOutputReader.Diagnostics(
                        processResult),
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

            if (attemptParseFailure is not null)
            {
                if (attemptParseFailure is
                    Qwen3VlOutputParseException
                    outputParseException)
                {
                    throw outputParseException;
                }

                throw new Qwen3VlInferenceException(
                    "The Qwen3-VL host returned invalid provider-attempt output.",
                    innerException: attemptParseFailure);
            }

            if (attemptBatch is null ||
                !attemptBatch.IsCompleteSuccess)
            {
                throw new Qwen3VlInferenceException(
                    "A successful Qwen3-VL process requires one successful provider attempt per request.");
            }

            string outputJson =
                await Qwen3VlProcessOutputReader.ReadAsync(
                    workspace.OutputBatchPath,
                    _settings.MaximumStructuredOutputBytes,
                    cancellationToken);
            Qwen3VlParsedBatchResult parsed =
                Qwen3VlBatchResultParser.ParseBatch(
                    outputJson,
                    request);
            Qwen3VlAttemptResultCoordinator.RequireAttemptMatchesCompleted(
                attemptBatch,
                parsed);

            if (!string.Equals(
                    parsed.Backend,
                    Qwen3VlRuntimeContract.ExecutionBackend,
                    StringComparison.Ordinal))
            {
                throw new Qwen3VlInferenceException(
                    $"The Qwen3-VL batch host must use the exact '{Qwen3VlRuntimeContract.ExecutionBackend}' backend; fallback is not permitted.");
            }

            if (!string.Equals(
                    initialization.Probe.Device,
                    parsed.Device,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    initialization.Probe.Backend,
                    parsed.Backend,
                    StringComparison.Ordinal))
            {
                throw new Qwen3VlInferenceException(
                    "The Qwen3-VL execution backend changed after capability probing.");
            }

            await Qwen3VlRuntimeIntegrityVerifier.VerifyInputsAsync(
                request,
                cancellationToken);
            _integrityVerifier.VerifyRuntimeIntegrity(initialization);
            request.Model.VerifyInstalledFiles(cancellationToken);
            return Qwen3VlResultMapper.Map(
                Identity,
                _settings,
                request,
                parsed,
                attemptBatch,
                initialization.PythonExecutableSha256,
                initialization.HostScriptSha256,
                initialization.ProbeOutput,
                startedAtUtc,
                completedAtUtc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Qwen3VlInitializationException)
        {
            throw;
        }
        catch (Qwen3VlInferenceException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  InvalidDataException or
                  UnauthorizedAccessException or
                  ProcessExecutionException)
        {
            throw new Qwen3VlInferenceException(
                "Replay Foundry could not run the local Qwen3-VL batch host.",
                diagnosticDetails:
                    exception.Message,
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
