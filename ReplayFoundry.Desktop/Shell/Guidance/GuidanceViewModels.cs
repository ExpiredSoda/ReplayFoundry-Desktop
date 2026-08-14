using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Shell.Guidance;

public sealed record GuideEntry(string Id, string Title, string Summary, string Body, string Keywords);

public sealed record ShortcutEntry(string Command, string Gesture, string Scope, string Keywords);

public sealed record CommandPaletteEntry(string Label, string Description, string Gesture, string Keywords, ICommand Command);

public sealed class FoundryGuideViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<GuideEntry> _entries;
    private readonly ICommand _closeCommand;
    private string _searchText = string.Empty;
    private GuideEntry? _selectedEntry;

    public FoundryGuideViewModel(ICommand closeCommand, ICommand openShortcutsCommand)
    {
        _closeCommand = closeCommand ?? throw new ArgumentNullException(nameof(closeCommand));
        OpenShortcutsCommand = openShortcutsCommand ?? throw new ArgumentNullException(nameof(openShortcutsCommand));
        _entries =
        [
            new(
                "start",
                "Start with Generate",
                "Bring in a local source and choose a clip direction.",
                "Choose Generate from the bottom dock, add a local video, and select Individual clips or Montage. " +
                "The current surface is presentation-only until the connected providers are available.",
                "generate source local video clips montage first project"),
            new(
                "workspaces",
                "Move through the five workspaces",
                "Generate, Studio, Library, Publish, and Settings keep one stable shell.",
                "Use the dock or Ctrl+K to move between workspaces. Your in-memory selections stay attached to their workspace while you look around.",
                "navigation dock workspace generate studio library publish settings"),
            new(
                "guidance",
                "Ask Foundry Guide",
                "Use F1 or the Help button whenever the next step is unclear.",
                "Guide pages explain the purpose of each workspace, the current availability boundary, and the keyboard paths that avoid hunting through the surface.",
                "help guide f1 explain"),
            new(
                "accessibility",
                "Make the surface easier to read",
                "Keyboard access, focus, high contrast, text scale, and reduced motion are first-class paths.",
                "Every core action has a visible focus treatment and a keyboard route. Use Windows display and " +
                "accessibility settings for text scale and contrast; the shell reflows rather than hiding essential labels.",
                "accessibility keyboard focus high contrast text scale magnifier reduced motion"),
            new(
                "issues",
                "When something cannot continue",
                "Look for a short explanation and stable reference code.",
                "Replay Foundry names the blocked area, gives the next useful action when one is known, and keeps " +
                "technical detail collapsed. Codes such as RF-LIB-001 are stable enough to share with a teammate.",
                "error issue problem blocked reference code details support"),
        ];
        FilteredEntries = new ObservableCollection<GuideEntry>(_entries);
        SelectedEntry = _entries[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GuideEntry> FilteredEntries { get; }

    public GuideEntry? SelectedEntry
    {
        get => _selectedEntry;
        set { if (ReferenceEquals(_selectedEntry, value)) return; _selectedEntry = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (_searchText == value) return; _searchText = value; Refresh(); OnPropertyChanged(); }
    }

    public ICommand CloseCommand => _closeCommand;
    public ICommand OpenShortcutsCommand { get; }

    private void Refresh()
    {
        FilteredEntries.Clear();
        foreach (GuideEntry entry in _entries.Where(Matches)) FilteredEntries.Add(entry);
        if (SelectedEntry is null || !FilteredEntries.Contains(SelectedEntry)) SelectedEntry = FilteredEntries.FirstOrDefault();
        OnPropertyChanged(nameof(FilteredEntries));
    }

    private bool Matches(GuideEntry entry) => string.IsNullOrWhiteSpace(SearchText) ||
        string.Join(' ', entry.Title, entry.Summary, entry.Body, entry.Keywords).Contains(SearchText, StringComparison.OrdinalIgnoreCase);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ShortcutReferenceViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<ShortcutEntry> _entries;
    private string _searchText = string.Empty;

    public ShortcutReferenceViewModel(ICommand closeCommand)
    {
        CloseCommand = closeCommand ?? throw new ArgumentNullException(nameof(closeCommand));
        _entries =
        [
            new("Open Foundry Guide", "F1", "Every workspace", "help guide"),
            new("Open command palette", "Ctrl+K", "Every workspace", "commands navigation search palette"),
            new("Open shortcut reference", "Ctrl+/", "Every workspace", "shortcuts keyboard reference"),
            new("Move between workspaces", "Ctrl+K, then type a workspace", "Shell", "generate studio library publish settings navigation"),
            new("Dismiss the current overlay", "Esc", "Guidance surfaces", "close dismiss overlay"),
            new("Move through focused controls", "Tab / Shift+Tab", "Every workspace", "keyboard focus accessibility"),
        ];
        FilteredEntries = new ObservableCollection<ShortcutEntry>(_entries);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ShortcutEntry> FilteredEntries { get; }
    public ICommand CloseCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            FilteredEntries.Clear();
            foreach (ShortcutEntry entry in _entries.Where(Matches)) FilteredEntries.Add(entry);
            OnPropertyChanged();
        }
    }

    private bool Matches(ShortcutEntry entry) =>
        string.Join(' ', entry.Command, entry.Gesture, entry.Scope, entry.Keywords)
            .Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class CommandPaletteViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<CommandPaletteEntry> _entries;
    private readonly ICommand _closeCommand;
    private string _searchText = string.Empty;
    private CommandPaletteEntry? _selectedEntry;

    public CommandPaletteViewModel(IReadOnlyList<CommandPaletteEntry> entries, ICommand closeCommand)
    {
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _closeCommand = closeCommand ?? throw new ArgumentNullException(nameof(closeCommand));
        FilteredEntries = new ObservableCollection<CommandPaletteEntry>(_entries);
        SelectedEntry = FilteredEntries.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<CommandPaletteEntry> FilteredEntries { get; }
    public ICommand CloseCommand => _closeCommand;
    public ICommand ExecuteSelectedCommand => new DelegateCommand(ExecuteSelected, () => SelectedEntry?.Command.CanExecute(null) == true);

    public CommandPaletteEntry? SelectedEntry
    {
        get => _selectedEntry;
        set { if (ReferenceEquals(_selectedEntry, value)) return; _selectedEntry = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExecuteSelectedCommand)); }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (_searchText == value) return; _searchText = value; Refresh(); OnPropertyChanged(); }
    }

    private void ExecuteSelected() => SelectedEntry?.Command.Execute(null);

    private void Refresh()
    {
        FilteredEntries.Clear();
        foreach (CommandPaletteEntry entry in _entries.Where(Matches)) FilteredEntries.Add(entry);
        SelectedEntry = FilteredEntries.FirstOrDefault();
    }

    private bool Matches(CommandPaletteEntry entry) =>
        string.Join(' ', entry.Label, entry.Description, entry.Gesture, entry.Keywords)
            .Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class TeachingPromptViewModel
{
    public TeachingPromptViewModel(ICommand closeCommand)
    {
        CloseCommand = closeCommand ?? throw new ArgumentNullException(nameof(closeCommand));
    }

    public string Title => "A small wayfinding tip";
    public string Body => "Press Ctrl+K to jump to a workspace or open a guide page without leaving your current context.";
    public ICommand CloseCommand { get; }
}
