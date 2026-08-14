using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Rendering;

namespace ReplayFoundry.Desktop.Features.Library;

public sealed class LibraryMediaAsset
{
    public LibraryMediaAsset(
        string id,
        string projectId,
        GenerationMode mode,
        int rank,
        string outputFullPath,
        string? thumbnailFullPath,
        TimeSpan duration,
        int outputWidth,
        int outputHeight,
        string title,
        string description,
        IEnumerable<string> tags,
        DateTimeOffset addedAtUtc,
        int contributingCandidateCount = 1,
        IEnumerable<string>? sourceCandidateIds = null)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(projectId) ||
            !Enum.IsDefined(mode) ||
            rank <= 0 ||
            !Path.IsPathFullyQualified(outputFullPath) ||
            thumbnailFullPath is not null &&
            !Path.IsPathFullyQualified(thumbnailFullPath) ||
            duration <= TimeSpan.Zero ||
            outputWidth <= 0 ||
            outputHeight <= 0 ||
            string.IsNullOrWhiteSpace(title) ||
            addedAtUtc.Offset != TimeSpan.Zero ||
            contributingCandidateCount <= 0)
        {
            throw new ArgumentException(
                "A Library asset requires finalized media and immutable display metadata.");
        }
        ArgumentNullException.ThrowIfNull(tags);
        string[] tagSnapshot = tags
            .Select(static value => value?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] sourceCandidateSnapshot = sourceCandidateIds?
            .Select(static value => value?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (sourceCandidateIds is not null &&
            sourceCandidateSnapshot.Length == 0)
        {
            throw new ArgumentException(
                "A supplied Library candidate identity list cannot be empty.",
                nameof(sourceCandidateIds));
        }

        Id = id.Trim();
        ProjectId = projectId.Trim();
        Mode = mode;
        Rank = rank;
        OutputFullPath = Path.GetFullPath(outputFullPath);
        ThumbnailFullPath = thumbnailFullPath is null
            ? null
            : Path.GetFullPath(thumbnailFullPath);
        Duration = duration;
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        Title = title.Trim();
        Description = description?.Trim() ?? string.Empty;
        Tags = Array.AsReadOnly(tagSnapshot);
        AddedAtUtc = addedAtUtc;
        ContributingCandidateCount = contributingCandidateCount;
        SourceCandidateIds = Array.AsReadOnly(sourceCandidateSnapshot);
    }

    public string Id { get; }
    public string ProjectId { get; }
    public GenerationMode Mode { get; }
    public int Rank { get; }
    public string OutputFullPath { get; }
    public string? ThumbnailFullPath { get; }
    public TimeSpan Duration { get; }
    public int OutputWidth { get; }
    public int OutputHeight { get; }
    public string AspectRatioText
    {
        get
        {
            double ratio = OutputWidth / (double)OutputHeight;
            if (Math.Abs(ratio - 16d / 9d) < 0.02) return "16:9";
            if (Math.Abs(ratio - 9d / 16d) < 0.02) return "9:16";
            if (Math.Abs(ratio - 1d) < 0.02) return "1:1";
            if (Math.Abs(ratio - 4d / 3d) < 0.02) return "4:3";
            return $"{OutputWidth} × {OutputHeight}";
        }
    }
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<string> Tags { get; }
    public DateTimeOffset AddedAtUtc { get; }
    public int ContributingCandidateCount { get; }
    public IReadOnlyList<string> SourceCandidateIds { get; }
    public bool IsAvailable => File.Exists(OutputFullPath);
    public string DisplayName => Title;

    public LibraryMediaAsset Relink(
        string outputFullPath,
        string? thumbnailFullPath) =>
        new(
            Id,
            ProjectId,
            Mode,
            Rank,
            outputFullPath,
            thumbnailFullPath,
            Duration,
            OutputWidth,
            OutputHeight,
            Title,
            Description,
            Tags,
            AddedAtUtc,
            ContributingCandidateCount,
            SourceCandidateIds.Count == 0 ? null : SourceCandidateIds);

    public override string ToString() => DisplayName;
}

public interface ILibraryCatalogStore
{
    IReadOnlyList<LibraryMediaAsset> Current { get; }
    void Replace(IReadOnlyList<LibraryMediaAsset> assets);
}

