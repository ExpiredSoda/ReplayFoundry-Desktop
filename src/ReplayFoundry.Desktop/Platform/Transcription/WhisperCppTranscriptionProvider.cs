using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Processes;

using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.Desktop.Platform.Transcription;

public sealed class WhisperCppTranscriptionProvider :
    IAudioTranscriptionProvider
{
    private const string AdapterVersion = "0.1.0";
    private readonly WhisperCppProviderSettings _settings;
    private readonly IProcessRunner _processRunner;
    private readonly IWhisperCppWorkspaceFactory
        _workspaceFactory;
    private readonly object _initializationSync = new();
    private Task<WhisperCppInitialization>? _initializationTask;
    private readonly WhisperCppTranscriptCache? _cache;

    public WhisperCppTranscriptionProvider(
        WhisperCppProviderSettings settings)
        : this(
            settings,
            new WindowsProcessRunner(),
            new SystemWhisperCppWorkspaceFactory(), usePersistentCache: true)
    {
    }

    internal WhisperCppTranscriptionProvider(
        WhisperCppProviderSettings settings,
        IProcessRunner processRunner,
        IWhisperCppWorkspaceFactory workspaceFactory, bool usePersistentCache = false)
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
        _cache = usePersistentCache ? new WhisperCppTranscriptCache() : null;
    }

    public InferenceProviderIdentity Identity { get; } =
        new(
            "whisper.cpp CLI",
            "runtime-probed",
            AdapterVersion);

    public async Task<AudioTranscriptionProviderCapabilities>
        GetCapabilitiesAsync(
            CancellationToken cancellationToken)
    {
        WhisperCppInitialization initialization =
            await GetInitializationTask()
                .WaitAsync(cancellationToken);

        return initialization.Capabilities.ToPublic(_settings.Model.LanguageCapabilities);
    }

    public async Task<AudioTranscriptionResult>
        TranscribeAsync(
            AudioTranscriptionRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (_settings.Model.LanguageCapabilities?.GetBlockingReason(request.Options) is string languageReason)
            throw new WhisperCppTranscriptionException(languageReason);
        if (_settings.Model.LanguageCapabilities?.SourceIdentity is string expectedModelIdentity &&
            !string.Equals(expectedModelIdentity,
                WhisperGgmlLanguageCapabilities.Resolve(_settings.Model.ModelPath).SourceIdentity, StringComparison.Ordinal))
            throw new WhisperCppTranscriptionException(
                "The speech model changed after its language capabilities were inspected. Reopen Replay Foundry before transcribing.");

        if (!string.Equals(
                request.ModelSettings.ModelPath,
                _settings.Model.ModelPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WhisperCppTranscriptionException(
                "The transcription request model does not match the initialized provider model.");
        }

        WhisperCppInitialization initialization =
            await GetInitializationTask()
                .WaitAsync(cancellationToken);
        WhisperCppWorkspace? workspace = null;

        try
        {
            workspace = _workspaceFactory.Create();
            WhisperCppCommand command =
                WhisperCppCommandBuilder.Build(
                    request,
                    initialization.Capabilities,
                    workspace.OutputPrefix,
                    _settings.VadModelPath,
                    initialization.VadModelSha256);
            DateTimeOffset startedAtUtc =
                DateTimeOffset.UtcNow;
            string? cacheKey = _cache is null ? null : await WhisperCppTranscriptCache.KeyAsync(request,
                initialization.Model.Sha256, initialization.ExecutableSha256, command.NormalizedOptions, cancellationToken);
            WhisperCppCachedTranscript? cached = cacheKey is null ? null : await _cache!.ReadAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                try { _ = Parse(cached.Json, new(0, cached.Output, cached.Error, cached.Duration)); }
                catch (Exception error) when (error is WhisperCppTranscriptionException or System.Text.Json.JsonException or ArgumentException)
                { cached = null; }
            }
            ProcessRunResult result = cached is not null ? new(0, cached.Output, cached.Error, cached.Duration) :
                await MediaWorkBudget.RunAsync(_processRunner,
                    new ProcessRunRequest(
                        _settings.ExecutablePath,
                        command.Arguments,
                        request.Options
                            .MaximumProcessDuration,
                        workspace.DirectoryPath,
                        maxStandardOutputCharacters:
                            1024 * 1024,
                        maxStandardErrorCharacters:
                            1024 * 1024),
                    MediaWorkPriority.Foreground, MediaWorkKind.HeavyAi, cancellationToken);
            DateTimeOffset completedAtUtc =
                DateTimeOffset.UtcNow;
            if (cached is not null) { startedAtUtc = cached.StartedAtUtc; completedAtUtc = cached.CompletedAtUtc; }

            if (!result.Succeeded)
            {
                throw new WhisperCppTranscriptionException(
                    "whisper.cpp could not transcribe the bounded audio neighborhood.",
                    Diagnostics(result));
            }

            if (cached is null && !File.Exists(command.OutputJsonPath))
            {
                throw new WhisperCppTranscriptionException(
                    "whisper.cpp completed without creating structured JSON output.");
            }

            string json =
                cached?.Json ?? await File.ReadAllTextAsync(
                    command.OutputJsonPath,
                    cancellationToken);
            WhisperCppParsedOutput Parse(string payload, ProcessRunResult executionResult)
            {
                WhisperCppVadTimeMap? vadTimeMap =
                request.Options.RequestWordTimestamps &&
                command.NormalizedOptions.ContainsKey("vad")
                    ? WhisperCppVadTimeMap.TryParse(
                        TimeSpan.FromSeconds(
                            WhisperCppCommandBuilder
                                .VadSamplesOverlapSeconds),
                        executionResult.StandardOutput,
                        executionResult.StandardError)
                    : null;
                return WhisperCppOutputParser.Parse(
                    payload,
                    request,
                    vadTimeMap);
            }
            WhisperCppParsedOutput parsed = Parse(json, result);
            if (cached is null && cacheKey is not null)
                await _cache!.SaveAsync(new(cacheKey, json, result.StandardOutput, result.StandardError,
                    startedAtUtc, completedAtUtc, result.Duration), cancellationToken);
            string? backend =
                TryReadBackend(
                    result.StandardOutput,
                    result.StandardError);
            var inferenceWarnings =
                new List<InferenceWarning>();

            if (backend is null)
            {
                inferenceWarnings.Add(
                    new InferenceWarning(
                        InferenceWarningCode
                            .ExecutionBackendUnavailable,
                        "The installed CLI did not report an execution backend."));
            }

            var execution =
                new InferenceExecutionManifest(
                    Identity,
                    initialization.ExecutablePath,
                    initialization.ExecutableSha256,
                    initialization.VersionOutput,
                    initialization.Model,
                    command.NormalizedOptions,
                    startedAtUtc,
                    completedAtUtc,
                    result.Duration,
                    wasCancelled: false,
                    backend,
                    inferenceWarnings);
            var manifest =
                new AudioTranscriptionManifest(
                    request.NeighborhoodId,
                    request.InputDuration,
                    request.AbsoluteSourceOffset,
                    request.SourceDuration,
                    request.AbsoluteAudioStreamIndex,
                    request.Options,
                    execution);

            return new AudioTranscriptionResult(
                request.NeighborhoodId,
                request.AbsoluteAudioStreamIndex,
                parsed.Segments,
                manifest,
                parsed.DetectedLanguage,
                parsed.Warnings, wasReused: cached is not null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WhisperCppInitializationException)
        {
            throw;
        }
        catch (WhisperCppTranscriptionException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  UnauthorizedAccessException or
                  ProcessExecutionException)
        {
            throw new WhisperCppTranscriptionException(
                "Replay Foundry could not run the local whisper.cpp transcription.",
                innerException: exception);
        }
        finally
        {
            workspace?.Cleanup();
        }
    }

    private Task<WhisperCppInitialization>
        GetInitializationTask()
    {
        lock (_initializationSync)
        {
            if (_initializationTask is null ||
                _initializationTask.IsFaulted ||
                _initializationTask.IsCanceled)
            {
                _initializationTask =
                    InitializeAsync();
            }

            return _initializationTask;
        }
    }

    private async Task<WhisperCppInitialization>
        InitializeAsync()
    {
        if (!File.Exists(_settings.ExecutablePath))
        {
            throw new WhisperCppInitializationException(
                $"The configured whisper.cpp executable does not exist: '{_settings.ExecutablePath}'.");
        }

        if (!File.Exists(_settings.Model.ModelPath))
        {
            throw new WhisperCppInitializationException(
                $"The configured whisper.cpp model does not exist: '{_settings.Model.ModelPath}'.");
        }

        if (_settings.VadModelPath is not null &&
            !File.Exists(_settings.VadModelPath))
        {
            throw new WhisperCppInitializationException(
                $"The configured whisper.cpp VAD model does not exist: '{_settings.VadModelPath}'.");
        }

        try
        {
            ProcessRunResult help =
                await _processRunner.RunAsync(
                    new ProcessRunRequest(
                        _settings.ExecutablePath,
                        ["--help"],
                        TimeSpan.FromSeconds(15),
                        maxStandardOutputCharacters:
                            512 * 1024,
                        maxStandardErrorCharacters:
                            512 * 1024),
                    CancellationToken.None);
            string helpOutput =
                string.Join(
                    Environment.NewLine,
                    help.StandardOutput,
                    help.StandardError);

            if (!help.Succeeded &&
                string.IsNullOrWhiteSpace(helpOutput))
            {
                throw new WhisperCppInitializationException(
                    "The configured whisper.cpp executable did not return usable help output.",
                    Diagnostics(help));
            }

            WhisperCppCliCapabilities capabilities =
                WhisperCppCliCapabilities.Discover(
                    helpOutput);
            ProcessRunResult version =
                await _processRunner.RunAsync(
                    new ProcessRunRequest(
                        _settings.ExecutablePath,
                        [_settings.ExecutableVersionArgument],
                        TimeSpan.FromSeconds(15),
                        maxStandardOutputCharacters:
                            128 * 1024,
                        maxStandardErrorCharacters:
                            128 * 1024),
                    CancellationToken.None);
            string versionOutput =
                string.Join(
                    Environment.NewLine,
                    version.StandardOutput,
                    version.StandardError)
                .Trim();

            if (string.IsNullOrWhiteSpace(versionOutput))
            {
                versionOutput =
                    helpOutput
                        .Split(
                            ['\r', '\n'],
                            StringSplitOptions
                                .RemoveEmptyEntries)
                        .First()
                        .Trim();
            }

            var modelFile =
                new FileInfo(
                    _settings.Model.ModelPath);
            var model =
                new ModelArtifactManifest(
                    _settings.Model.DisplayName,
                    modelFile.FullName,
                    ModelArtifactManifest.ComputeSha256(
                        modelFile.FullName),
                    modelFile.Length,
                    new DateTimeOffset(
                        modelFile.LastWriteTimeUtc,
                        TimeSpan.Zero),
                    _settings.Model.ModelFormat,
                    _settings.Model.LicenseIdentifier,
                    _settings.Model.SourceUrlOrNote,
                    _settings.Model
                        .LanguageCapabilityDescription);

            return new WhisperCppInitialization(
                _settings.ExecutablePath,
                ModelArtifactManifest.ComputeSha256(
                    _settings.ExecutablePath),
                versionOutput,
                capabilities,
                model,
                _settings.VadModelPath is null
                    ? null
                    : ModelArtifactManifest.ComputeSha256(
                        _settings.VadModelPath));
        }
        catch (WhisperCppInitializationException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  UnauthorizedAccessException or
                  ProcessExecutionException)
        {
            throw new WhisperCppInitializationException(
                "Replay Foundry could not initialize the configured local whisper.cpp provider.",
                innerException: exception);
        }
    }

    private static string? TryReadBackend(
        params string[] outputs)
    {
        string? line =
            outputs
                .SelectMany(
                    static output =>
                        output.Split(
                            ['\r', '\n'],
                            StringSplitOptions
                                .RemoveEmptyEntries))
                .FirstOrDefault(
                    static value =>
                        value.Contains(
                            "backend",
                            StringComparison
                                .OrdinalIgnoreCase) ||
                        value.Contains(
                            "system_info",
                            StringComparison
                                .OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(line)
            ? null
            : line.Trim();
    }

    private static string Diagnostics(
        ProcessRunResult result) =>
        $"Exit code: {result.ExitCode}{Environment.NewLine}" +
        $"stdout: {result.StandardOutput.Trim()}{Environment.NewLine}" +
        $"stderr: {result.StandardError.Trim()}";

    private sealed record WhisperCppInitialization(
        string ExecutablePath,
        string ExecutableSha256,
        string VersionOutput,
        WhisperCppCliCapabilities Capabilities,
        ModelArtifactManifest Model,
        string? VadModelSha256);
}
