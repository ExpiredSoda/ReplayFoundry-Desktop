using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Features.Publish.YouTube;

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
        IEnumerable<string>? sourceCandidateIds = null,
        string? libraryLabel = null,
        YouTubePublishProvenance? sourceProvenance = null)
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
        LibraryLabel = string.IsNullOrWhiteSpace(libraryLabel)
            ? Title
            : libraryLabel.Trim();
        Description = description?.Trim() ?? string.Empty;
        Tags = Array.AsReadOnly(tagSnapshot);
        AddedAtUtc = addedAtUtc;
        ContributingCandidateCount = contributingCandidateCount;
        SourceCandidateIds = Array.AsReadOnly(sourceCandidateSnapshot);
        SourceProvenance = sourceProvenance;
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
    /// <summary>
    /// The editable audience-facing title used to seed publishing surfaces.
    /// </summary>
    public string Title { get; }
    /// <summary>
    /// A conservative navigation label used only inside Library.
    /// </summary>
    public string LibraryLabel { get; }
    public string Description { get; }
    public IReadOnlyList<string> Tags { get; }
    public DateTimeOffset AddedAtUtc { get; }
    public int ContributingCandidateCount { get; }
    public IReadOnlyList<string> SourceCandidateIds { get; }
    public YouTubePublishProvenance? SourceProvenance { get; }
    public bool IsAvailable => File.Exists(OutputFullPath);
    public string DisplayName => LibraryLabel;

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
            SourceCandidateIds.Count == 0 ? null : SourceCandidateIds,
            LibraryLabel,
            SourceProvenance);

    public override string ToString() => DisplayName;
}

internal static class LibraryLabelPolicy
{
    private const int MaximumLabelLength = 180;
    private const int MaximumLiteralDetailLength = 72;

    internal static string CreateForClip(GenerationOutputAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string game = ResolveGameName(asset);
        GroundedEditorialBrief? brief =
            asset.EditorialMetadata?.GroundingAudit?.ResolvedBrief ??
            asset.EditorialContext?.EditorialBrief;
        string timestamp = FormatTimestamp(asset.SourceStart);
        if (brief is null)
        {
            return Bound($"{game} — {timestamp}", MaximumLabelLength);
        }

        string? literal = FindLiteralDetail(asset, brief);
        string descriptor = brief.PresentationKind switch
        {
            GroundedEditorialPresentationKind.DocumentOrLore =>
                DescribeAt("document", literal, timestamp),
            GroundedEditorialPresentationKind.InWorldRecording =>
                DescribeAt("in-world recording", literal, timestamp),
            GroundedEditorialPresentationKind.ObjectiveOrInterface =>
                DescribeAt("objective update", literal, timestamp),
            GroundedEditorialPresentationKind.MenuOrLoadout =>
                DescribeAt("menu or loadout", literal, timestamp),
            GroundedEditorialPresentationKind.CinematicSequence =>
                DescribeAt("cutscene", literal, timestamp),
            _ => DescribeMoment(brief.MomentKind, literal, timestamp),
        };
        return Bound($"{game} — {descriptor}", MaximumLabelLength);
    }

    internal static string CreateForMontage(
        IReadOnlyList<GenerationOutputAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets.Count == 0)
        {
            throw new ArgumentException(
                "A Library montage label requires at least one segment.",
                nameof(assets));
        }

