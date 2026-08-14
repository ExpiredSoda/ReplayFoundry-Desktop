using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Workspaces;
using ReplayFoundry.Desktop.Shell.Guidance;
using ReplayFoundry.Desktop.Shell.Navigation;

namespace ReplayFoundry.Desktop.Shell;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly GenerateViewModel _generateViewModel;
    private readonly PublishViewModel _publishViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly IDisposable[] _workspaceDisposables;

    private readonly IReadOnlyDictionary<ShellDestination, IWorkspaceChromeSource>
        _workspaces;

    private readonly NavigateCommand _navigateCommand;
    private readonly DelegateCommand _closeOverlayCommand;
    private readonly DelegateCommand _openGuideCommand;
    private readonly DelegateCommand _openCommandPaletteCommand;
    private readonly DelegateCommand _openShortcutReferenceCommand;
    private readonly DelegateCommand _openTeachingPromptCommand;

    private ShellDestination _currentDestination;
    private IWorkspaceChromeSource _currentWorkspace;
    private object? _activeOverlay;
    private bool _isDisposed;

    public MainWindowViewModel(
        GenerateViewModel generateViewModel,
        StudioViewModel studioViewModel,
        LibraryViewModel libraryViewModel,
        PublishViewModel publishViewModel,
        SettingsViewModel settingsViewModel)
    {
        ArgumentNullException.ThrowIfNull(
            generateViewModel);
        ArgumentNullException.ThrowIfNull(studioViewModel);
        ArgumentNullException.ThrowIfNull(libraryViewModel);
        ArgumentNullException.ThrowIfNull(publishViewModel);
        ArgumentNullException.ThrowIfNull(settingsViewModel);

        _generateViewModel =
            generateViewModel;
        _publishViewModel = publishViewModel;
        _settingsViewModel = settingsViewModel;
        _generateViewModel.StudioRequested +=
            GenerateViewModel_StudioRequested;
        _publishViewModel.PropertyChanged +=
            PublishViewModel_PropertyChanged;
        _settingsViewModel.PropertyChanged +=
            SettingsViewModel_PropertyChanged;

        _workspaceDisposables =
        [
            generateViewModel,
            .. new object[]
            {
                studioViewModel,
                libraryViewModel,
                publishViewModel,
                settingsViewModel,
            }.OfType<IDisposable>(),
        ];

        _workspaces =
            new Dictionary<ShellDestination, IWorkspaceChromeSource>
            {
                [ShellDestination.Generate] =
                    generateViewModel,
                [ShellDestination.Studio] =
                    studioViewModel,
                [ShellDestination.Library] =
                    libraryViewModel,
                [ShellDestination.Publish] =
                    publishViewModel,
                [ShellDestination.Settings] =
                    settingsViewModel,
            };

        _currentDestination =
            ShellDestination.Generate;

        _currentWorkspace =
            generateViewModel;

        _navigateCommand =
            new NavigateCommand(
                Navigate,
                CanNavigate);

        _closeOverlayCommand = new DelegateCommand(CloseOverlay);
        _openGuideCommand = new DelegateCommand(OpenGuide);
        _openCommandPaletteCommand = new DelegateCommand(OpenCommandPalette);
        _openShortcutReferenceCommand = new DelegateCommand(OpenShortcutReference);
        _openTeachingPromptCommand = new DelegateCommand(OpenTeachingPrompt);

        Guide = new FoundryGuideViewModel(_closeOverlayCommand, _openShortcutReferenceCommand);
        ShortcutReference = new ShortcutReferenceViewModel(_closeOverlayCommand);
        TeachingPrompt = new TeachingPromptViewModel(_closeOverlayCommand);
        CommandPalette = new CommandPaletteViewModel(
        [
            new("Go to Generate", "Choose a source and starting direction.", "", "generate source create", new DelegateCommand(() => Navigate(ShellDestination.Generate))),
            new("Go to Studio", "Review the editing surface and timeline guidance.", "", "studio edit timeline", new DelegateCommand(() => Navigate(ShellDestination.Studio))),
            new("Go to Library", "Review in-memory project organization.", "", "library projects filter", new DelegateCommand(() => Navigate(ShellDestination.Library))),
            new("Go to Publish", "Review publishing readiness and destinations.", "", "publish share export", new DelegateCommand(() => Navigate(ShellDestination.Publish))),
            new(
                "Go to Settings",
                "Review preferences and capability boundaries.",
                "",
                "settings preferences accessibility",
                new DelegateCommand(() => Navigate(ShellDestination.Settings))),
            new("Open Foundry Guide", "Search help without losing your place.", "F1", "help guide explain", _openGuideCommand),
            new("Open keyboard shortcuts", "Search the keyboard reference.", "Ctrl+/", "shortcut keyboard reference", _openShortcutReferenceCommand),
            new("Show a wayfinding tip", "Open the optional teaching prompt.", "", "tip learn teaching", _openTeachingPromptCommand),
        ], _closeOverlayCommand);
    }


    public ShellDestination CurrentDestination
    {
        get => _currentDestination;

        private set
        {
            if (_currentDestination == value)
            {
                return;
            }

            _currentDestination = value;
            OnPropertyChanged();
        }
    }

    public IWorkspaceChromeSource CurrentWorkspace
    {
        get => _currentWorkspace;

        private set
        {
            if (ReferenceEquals(
                    _currentWorkspace,
                    value))
            {
                return;
            }

            _currentWorkspace = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentWorkspaceChrome));
            OnPropertyChanged(nameof(CurrentWorkspaceLabel));
        }
    }

    public IWorkspaceChromeSource CurrentWorkspaceChrome => CurrentWorkspace;

    public ICommand NavigateCommand =>
        _navigateCommand;

    public string CurrentWorkspaceLabel => CurrentWorkspaceChrome.WorkspaceTitle;

    public FoundryGuideViewModel Guide { get; }

    public ShortcutReferenceViewModel ShortcutReference { get; }

    public CommandPaletteViewModel CommandPalette { get; }

    public TeachingPromptViewModel TeachingPrompt { get; }

    public object? ActiveOverlay
    {
        get => _activeOverlay;
        private set
        {
            if (ReferenceEquals(_activeOverlay, value)) return;
            _activeOverlay = value;
            OnPropertyChanged();
        }
    }

    public ICommand CloseOverlayCommand => _closeOverlayCommand;
    public ICommand OpenGuideCommand => _openGuideCommand;
    public ICommand OpenCommandPaletteCommand => _openCommandPaletteCommand;
    public ICommand OpenShortcutReferenceCommand => _openShortcutReferenceCommand;
    public ICommand OpenTeachingPromptCommand => _openTeachingPromptCommand;

    public bool IsOnlineConnectionEnabled =>
        _settingsViewModel.IsYouTubeConnectionEnabled;

    public bool IsYouTubeConnected =>
        _publishViewModel.IsConnected;

    public string ConnectivityStatusText =>
        IsYouTubeConnected
            ? "YouTube connected"
            : IsOnlineConnectionEnabled
                ? "Online enabled"
                : "Local only";

    public string ConnectivityStatusDetail =>
        IsYouTubeConnected
            ? "A YouTube channel is connected. Network actions still occur only when you choose them."
            : IsOnlineConnectionEnabled
                ? "YouTube network access is allowed, but no channel is connected."
                : "YouTube network access is disabled in Settings.";

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _generateViewModel.StudioRequested -=
            GenerateViewModel_StudioRequested;
        _publishViewModel.PropertyChanged -=
            PublishViewModel_PropertyChanged;
        _settingsViewModel.PropertyChanged -=
            SettingsViewModel_PropertyChanged;
        ActiveOverlay = null;
        foreach (IDisposable disposable in
                 _workspaceDisposables)
        {
            disposable.Dispose();
        }
    }

    private bool CanNavigate(
        ShellDestination destination)
    {
        return !_isDisposed &&
               _workspaces.ContainsKey(
                   destination);
    }

    private void Navigate(
        ShellDestination destination)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(
                nameof(MainWindowViewModel));
        }

        if (!_workspaces.TryGetValue(
                destination,
                out IWorkspaceChromeSource? workspace))
        {
            throw new InvalidOperationException(
                $"Navigation to '{destination}' is not currently available.");
        }

        CurrentDestination = destination;
        CurrentWorkspace = workspace;
    }

    private void GenerateViewModel_StudioRequested(
        object? sender,
        EventArgs eventArgs) =>
        Navigate(ShellDestination.Studio);

    private void PublishViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName) ||
            eventArgs.PropertyName == nameof(PublishViewModel.IsConnected))
        {
            RaiseConnectivityChanged();
        }
    }

    private void SettingsViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName) ||
            eventArgs.PropertyName ==
                nameof(SettingsViewModel.IsYouTubeConnectionEnabled))
        {
            RaiseConnectivityChanged();
        }
    }

    private void RaiseConnectivityChanged()
    {
        OnPropertyChanged(nameof(IsOnlineConnectionEnabled));
        OnPropertyChanged(nameof(IsYouTubeConnected));
        OnPropertyChanged(nameof(ConnectivityStatusText));
        OnPropertyChanged(nameof(ConnectivityStatusDetail));
    }

    private void CloseOverlay() => ActiveOverlay = null;

    private void OpenGuide() => ActiveOverlay = Guide;

    private void OpenCommandPalette() => ActiveOverlay = CommandPalette;

    private void OpenShortcutReference() => ActiveOverlay = ShortcutReference;

    private void OpenTeachingPrompt() => ActiveOverlay = TeachingPrompt;

}
