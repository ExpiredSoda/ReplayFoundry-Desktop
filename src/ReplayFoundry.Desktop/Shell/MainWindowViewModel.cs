using System;
using System.Collections.Generic;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
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

public enum ShellServiceState
{
    Ready,
    Degraded,
    Offline,
}

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly GenerateViewModel _generateViewModel;
    private readonly IGameKnowledgePermissionStatus?
        _gameKnowledgePermissionStatus;
    private readonly GenerationRuntimeCapabilities?
        _localAiCapabilities;
    private readonly IDisposable[] _workspaceDisposables;
    private readonly IApplicationStopParticipant[] _workspaceStopParticipants;

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
        SettingsViewModel settingsViewModel,
        IGameKnowledgePermissionStatus? gameKnowledgePermissionStatus = null,
        GenerationRuntimeCapabilities? localAiCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(
            generateViewModel);
        ArgumentNullException.ThrowIfNull(studioViewModel);
        ArgumentNullException.ThrowIfNull(libraryViewModel);
        ArgumentNullException.ThrowIfNull(publishViewModel);
        ArgumentNullException.ThrowIfNull(settingsViewModel);

        _generateViewModel =
            generateViewModel;
        _gameKnowledgePermissionStatus = gameKnowledgePermissionStatus;
        _localAiCapabilities = localAiCapabilities;
        _generateViewModel.StudioRequested +=
            GenerateViewModel_StudioRequested;
        if (_gameKnowledgePermissionStatus is not null)
        {
            _gameKnowledgePermissionStatus.Changed +=
                GameKnowledgePermissionStatus_Changed;
        }

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
        _workspaceStopParticipants = _workspaceDisposables
            .OfType<IApplicationStopParticipant>()
            .ToArray();

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
            new("Go to Studio", "Edit clips and adjust their timing.", "", "studio edit timeline", new DelegateCommand(() => Navigate(ShellDestination.Studio))),
            new("Go to Library", "Browse and organize finished videos.", "", "library projects filter", new DelegateCommand(() => Navigate(ShellDestination.Library))),
            new("Go to Publish", "Review publishing readiness and destinations.", "", "publish share export", new DelegateCommand(() => Navigate(ShellDestination.Publish))),
            new(
                "Go to Settings",
                "Review preferences and available tools.",
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

    public ShellServiceState LocalAiServiceState =>
        _localAiCapabilities is null
            ? ShellServiceState.Degraded
            : HasUsableAiTooling
                ? ShellServiceState.Ready
                : ShellServiceState.Offline;

    public string LocalAiStatusLabel => LocalAiServiceState switch
    {
        ShellServiceState.Ready => "AI and local tools",
        ShellServiceState.Degraded => "Tool status unavailable",
        ShellServiceState.Offline => "Local tools only",
        _ => throw new ArgumentOutOfRangeException(),
    };

    public string LocalAiStatusMarker => LocalAiServiceState ==
        ShellServiceState.Ready
            ? "✓"
            : LocalAiServiceState == ShellServiceState.Degraded
                ? "!"
                : "L";

    public string LocalAiStatusIcon => LocalAiServiceState ==
        ShellServiceState.Ready
            ? "Icon.Spark"
            : "Icon.Local";

    public string LocalAiAccessibleName =>
        $"Installed tools: {LocalAiStatusLabel}";

    public string LocalAiAccessibleStatus => LocalAiServiceState switch
    {
        ShellServiceState.Ready => $"{UsableAiTier} ready",
        ShellServiceState.Degraded => "AI availability could not be checked",
        ShellServiceState.Offline => "Local tools ready; AI tools unavailable",
        _ => throw new ArgumentOutOfRangeException(),
    };

    public string LocalAiStatusDetail => BuildLocalAiStatusDetail();

    public ShellServiceState WikidataServiceState =>
        _gameKnowledgePermissionStatus is null
            ? ShellServiceState.Degraded
            : _gameKnowledgePermissionStatus
                .HasRememberedWikimediaPermission
                    ? ShellServiceState.Ready
                    : ShellServiceState.Offline;

    public string WikidataStatusLabel => WikidataServiceState switch
    {
        ShellServiceState.Ready => "Available",
        ShellServiceState.Degraded => "Status unavailable",
        ShellServiceState.Offline => "Offline",
        _ => throw new ArgumentOutOfRangeException(),
    };

    public string WikidataStatusMarker => MarkerFor(WikidataServiceState);

    public string WikidataAccessibleName =>
        $"Wikidata status: {WikidataStatusLabel}";

    public string WikidataStatusDetail => WikidataServiceState switch
    {
        ShellServiceState.Ready =>
            "Wikidata game lookup is available when you choose it. Only the confirmed game name is sent; clips and transcripts stay on this PC.",
        ShellServiceState.Degraded =>
            "Wikidata status is unavailable. Replay Foundry will not assume public game lookup is available.",
        ShellServiceState.Offline =>
            "Wikidata game lookup is offline. You can enable public game lookup while choosing a game.",
        _ => throw new ArgumentOutOfRangeException(),
    };

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _generateViewModel.StudioRequested -=
            GenerateViewModel_StudioRequested;
        if (_gameKnowledgePermissionStatus is not null)
        {
            _gameKnowledgePermissionStatus.Changed -=
                GameKnowledgePermissionStatus_Changed;
        }
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

    internal Task StopAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(
            _workspaceStopParticipants.Select(
                participant => participant.StopAsync(cancellationToken)));

    private void RaiseWikidataStatusChanged()
    {
        OnPropertyChanged(nameof(WikidataServiceState));
        OnPropertyChanged(nameof(WikidataStatusLabel));
        OnPropertyChanged(nameof(WikidataStatusMarker));
        OnPropertyChanged(nameof(WikidataAccessibleName));
        OnPropertyChanged(nameof(WikidataStatusDetail));
    }

    private void GameKnowledgePermissionStatus_Changed(
        object? sender,
        EventArgs eventArgs) =>
        RaiseWikidataStatusChanged();

    private static string MarkerFor(ShellServiceState state) => state switch
    {
        ShellServiceState.Ready => "✓",
        ShellServiceState.Degraded => "!",
        ShellServiceState.Offline => "╱",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private bool HasUsableAiTooling =>
        _localAiCapabilities is not null &&
        (_localAiCapabilities.IsCaptionTranscriptionAvailable ||
         _localAiCapabilities.IsSpeechActivityAvailable ||
         _localAiCapabilities.IsVisualSemanticReviewAvailable ||
         _localAiCapabilities.IsEditorialAiAvailable);

    private bool HasUsableAdvancedAiTooling =>
        _localAiCapabilities is not null &&
        (_localAiCapabilities.IsVisualSemanticReviewAvailable ||
         _localAiCapabilities.IsEditorialAiAvailable);

    private string BuildLocalAiStatusDetail()
    {
        if (_localAiCapabilities is null)
        {
            return "Local tools are ready. AI availability could not be checked.";
        }

        if (!HasUsableAiTooling)
        {
            return "Local tools are ready. AI tools are not available.";
        }

        var uses = new List<string>(4);
        if (_localAiCapabilities.IsVisualSemanticReviewAvailable)
        {
            uses.Add("picture review");
        }

        if (_localAiCapabilities.IsEditorialAiAvailable)
        {
            uses.Add("title writing");
        }

        if (_localAiCapabilities.IsCaptionTranscriptionAvailable)
        {
            uses.Add("captions");
        }

        if (_localAiCapabilities.IsSpeechActivityAvailable)
        {
            uses.Add("speech timing");
        }

        return $"{UsableAiTier} is ready for {JoinPlainLanguage(uses)}. " +
               "Local tools are also ready.";
    }

    private string UsableAiTier => HasUsableAdvancedAiTooling
        ? "Advanced AI"
        : "Local AI";

    private static string JoinPlainLanguage(IReadOnlyList<string> values) =>
        values.Count switch
        {
            0 => "AI-assisted work",
            1 => values[0],
            2 => $"{values[0]} and {values[1]}",
            _ => string.Join(", ", values.Take(values.Count - 1)) +
                 $", and {values[^1]}",
        };

    private void CloseOverlay() => ActiveOverlay = null;

    private void OpenGuide() => ActiveOverlay = Guide;

    private void OpenCommandPalette() => ActiveOverlay = CommandPalette;

    private void OpenShortcutReference() => ActiveOverlay = ShortcutReference;

    private void OpenTeachingPrompt() => ActiveOverlay = TeachingPrompt;

}
