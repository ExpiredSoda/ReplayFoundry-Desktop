using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;


public sealed class StudioMomentSuggestionsViewModel : ObservableObject
{
    private readonly Action<StudioMomentSuggestion> _preview;
    private readonly Action<StudioMomentSuggestion> _use;
    private readonly DelegateCommand _previous, _next, _useCommand;
    private StudioMomentSuggestion[] _all = [];
    private StudioMomentSuggestion? _selected;
    public StudioMomentSuggestionsViewModel(Action<StudioMomentSuggestion> preview, Action<StudioMomentSuggestion> use)
    {
        _preview = preview;
        _use = use;
        Filters = new(Refresh);
        SelectCommand = new DelegateCommand<StudioMomentSuggestion>(Select);
        _previous = new(() => Navigate(-1), () => Items.Count > 0 && (_selected is null || Index > 0));
        _next = new(() => Navigate(1), () => Items.Count > 0 && (_selected is null || Index < Items.Count - 1));
        _useCommand = new(() => { if (_selected is not null) _use(_selected); }, () => _selected is not null);
    }
    public StudioMomentFilters Filters { get; }
    public IReadOnlyList<StudioMomentSuggestion> Items { get; private set; } = [];
    public StudioMomentSuggestion? Selected => _selected;
    public ICommand SelectCommand { get; }
    public ICommand PreviousCommand => _previous;
    public ICommand NextCommand => _next;
    public ICommand UseCommand => _useCommand;
    public string Summary => Items.Count == 0 ? _all.Length == 0 ? "No saved suggestions for this recording. You can still choose any range."
        : "No matches. Try another type or All moments." : $"{Items.Count} of {_all.Length} moments · click a cyan mark to preview";
    public string SelectionText => _selected is null ? "Explore a suggested moment, then use its range when you’re ready."
        : $"{_selected.TimeText} · {_selected.Label}";
    public string Explanation => _selected?.Explanation ?? "These suggestions come from your video scan. Overlapping moments share a marker; use Next to explore each one.";
    private int Index => _selected is null ? -1 : Items.ToList().FindIndex(item => item.Id == _selected.Id);

    public void Bind(GenerationOutputProject? project, string? path)
    {
        _all = StudioMomentSuggestionCatalog.Create(project, path);
        Refresh();
    }
    public void Select(StudioMomentSuggestion item)
    {
        if (!Items.Contains(item)) return;
        _selected = item;
        Notify();
        _preview(item);
    }
    private void Navigate(int direction)
    {
        if (Items.Count == 0) return;
        Select(Items[Math.Clamp(_selected is null ? 0 : Index + direction, 0, Items.Count - 1)]);
    }
    private void Refresh()
    {
        Items = _all.Where(item => Filters.Matches(item.Content)).ToArray();
        _selected = Items.FirstOrDefault(item => item.Id == _selected?.Id);
        Notify();
    }
    private void Notify()
    {
        foreach (string name in new[] { nameof(Items), nameof(Selected), nameof(Summary), nameof(SelectionText), nameof(Explanation) }) OnPropertyChanged(name);
        _previous.RaiseCanExecuteChanged(); _next.RaiseCanExecuteChanged(); _useCommand.RaiseCanExecuteChanged();
    }
}
