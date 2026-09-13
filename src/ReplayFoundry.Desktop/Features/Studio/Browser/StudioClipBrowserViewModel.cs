using System.ComponentModel;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Browser;

public sealed record StudioClipOrder(string Label, bool Chronological);

/// <summary>Search and order are view state; they never change the cut, selection, or render queue.</summary>
public sealed class StudioClipBrowserViewModel : ObservableObject, IDisposable
{
    private readonly INotifyPropertyChanged _owner;
    private readonly Func<GenerationOutputProject?> _project;
    private readonly Func<IReadOnlyList<StudioBrowserPreviewItem>> _cards;
    private string _query = "";
    private string? _projectId;
    private StudioClipOrder _order = Orders[0];
    private IReadOnlyList<StudioBrowserPreviewItem> _items = [];
    private int _total;
    private GenerationOutputProject? _indexedProject;
    private Dictionary<string, string> _searchText = new(StringComparer.Ordinal);

    internal StudioClipBrowserViewModel(INotifyPropertyChanged owner, Func<GenerationOutputProject?> project,
        Func<IReadOnlyList<StudioBrowserPreviewItem>> cards)
    {
        _owner = owner; _project = project; _cards = cards;
        ClearCommand = new DelegateCommand(() => Query = "");
        owner.PropertyChanged += OwnerChanged;
        Refresh();
    }

    public static IReadOnlyList<StudioClipOrder> Orders { get; } = [new("Ranked", false), new("Recording order", true)];
    public IReadOnlyList<StudioClipOrder> OrderOptions => Orders;
    public string Query
    {
        get => _query;
        set { if (_query == value) return; _query = value ?? ""; OnPropertyChanged(); Refresh(); }
    }
    public StudioClipOrder SelectedOrder
    {
        get => _order;
        set { if (value is null || !Orders.Contains(value) || _order == value) return; _order = value; OnPropertyChanged(); Refresh(); }
    }
    public IReadOnlyList<StudioBrowserPreviewItem> Items => _items;
    public ICommand ClearCommand { get; }
    public bool HasQuery => !string.IsNullOrWhiteSpace(_query);
    public string Summary => _total == 0 ? "" : HasQuery
        ? $"{_items.Count} of {_total} clips match. Searches saved titles, descriptions, and captions."
        : $"{_total} clips · {_order.Label.ToLowerInvariant()}";
    public bool HasNoMatches => _total > 0 && _items.Count == 0;

    private void OwnerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(StudioViewModel.BrowserPreviewItems)) Refresh();
    }

    private void Refresh()
    {
        var project = _project();
        if (_projectId != project?.Id)
        {
            _projectId = project?.Id; _query = ""; _order = Orders[0];
            OnPropertyChanged(nameof(Query)); OnPropertyChanged(nameof(SelectedOrder));
        }
        var cards = _cards();
        _total = project?.Assets.Count ?? 0;
        string[] terms = _query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (project is null) _items = cards;
        else
        {
            var assets = project.Assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
            if (!ReferenceEquals(_indexedProject, project))
            {
                _indexedProject = project;
                _searchText = project.Assets.ToDictionary(asset => asset.Id, SearchText, StringComparer.Ordinal);
            }
            var sourceOrder = project.SourceMedia.Select((source, index) => (source.FullPath, index))
                .ToDictionary(source => source.FullPath, source => source.index, StringComparer.OrdinalIgnoreCase);
            var matches = cards.Where(card => card.AssetId is { } id && assets.TryGetValue(id, out var asset) &&
                terms.All(term => _searchText[id].Contains(term, StringComparison.OrdinalIgnoreCase)));
            _items = (_order.Chronological
                ? matches.OrderBy(card => sourceOrder[assets[card.AssetId!].SourceFullPath])
                    .ThenBy(card => assets[card.AssetId!].SourceStart).ThenBy(card => assets[card.AssetId!].Rank)
                : matches.OrderBy(card => assets[card.AssetId!].Rank)).ToArray();
        }
        foreach (string name in new[] { nameof(Items), nameof(HasQuery), nameof(Summary), nameof(HasNoMatches) }) OnPropertyChanged(name);
    }

    private static string SearchText(GenerationOutputAsset asset) => string.Join(" ",
        asset.DisplayName, asset.EditorialMetadata?.Title, asset.EditorialMetadata?.Description,
        System.IO.Path.GetFileName(asset.SourceFullPath), asset.Captions is { } track ? string.Join(" ",
            StudioCaptionCutProjection.Project(track, asset.SourceStart, asset.SourceEnd).Track.Segments.Select(segment => segment.Text)) : "");

    public void Dispose() => _owner.PropertyChanged -= OwnerChanged;
}
