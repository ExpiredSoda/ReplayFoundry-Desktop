using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.PreparationTests;

internal sealed class TestMediaRightsConfirmation : IMediaRightsConfirmation
{
    public TestMediaRightsConfirmation(bool result = true) => Result = result;

    public bool Result { get; set; }
    public int RequestCount { get; private set; }
    public IReadOnlyList<SelectedVideoSource> LastSources { get; private set; } = [];

    public bool Confirm(IReadOnlyList<SelectedVideoSource> sources)
    {
        RequestCount++;
        LastSources = sources.ToArray();
        return Result;
    }
}

internal sealed class FakeMediaProbe :
    IMediaProbe
{
    private readonly Dictionary<string, MediaProbeResult> _results =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Exception> _failures =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requests { get; } = [];

    public void AddResult(
        MediaProbeResult result)
    {
        _results[result.FullPath] =
            result;
    }

    public void AddFailure(
        string fullPath,
        Exception exception)
    {
        _failures[fullPath] =
            exception;
    }

    public Task<MediaProbeResult> ProbeAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(fullPath);

        if (_failures.TryGetValue(
                fullPath,
                out Exception? failure) &&
            failure is not null)
        {
            return Task.FromException<MediaProbeResult>(
                failure);
        }

        return Task.FromResult(
            _results[fullPath]);
    }
}

internal sealed class FakeGenerationSourceFileSnapshotProvider :
    IGenerationSourceFileSnapshotProvider
{
    private readonly Dictionary<
        string,
        Queue<Func<GenerationSourceFileSnapshot>>> _captures =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<
        string,
        GenerationSourceFileSnapshot> _defaults =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requests { get; } = [];

    public void SetDefault(
        GenerationSourceFileSnapshot snapshot)
    {
        _defaults[snapshot.FullPath] = snapshot;
    }

    public void Enqueue(
        string fullPath,
        GenerationSourceFileSnapshot snapshot)
    {
        Enqueue(
            fullPath,
            () => snapshot);
    }

    public void EnqueueFailure(
        string fullPath,
        Exception exception)
    {
        Enqueue(
            fullPath,
            () => throw exception);
    }

    public GenerationSourceFileSnapshot Capture(
        string sourcePath)
    {
        Requests.Add(sourcePath);

        if (_captures.TryGetValue(
                sourcePath,
                out Queue<Func<GenerationSourceFileSnapshot>>? captures) &&
            captures.Count > 0)
        {
            return captures.Dequeue()();
        }

        if (_defaults.TryGetValue(
                sourcePath,
                out GenerationSourceFileSnapshot? snapshot))
        {
            return snapshot;
        }

        throw new InvalidOperationException(
            $"No snapshot was configured for '{sourcePath}'.");
    }

    private void Enqueue(
        string fullPath,
        Func<GenerationSourceFileSnapshot> capture)
    {
        if (!_captures.TryGetValue(
                fullPath,
                out Queue<Func<GenerationSourceFileSnapshot>>? captures))
        {
            captures = [];
            _captures.Add(
                fullPath,
                captures);
        }

        captures.Enqueue(capture);
    }
}

internal sealed class ScriptedProcessRunner :
    IProcessRunner
{
    private readonly Queue<
        Func<
            ProcessRunRequest,
            CancellationToken,
            Task<ProcessRunResult>>> _steps = [];

    public List<ProcessRunRequest> Requests { get; } = [];

    public void Enqueue(
        Func<
            ProcessRunRequest,
            CancellationToken,
            Task<ProcessRunResult>> step)
    {
        _steps.Enqueue(step);
    }

    public Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_steps.Count == 0)
        {
            throw new InvalidOperationException(
                "No scripted process step remains.");
        }

        return _steps.Dequeue()(
            request,
            cancellationToken);
    }
}

internal sealed class TestPreviewWorkspaceFactory :
    IPreviewWorkspaceFactory
{
    private readonly string _root;

    public TestPreviewWorkspaceFactory(
        string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public string? LastDirectory { get; private set; }

    public PreviewWorkspace Create()
    {
        string directory =
            Path.Combine(
                _root,
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        LastDirectory = directory;

        return new PreviewWorkspace(
            directory,
            Path.Combine(
                directory,
                "preview.png"));
    }
}

internal sealed class RecordingProgress<TValue> :
    IProgress<TValue>
{
    public List<TValue> Values { get; } = [];

    public void Report(
        TValue value)
    {
        Values.Add(value);
    }
}

internal sealed class EnvironmentVariableScope :
    IDisposable
{
    private readonly string _name;
    private readonly string? _previous;

    public EnvironmentVariableScope(
        string name,
        string value)
    {
        _name = name;
        _previous =
            Environment.GetEnvironmentVariable(
                name);

        Environment.SetEnvironmentVariable(
            name,
            value,
            EnvironmentVariableTarget.Process);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            _name,
            _previous,
            EnvironmentVariableTarget.Process);
    }
}
