using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Processes;

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

    public WhisperCppTranscriptionProvider(
        WhisperCppProviderSettings settings)
        : this(
            settings,
            new WindowsProcessRunner(),
            new SystemWhisperCppWorkspaceFactory())
    {
    }

    internal WhisperCppTranscriptionProvider(
        WhisperCppProviderSettings settings,
        IProcessRunner processRunner,
        IWhisperCppWorkspaceFactory workspaceFactory)
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

        return initialization.Capabilities.ToPublic();
    }

    public async Task<AudioTranscriptionResult>
        TranscribeAsync(
            AudioTranscriptionRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

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
            ProcessRunResult result =
                await _processRunner.RunAsync(
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
                    cancellationToken);
            DateTimeOffset completedAtUtc =
                DateTimeOffset.UtcNow;

            if (!result.Succeeded)
            {
                throw new WhisperCppTranscriptionException(
                    "whisper.cpp could not transcribe the bounded audio neighborhood.",
                    Diagnostics(result));
            }

            if (!File.Exists(command.OutputJsonPath))
            {
                throw new WhisperCppTranscriptionException(
                    "whisper.cpp completed without creating structured JSON output.");
            }

            string json =
                await File.ReadAllTextAsync(
                    command.OutputJsonPath,
                    cancellationToken);
            WhisperCppParsedOutput parsed =
                WhisperCppOutputParser.Parse(
                    json,
                    request);
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
                parsed.Warnings);
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