public sealed class InMemoryLibraryCatalogStore : ILibraryCatalogStore
{
    private LibraryMediaAsset[] _current = [];
    public IReadOnlyList<LibraryMediaAsset> Current =>
        Array.AsReadOnly(_current.ToArray());
    public void Replace(IReadOnlyList<LibraryMediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _current = assets.ToArray();
    }
}

public interface ILibraryCatalog
{
    IReadOnlyList<LibraryMediaAsset> Assets { get; }
    event EventHandler? Changed;
}

public interface ILibraryAssetRelinker
{
    LibraryMediaAsset RelinkMissingAsset(
        string assetId,
        string replacementMediaFullPath);
}

public interface ILibraryAssetRemover
{
    void RemoveAsset(string assetId);

    void RemoveAssets(IReadOnlyCollection<string> assetIds)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        foreach (string assetId in assetIds)
        {
            RemoveAsset(assetId);
        }
    }
}

internal sealed class EmptyLibraryCatalog : ILibraryCatalog
{
    public static EmptyLibraryCatalog Instance { get; } = new();
    public IReadOnlyList<LibraryMediaAsset> Assets => [];
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}

public sealed class GenerationLibraryCatalog :
    ILibraryCatalog,
    ILibraryAssetRelinker,
    ILibraryAssetRemover,
    IDisposable
{
    private readonly IGenerationOutputSession _session;
    private readonly IGenerationRenderedOutputSession?
        _renderedOutputSession;
    private readonly ILibraryCatalogStore _store;
    private LibraryMediaAsset[] _assets;
    private bool _disposed;

    public GenerationLibraryCatalog(
        IGenerationOutputSession session,
        ILibraryCatalogStore store)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _renderedOutputSession = session as IGenerationRenderedOutputSession;
        _assets = Validate(store.Current).ToArray();
        _session.CurrentChanged += Session_CurrentChanged;
        if (_renderedOutputSession is not null)
        {
            _renderedOutputSession.RenderedOutputCommitted +=
                Session_RenderedOutputCommitted;
        }
        if (_session.Current is { IsFinalized: true } current)
        {
            Archive(current);
        }
    }

    public IReadOnlyList<LibraryMediaAsset> Assets =>
        new ReadOnlyCollection<LibraryMediaAsset>(_assets.ToArray());
    public event EventHandler? Changed;

    public LibraryMediaAsset RelinkMissingAsset(
        string assetId,
        string replacementMediaFullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementMediaFullPath);
        if (!Path.IsPathFullyQualified(replacementMediaFullPath))
        {
            throw new ArgumentException(
                "A replacement Library path must be fully qualified.",
                nameof(replacementMediaFullPath));
        }

        int index = Array.FindIndex(
            _assets,
            asset => asset.Id.Equals(assetId, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new ArgumentException(
                "The Library asset is not part of the current catalog.",
                nameof(assetId));
        }
        LibraryMediaAsset current = _assets[index];
        if (current.IsAvailable)
        {
            throw new InvalidOperationException(
                "Only a missing Library asset can be relinked.");
        }

        string replacement = Path.GetFullPath(replacementMediaFullPath);
        if (!File.Exists(replacement))
        {
            throw new FileNotFoundException(
                "The selected replacement video does not exist.",
                replacement);
        }
        string extension = Path.GetExtension(replacement);
        if (!SupportedVideoExtensions.Contains(extension))
        {
            throw new InvalidDataException(
                "The selected replacement is not a supported rendered video.");
        }

        string candidateThumbnail =
            Path.ChangeExtension(replacement, ".thumbnail.jpg");
        LibraryMediaAsset rebound = current.Relink(
            replacement,
            File.Exists(candidateThumbnail) ? candidateThumbnail : null);
        LibraryMediaAsset[] updated = _assets.ToArray();
        updated[index] = rebound;
        updated = Validate(updated).ToArray();
        _store.Replace(updated);
        _assets = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        return rebound;
    }

    public void RemoveAsset(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        int index = Array.FindIndex(
            _assets,
            asset => asset.Id.Equals(assetId, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new ArgumentException(
                "The Library asset is not part of the current catalog.",
                nameof(assetId));
        }

        LibraryMediaAsset[] updated = _assets
            .Where((_, assetIndex) => assetIndex != index)
            .ToArray();
        _store.Replace(updated);
        _assets = updated;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveAssets(IReadOnlyCollection<string> assetIds)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        string[] ids = assetIds
            .Select(static value => value?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            throw new ArgumentException(
                "At least one Library asset identifier is required.",
                nameof(assetIds));
        }

        var requested = new HashSet<string>(ids, StringComparer.Ordinal);
        if (requested.Any(id => !_assets.Any(asset =>
                asset.Id.Equals(id, StringComparison.Ordinal))))
        {
            throw new ArgumentException(
                "Every removed Library asset must belong to the current catalog.",
                nameof(assetIds));
        }

        LibraryMediaAsset[] updated = _assets
            .Where(asset => !requested.Contains(asset.Id))
            .ToArray();
        _store.Replace(updated);
        _assets = updated;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.CurrentChanged -= Session_CurrentChanged;
        if (_renderedOutputSession is not null)
        {
            _renderedOutputSession.RenderedOutputCommitted -=
                Session_RenderedOutputCommitted;
        }
    }

    private void Session_CurrentChanged(
        object? sender,
        GenerationOutputChangedEventArgs e)
    {
        if (e.Current is { IsFinalized: true } project)
        {
            Archive(project);
        }
    }

    private void Session_RenderedOutputCommitted(
        object? sender,
        GenerationRenderedOutputEventArgs e) =>
        Archive(e.RenderedProject);

    private void Archive(GenerationOutputProject project)
    {
        LibraryMediaAsset[] incoming = Build(project);
        LibraryMediaAsset[] updated =
        [
            .. incoming,
            .. _assets.Where(asset =>
                !asset.ProjectId.Equals(project.Id, StringComparison.Ordinal)),
        ];
        updated = Validate(updated).ToArray();
        _store.Replace(updated);
        _assets = updated;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IEnumerable<LibraryMediaAsset> Validate(
        IEnumerable<LibraryMediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        LibraryMediaAsset[] snapshot = assets.ToArray();
        if (snapshot.Any(static value => value is null) ||
            snapshot.Select(static value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
        {
            throw new InvalidDataException(
                "Library asset identifiers must be nonnull and unique.");
        }
        return snapshot.OrderByDescending(static value => value.AddedAtUtc)
            .ThenBy(static value => value.Rank);
    }

    private static LibraryMediaAsset[] Build(GenerationOutputProject project)
    {
        GenerationOutputAsset[] included = project.IncludedAssets.ToArray();
        if (included.Length == 0 || included.Any(static asset => !asset.IsRendered))
        {
            throw new ArgumentException(
                "Only a finalized project with rendered included assets can enter Library.",
                nameof(project));
        }
        DateTimeOffset added = project.FinalizedAtUtc!.Value;
        if (project.Mode == GenerationMode.Montage)
        {
            GenerationOutputAsset first = included[0];
            return
            [
                Create(
                    project.Id + "-montage",
                    project,
                    first,
                    TimeSpan.FromTicks(included.Sum(static value => value.Duration.Ticks)),
                    added,
                    included.Length,
                    titleSuffix: " · Montage",
                    sourceCandidateIds: included.Select(static asset => asset.Id)),
            ];
        }

        return included.Select(asset => Create(
                CreateAssetId(project.Id, asset.Id),
                project,
                asset,
                asset.Duration,
                added,
                1,
                titleSuffix: string.Empty,
                sourceCandidateIds: [asset.Id]))
            .ToArray();
    }

    private static readonly HashSet<string> SupportedVideoExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".mov", ".avi",
        };

    private static string CreateAssetId(
        string projectId,
        string candidateId) =>
        $"{projectId}-{candidateId}";

    private static LibraryMediaAsset Create(
        string id,
        GenerationOutputProject project,
        GenerationOutputAsset asset,
        TimeSpan duration,
        DateTimeOffset added,
        int contributingCount,
        string titleSuffix,
        IEnumerable<string> sourceCandidateIds)
    {
        GenerationClipOutputProfile profile =
            GenerationClipOutputProfile.FromReference(
                asset.SourceMedia.PrimaryVideoStream);
        string? title = asset.EditorialMetadata?.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(asset.OutputFullPath!);
        }
        return new LibraryMediaAsset(
            id,
            project.Id,
            project.Mode,
            asset.Rank,
            asset.OutputFullPath!,
            asset.ThumbnailFullPath,
            duration,
            profile.Width,
            profile.Height,
            title + titleSuffix,
            asset.EditorialMetadata?.Description ?? string.Empty,
            asset.EditorialMetadata?.Tags ?? [],
            added,
            contributingCount,
            sourceCandidateIds);
    }
}
