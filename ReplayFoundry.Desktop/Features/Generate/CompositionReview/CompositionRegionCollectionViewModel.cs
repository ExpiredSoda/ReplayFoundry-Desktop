using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public sealed class CompositionRegionCollectionViewModel :
    INotifyPropertyChanged
{
    private readonly ObservableCollection<CompositionRegionDraftViewModel>
        _regions = [];
    private readonly Action _changed;
    private CompositionRegionDraftViewModel? _selectedRegion;
    private int _previewWidth = 1280;
    private int _previewHeight = 720;

    public CompositionRegionCollectionViewModel(Action changed)
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Regions = new ReadOnlyObservableCollection<CompositionRegionDraftViewModel>(
            _regions);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<CompositionRegionDraftViewModel> Regions
    {
        get;
    }

    public CompositionRegionDraftViewModel? SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (ReferenceEquals(_selectedRegion, value))
            {
                return;
            }

            if (value is not null && !_regions.Contains(value))
            {
                throw new ArgumentException(
                    "The selected region must belong to this source.",
                    nameof(value));
            }

            if (_selectedRegion is not null)
            {
                _selectedRegion.IsSelected = false;
            }

            _selectedRegion = value;
            if (_selectedRegion is not null)
            {
                _selectedRegion.IsSelected = true;
            }

            Notify(
                nameof(SelectedRegion),
                nameof(HasSelectedRegion),
                nameof(CanRemoveSelectedRegion));
        }
    }

    public bool HasSelectedRegion => SelectedRegion is not null;

    public bool CanRemoveSelectedRegion => SelectedRegion is not null;

    public bool HasGameplayRegion => Regions.Any(
        static region => region.Role == CompositionRegionRole.Gameplay);

    internal void InitializeFullFrameGameplay() =>
        AddDraft(
            "gameplay",
            NormalizedRectangle.FullFrame,
            CompositionRegionRole.Gameplay,
            CompositionRegionTraits.Dynamic,
            select: true);

    internal void Restore(IEnumerable<CompositionRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        foreach (CompositionRegion region in regions)
        {
            AddDraft(
                region.Id,
                region.Geometry,
                region.Role,
                region.Traits,
                select: _regions.Count == 0);
        }
    }

    public CompositionRegionDraftViewModel AddRegion(
        CompositionRegionRole role)
    {
        ValidateRole(role);
        (NormalizedRectangle Geometry, CompositionRegionTraits Traits) defaults =
            GetDefaults(role);
        CompositionRegionDraftViewModel region = AddDraft(
            CreateUniqueId(role),
            defaults.Geometry,
            role,
            defaults.Traits,
            select: true);
        RaiseCollectionChanged();
        _changed();
        return region;
    }

    public void UseFullFrameGameplay()
    {
        _regions.Clear();
        InitializeFullFrameGameplay();
        RaiseCollectionChanged();
        _changed();
    }

    public void RemoveSelectedRegion()
    {
        if (SelectedRegion is null)
        {
            throw new InvalidOperationException(
                "No composition region is selected.");
        }

        int removedIndex = _regions.IndexOf(SelectedRegion);
        _regions.Remove(SelectedRegion);
        SelectedRegion = _regions.Count == 0
            ? null
            : _regions[Math.Min(removedIndex, _regions.Count - 1)];
        RaiseCollectionChanged();
        _changed();
    }

    internal void ApplyCopied(
        IEnumerable<CompositionRegionDraftViewModel> sourceRegions)
    {
        ArgumentNullException.ThrowIfNull(sourceRegions);
        CompositionRegionDraftViewModel[] snapshot = sourceRegions.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A copied layout requires at least one region.",
                nameof(sourceRegions));
        }

        _regions.Clear();
        foreach (CompositionRegionDraftViewModel region in snapshot)
        {
            AddDraft(
                region.Id,
                new NormalizedRectangle(
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height),
                region.Role,
                region.Traits,
                select: _regions.Count == 0);
        }
        RaiseCollectionChanged();
    }

    internal CompositionRegion[] CreateRegions()
    {
        if (_regions.Count == 0)
        {
            throw new InvalidOperationException(
                "Add at least one composition region before confirming this source.");
        }

        string? duplicateId = _regions
            .GroupBy(
                static region => region.Id,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;
        if (duplicateId is not null)
        {
            throw new InvalidOperationException(
                $"Region identifier '{duplicateId}' is duplicated.");
        }

        if (!HasGameplayRegion)
        {
            throw new InvalidOperationException(
                "Add at least one Gameplay region before confirming this source.");
        }

        return _regions
            .Select(static region => region.CreateRegion())
            .ToArray();
    }

    internal void SetPreviewDimensions(int width, int height)
    {
        _previewWidth = width;
        _previewHeight = height;
        foreach (CompositionRegionDraftViewModel region in _regions)
        {
            region.SetPreviewDimensions(width, height);
        }
    }

    private CompositionRegionDraftViewModel AddDraft(
        string id,
        NormalizedRectangle geometry,
        CompositionRegionRole role,
        CompositionRegionTraits traits,
        bool select)
    {
        if (_regions.Any(
                region => string.Equals(
                    region.Id,
                    id,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"Region identifier '{id}' is duplicated.",
                nameof(id));
        }

        var region = new CompositionRegionDraftViewModel(
            id,
            geometry,
            role,
            traits,
            OnDraftChanged,
            OnSelectionRequested,
            OnRemovalRequested);
        region.SetPreviewDimensions(_previewWidth, _previewHeight);
        _regions.Add(region);
        if (select)
        {
            SelectedRegion = region;
        }
        return region;
    }

    private void OnDraftChanged(CompositionRegionDraftViewModel region)
    {
        if (!_regions.Contains(region))
        {
            throw new InvalidOperationException(
                "The edited region no longer belongs to this source.");
        }

        OnPropertyChanged(nameof(HasGameplayRegion));
        _changed();
    }

    private void OnSelectionRequested(CompositionRegionDraftViewModel region) =>
        SelectedRegion = region;

    private void OnRemovalRequested(CompositionRegionDraftViewModel region)
    {
        SelectedRegion = region;
        RemoveSelectedRegion();
    }

    private void RaiseCollectionChanged() => Notify(
        nameof(Regions),
        nameof(HasGameplayRegion),
        nameof(CanRemoveSelectedRegion));

    private string CreateUniqueId(CompositionRegionRole role)
    {
        string stem = role switch
        {
            CompositionRegionRole.Gameplay => "gameplay",
            CompositionRegionRole.Presenter => "presenter",
            CompositionRegionRole.ChatOrText => "chat-text",
            CompositionRegionRole.Overlay => "overlay",
            CompositionRegionRole.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

        if (_regions.All(
                region => !string.Equals(
                    region.Id,
                    stem,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return stem;
        }

        int suffix = 2;
        while (_regions.Any(
                   region => string.Equals(
                       region.Id,
                       $"{stem}-{suffix}",
                       StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
        }
        return $"{stem}-{suffix}";
    }

    private static (
        NormalizedRectangle Geometry,
        CompositionRegionTraits Traits) GetDefaults(
        CompositionRegionRole role) => role switch
        {
            CompositionRegionRole.Gameplay =>
                (new NormalizedRectangle(0.05, 0.05, 0.68, 0.72),
                    CompositionRegionRoleDefaults.GetTraits(role)),
            CompositionRegionRole.Presenter =>
                (new NormalizedRectangle(0.72, 0.05, 0.24, 0.32),
                    CompositionRegionRoleDefaults.GetTraits(role)),
            CompositionRegionRole.ChatOrText =>
                (new NormalizedRectangle(0.03, 0.58, 0.28, 0.36),
                    CompositionRegionRoleDefaults.GetTraits(role)),
            CompositionRegionRole.Overlay =>
                (new NormalizedRectangle(0.64, 0.68, 0.31, 0.25),
                    CompositionRegionRoleDefaults.GetTraits(role)),
            CompositionRegionRole.Unknown =>
                (new NormalizedRectangle(0.35, 0.35, 0.3, 0.3),
                    CompositionRegionRoleDefaults.GetTraits(role)),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    private static void ValidateRole(CompositionRegionRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }
    }

    private void Notify(params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