        string[] confirmedGames = assets
            .Select(ResolveGameName)
            .Where(static game => !game.Equals(
                ClipEditorialGameContext.UnconfirmedGameName,
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string game = confirmedGames.Length == 1
            ? confirmedGames[0]
            : ClipEditorialGameContext.UnconfirmedGameName;
        string segmentLabel = assets.Count == 1 ? "segment" : "segments";
        return Bound(
            $"{game} — montage · {assets.Count} {segmentLabel}",
            MaximumLabelLength);
    }

    private static string ResolveGameName(GenerationOutputAsset asset) =>
        Normalize(
            asset.EditorialContext?.GameContext.AudienceGameName ??
            ClipEditorialGameContext.UnconfirmedGameName,
            maximumLength: 120);

    private static string? FindLiteralDetail(
        GenerationOutputAsset asset,
        GroundedEditorialBrief brief)
    {
        HashSet<string> usedClaimIds = asset.EditorialMetadata?
            .GroundingAudit?.UsedClaimIds
            .ToHashSet(StringComparer.Ordinal) ?? [];
        GroundedGameContextClaim[] eligible = brief.Claims
            .Where(static claim =>
                claim.MayShapeAudienceCopy &&
                claim.FieldAuthorizations.Contains(
                    GroundedEditorialField.Title) &&
                claim.Kind is
                    GroundedGameContextClaimKind.StableReadableText or
                    GroundedGameContextClaimKind.MissionOrChapter or
                    GroundedGameContextClaimKind.Location or
                    GroundedGameContextClaimKind.CanonicalEntity)
            .ToArray();
        GroundedGameContextClaim? selected = usedClaimIds.Count > 0
            ? eligible.FirstOrDefault(claim => usedClaimIds.Contains(claim.Id))
            : eligible.Length == 1
                ? eligible[0]
                : null;
        if (selected is null)
        {
            return null;
        }

        string value = Normalize(
            selected.Value,
            MaximumLiteralDetailLength);
        string game = ResolveGameName(asset);
        return value.Equals(game, StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    private static string DescribeMoment(
        GroundedEditorialMomentKind kind,
        string? literal,
        string timestamp) => kind switch
        {
            GroundedEditorialMomentKind.Action =>
                DescribeAt("gameplay action", literal, timestamp),
            GroundedEditorialMomentKind.Progress =>
                DescribeAt("gameplay progress", literal, timestamp),
            GroundedEditorialMomentKind.Complication =>
                DescribeAt("gameplay complication", literal, timestamp),
            GroundedEditorialMomentKind.Discovery =>
                DescribeAt("gameplay discovery", literal, timestamp),
            GroundedEditorialMomentKind.Decision =>
                DescribeAt("gameplay decision", literal, timestamp),
            GroundedEditorialMomentKind.Outcome =>
                DescribeAt("gameplay outcome", literal, timestamp),
            _ => timestamp,
        };

    private static string DescribeAt(
        string category,
        string? literal,
        string timestamp) =>
        literal is null
            ? $"{category} at {timestamp}"
            : $"{category} · {literal}";

    private static string FormatTimestamp(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    private static string Normalize(string value, int maximumLength)
    {
        string normalized = string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
        return Bound(normalized, maximumLength);
    }

    private static string Bound(string value, int maximumLength)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= maximumLength
            ? trimmed
            : trimmed[..maximumLength].TrimEnd();
    }
}

public interface ILibraryCatalogStore
{
    IReadOnlyList<LibraryMediaAsset> Current { get; }
    void Replace(IReadOnlyList<LibraryMediaAsset> assets);
}

public sealed class InMemoryLibraryCatalogStore : ILibraryCatalogStore
{
    private IReadOnlyList<LibraryMediaAsset> _current =
        Array.Empty<LibraryMediaAsset>();

    public IReadOnlyList<LibraryMediaAsset> Current => _current;

    public void Replace(IReadOnlyList<LibraryMediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _current = Array.AsReadOnly(assets.ToArray());
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
    private IReadOnlyList<LibraryMediaAsset> _assetsSnapshot;
    private Dictionary<string, int> _assetIndexes;
    private bool _disposed;

    public GenerationLibraryCatalog(
        IGenerationOutputSession session,
        ILibraryCatalogStore store)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _renderedOutputSession = session as IGenerationRenderedOutputSession;
        _assets = Validate(store.Current);
        _assetsSnapshot = Array.AsReadOnly(_assets);
        _assetIndexes = BuildAssetIndexes(_assets);
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

    public IReadOnlyList<LibraryMediaAsset> Assets => _assetsSnapshot;
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

        if (!_assetIndexes.TryGetValue(assetId, out int index))
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
        updated = Validate(updated);
        ReplaceAssets(updated);
        Changed?.Invoke(this, EventArgs.Empty);
        return rebound;
    }

    public void RemoveAsset(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (!_assetIndexes.TryGetValue(assetId, out int index))
        {
            throw new ArgumentException(
                "The Library asset is not part of the current catalog.",
                nameof(assetId));
        }

        LibraryMediaAsset[] updated = _assets
            .Where((_, assetIndex) => assetIndex != index)
            .ToArray();
        ReplaceAssets(updated);
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
        if (requested.Any(id => !_assetIndexes.ContainsKey(id)))
        {
            throw new ArgumentException(
                "Every removed Library asset must belong to the current catalog.",
                nameof(assetIds));
        }

        LibraryMediaAsset[] updated = _assets
            .Where(asset => !requested.Contains(asset.Id))
            .ToArray();
        ReplaceAssets(updated);
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
        updated = Validate(updated);
        ReplaceAssets(updated);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceAssets(LibraryMediaAsset[] updated)
    {
        _store.Replace(updated);
        _assets = updated;
        _assetsSnapshot = Array.AsReadOnly(updated);
        _assetIndexes = BuildAssetIndexes(updated);
    }

    private static Dictionary<string, int> BuildAssetIndexes(
        IReadOnlyList<LibraryMediaAsset> assets) =>
        assets
            .Select(static (asset, index) => (asset.Id, index))
            .ToDictionary(
                static value => value.Id,
                static value => value.index,
                StringComparer.Ordinal);

    private static LibraryMediaAsset[] Validate(
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
            .ThenBy(static value => value.Rank)
            .ToArray();
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
                    LibraryLabelPolicy.CreateForMontage(included),
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
                LibraryLabelPolicy.CreateForClip(asset),
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
        string libraryLabel,
        string titleSuffix,
        IEnumerable<string> sourceCandidateIds)
    {
        GenerationClipOutputProfile profile =
            GenerationClipOutputProfile.FromAsset(asset);
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
            sourceCandidateIds,
            libraryLabel,
            YouTubePublishProvenance.CaptureSequence(project.Mode == GenerationMode.Montage ? project.IncludedAssets : [asset], duration));
    }
}
