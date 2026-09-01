using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Studio.Projects;

namespace ReplayFoundry.Desktop.Features.Generate.RecentProjects;

public sealed class RecentGenerationProject
{
    private readonly ReadOnlyCollection<string> _sourcePaths;

    public RecentGenerationProject(
        string projectId,
        GenerationMode mode,
        IEnumerable<string> sourcePaths,
        int clipCount,
        DateTimeOffset createdAtUtc,
        bool isFinalized,
        bool isStudioReady = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(sourcePaths);
        string[] paths = sourcePaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!Enum.IsDefined(mode) || paths.Length == 0 || clipCount <= 0 ||
            createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A recent Generate project is invalid.");
        }

        ProjectId = projectId.Trim();
        Mode = mode;
        _sourcePaths = Array.AsReadOnly(paths);
        ClipCount = clipCount;
        CreatedAtUtc = createdAtUtc;
        IsFinalized = isFinalized;
        IsStudioReady = isStudioReady;
    }

    public string ProjectId { get; }
    public GenerationMode Mode { get; }
    public IReadOnlyList<string> SourcePaths => _sourcePaths;
    public int ClipCount { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public bool IsFinalized { get; }
    public bool IsStudioReady { get; }
    public string Title => Path.GetFileNameWithoutExtension(SourcePaths[0]);
    public string Detail =>
        $"{ClipCount} {(ClipCount == 1 ? "clip" : "clips")} · " +
        $"{(Mode == GenerationMode.Montage ? "Montage" : "Individual clips")} · " +
        $"{CreatedAtUtc.ToLocalTime():MMM d} · " +
        (IsStudioReady ? "Open Studio" : "Past session");
}

public interface IRecentGenerationProjectCatalog
{
    ReadOnlyObservableCollection<RecentGenerationProject> Projects { get; }
    int ClearAll();
    bool TryGetStudioProject(
        string projectId,
        out GenerationOutputProject? project);
}

public sealed class RecentGenerationProjectCatalog :
    IRecentGenerationProjectCatalog,
    IDisposable
{
    public const int MaximumItems = 10;
    private readonly IGenerationOutputSession _session;
    private readonly JsonRecentGenerationProjectStore _store;
    private readonly IStudioProjectStore? _studioProjectStore;
    private readonly ObservableCollection<RecentGenerationProject> _projects;
    private readonly Dictionary<string, GenerationOutputProject> _studioProjects =
        new(StringComparer.Ordinal);

    public RecentGenerationProjectCatalog(
        IGenerationOutputSession session,
        JsonRecentGenerationProjectStore? store = null,
        IStudioProjectStore? studioProjectStore = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _store = store ?? new JsonRecentGenerationProjectStore();
        _studioProjectStore = studioProjectStore;
        IReadOnlyList<RecentGenerationProject> stored = _store.Read();
        _projects = new ObservableCollection<RecentGenerationProject>(
            stored.Take(MaximumItems).Select(value => new RecentGenerationProject(
                value.ProjectId,
                value.Mode,
                value.SourcePaths,
                value.ClipCount,
                value.CreatedAtUtc,
                value.IsFinalized,
                _studioProjectStore?.Exists(value.ProjectId) == true)));
        PruneOverflow(stored.Skip(MaximumItems));
        Projects = new ReadOnlyObservableCollection<RecentGenerationProject>(_projects);
        _session.CurrentChanged += Session_CurrentChanged;
        if (_session.Current is not null)
        {
            Record(_session.Current);
        }
    }

    public ReadOnlyObservableCollection<RecentGenerationProject> Projects { get; }

    public int ClearAll()
    {
        RecentGenerationProject[] projects = _projects.ToArray();
        if (_session is IGenerationOutputSessionMaintenance maintenance)
        {
            maintenance.Clear();
        }
        foreach (RecentGenerationProject project in projects)
        {
            _studioProjectStore?.Delete(project.ProjectId);
        }
        _studioProjects.Clear();
        _projects.Clear();
        _store.Write([]);
        return projects.Length;
    }

    public bool TryGetStudioProject(
        string projectId,
        out GenerationOutputProject? project)
    {
        project = null;
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return false;
        }

        project = _session.Current?.Id.Equals(
            projectId,
            StringComparison.Ordinal) == true
                ? _session.Current
                : _studioProjects.GetValueOrDefault(projectId);
        if (project is null && _studioProjectStore is not null)
        {
            StudioProjectLoadResult loaded =
                _studioProjectStore.Load(projectId);
            if (loaded.CanOpen && loaded.Project is not null)
            {
                project = loaded.Project;
                _studioProjects[projectId] = project;
            }
        }
        return project is not null;
    }

    public void Dispose() => _session.CurrentChanged -= Session_CurrentChanged;

    private void Session_CurrentChanged(object? sender, GenerationOutputChangedEventArgs e)
    {
        if (e.Current is not null)
        {
            Record(e.Current);
        }
    }

    private void Record(GenerationOutputProject project)
    {
        _studioProjects[project.Id] = project;
        var item = new RecentGenerationProject(
            project.Id,
            project.Mode,
            project.Assets.Select(static asset => asset.SourceFullPath),
            project.Assets.Count,
            project.CreatedAtUtc,
            project.IsFinalized,
            isStudioReady: true);
        RecentGenerationProject? existing = _projects.FirstOrDefault(value =>
            value.ProjectId.Equals(item.ProjectId, StringComparison.Ordinal));
        if (existing is not null)
        {
            _projects.Remove(existing);
        }
        _projects.Insert(0, item);
        while (_projects.Count > MaximumItems)
        {
            RecentGenerationProject removed = _projects[^1];
            _projects.RemoveAt(_projects.Count - 1);
            if (_session.Current?.Id.Equals(
                    removed.ProjectId,
                    StringComparison.Ordinal) != true)
            {
                _studioProjects.Remove(removed.ProjectId);
                TryDeleteStudioProject(removed.ProjectId);
            }
        }
        try
        {
            _store.Write(_projects);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A recent-project shortcut is convenience state. It must never
            // turn a successfully generated Studio project into a failure.
        }
    }

    private void PruneOverflow(IEnumerable<RecentGenerationProject> overflow)
    {
        RecentGenerationProject[] stale = overflow.ToArray();
        if (stale.Length == 0)
        {
            return;
        }
        foreach (RecentGenerationProject project in stale)
        {
            TryDeleteStudioProject(project.ProjectId);
        }
        try
        {
            _store.Write(_projects);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void TryDeleteStudioProject(string projectId)
    {
        try
        {
            _studioProjectStore?.Delete(projectId);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public interface IRecentProjectsClearConfirmation
{
    bool ConfirmClear(int projectCount);
}
