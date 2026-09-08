using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.GameKnowledge;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.GameContext;

public sealed class GameContextSourceViewModel : INotifyPropertyChanged
{
    private readonly Action _changed;
    private string _gameName;
    private string _contextNotes;
    private GenerationGameContextOrigin _origin;
    private bool _useOpenGameKnowledge;
    private ConfirmedGameIdentity? _confirmedIdentity;
    private readonly ObservableCollection<GameIdentityCandidateViewModel>
        _candidates = [];
    private GameIdentityCandidateViewModel? _selectedCandidate;
    private bool _isReplacingCandidates;
    private readonly IGameIdentityCandidateProvider? _candidateProvider;
    private readonly IGenerationGameKnowledgeService? _gameKnowledge;
    private string _candidateStatus = string.Empty;
    private readonly DelegateCommand _confirmGameCommand;
    private readonly AsyncDelegateCommand _discoverCandidatesCommand;
    private readonly AsyncDelegateCommand _refreshConfirmedContextCommand;
    private readonly DelegateCommand _chooseNoneCommand;

    internal GameContextSourceViewModel(
        GenerationSourceGameContext context,
        Action changed,
        IGameIdentityCandidateProvider? candidateProvider = null,
        IGenerationGameKnowledgeService? gameKnowledge = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        SourceFullPath = context.SourceFullPath;
        SourceName = Path.GetFileName(context.SourceFullPath);
        _gameName = context.GameName;
        _contextNotes = context.ContextNotes ?? string.Empty;
        _origin = context.Origin;
        _confirmedIdentity = context.ConfirmedIdentity;
        _candidateProvider = candidateProvider;
        _gameKnowledge = gameKnowledge;
        _useOpenGameKnowledge = context.UseOpenGameKnowledge &&
            IsConfirmed && _confirmedIdentity is not null &&
            IsPublicLookupAvailable;
        _confirmGameCommand = new DelegateCommand(
            ConfirmGame,
            () => CanConfirmGame);
        _discoverCandidatesCommand = new AsyncDelegateCommand(
            DiscoverCandidatesAsync,
            () => CanDiscoverCandidates);
        _refreshConfirmedContextCommand = new AsyncDelegateCommand(
            RefreshConfirmedContextAsync,
            () => CanRefreshConfirmedContext);
        _chooseNoneCommand = new DelegateCommand(
            ChooseNone,
            () => IsConfirmed &&
                (_useOpenGameKnowledge || HasCandidateChoices ||
                 _confirmedIdentity is not null));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourceFullPath { get; }

    public string SourceName { get; }

    public string GameName
    {
        get => _gameName;
        set
        {
            string normalized = value ?? string.Empty;
            if (_gameName == normalized)
            {
                return;
            }
            _gameName = normalized;
            _origin = GenerationGameContextOrigin.SourcePathHint;
            _confirmedIdentity = null;
            _useOpenGameKnowledge = false;
            _candidates.Clear();
            _selectedCandidate = null;
            _candidateStatus = BuildUnconfirmedStatus(normalized);
            Changed();
        }
    }

    public string ContextNotes
    {
        get => _contextNotes;
        set
        {
            string normalized = value ?? string.Empty;
            if (_contextNotes == normalized)
            {
                return;
            }
            _contextNotes = normalized;
            Changed();
        }
    }

    public string OriginText => _origin switch
    {
        GenerationGameContextOrigin.UserConfirmed => "Confirmed by you",
        GenerationGameContextOrigin.ReusedUserMemory => "Remembered on this PC",
        GenerationGameContextOrigin.RememberedSuggestion => "Remembered from this folder · confirm for this recording",
        GenerationGameContextOrigin.SourcePathHint => HasMeaningfulGameName
            ? "Suggested from the folder name · not used until confirmed"
            : "No confirmed game",
        _ => throw new InvalidOperationException("Unknown game-context origin."),
    };

    public string DisplayGameName => IsConfirmed
        ? GameName
        : HasMeaningfulGameName
            ? $"{GameName} (unconfirmed suggestion)"
            : "No game selected";

    private bool HasMeaningfulGameName =>
        GenerationGamePathHintPolicy.IsMeaningfulSuggestion(GameName);

    public bool IsConfirmed => _origin is
        GenerationGameContextOrigin.UserConfirmed or
        GenerationGameContextOrigin.ReusedUserMemory;

    public bool CanConfirmGame =>
        !IsConfirmed &&
        HasValidFields &&
        HasMeaningfulGameName;

    public ICommand ConfirmGameCommand => _confirmGameCommand;

    public ICommand DiscoverCandidatesCommand => _discoverCandidatesCommand;

    public ICommand RefreshConfirmedContextCommand =>
        _refreshConfirmedContextCommand;

    public ICommand ChooseNoneCommand => _chooseNoneCommand;

    public IReadOnlyList<GameIdentityCandidateViewModel> Candidates =>
        _candidates;

    public GameIdentityCandidateViewModel? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (value is not null && !_candidates.Contains(value))
            {
                throw new ArgumentException(
                    "The selected game identity is not available in this step.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedCandidate, value))
            {
                return;
            }
            _selectedCandidate = value;
            OnPropertyChanged();
            if (value is not null)
            {
                SelectCandidate(value.Candidate);
                return;
            }
            if (!_isReplacingCandidates && _confirmedIdentity is not null)
            {
                _confirmedIdentity = null;
                _candidateStatus =
                    "Selection cleared. Choose the matching game below, or continue without public information.";
                Changed();
            }
        }
    }

    public bool HasCandidateChoices => _candidates.Count > 0;

    public string CandidateStatus => _candidateStatus;

    public ConfirmedGameIdentity? ConfirmedIdentity => _confirmedIdentity;

    public bool HasConfirmedIdentity => _confirmedIdentity is not null;

    public bool IsPublicLookupAvailable => _candidateProvider is not null;

    public bool CanUsePublicLookup => IsConfirmed && IsPublicLookupAvailable;

    public string PublicLookupStatusText => IsPublicLookupAvailable
        ? "Available"
        : "Unavailable";

    public string PublicLookupStatusDetail => IsPublicLookupAvailable
        ? "Public game lookup is available when you choose to use it. Only the confirmed game name is sent."
        : "Public game lookup is not available in this setup. Confirmed game names and your own notes still work.";

    public bool CanDiscoverCandidates =>
        _candidateProvider is not null &&
        IsConfirmed &&
        _useOpenGameKnowledge &&
        HasValidFields &&
        HasMeaningfulGameName;

    public bool CanRefreshConfirmedContext =>
        _gameKnowledge is not null &&
        IsConfirmed &&
        _useOpenGameKnowledge &&
        _confirmedIdentity is not null;

    public bool UseOpenGameKnowledge
    {
        get => _useOpenGameKnowledge;
        set
        {
            if (value && !CanUsePublicLookup)
            {
                return;
            }
            if (_useOpenGameKnowledge == value)
            {
                return;
            }
            _useOpenGameKnowledge = value;
            if (value)
            {
                _candidateStatus =
                    "Public game information is on. Find and choose the matching game to continue.";
            }
            else
            {
                _confirmedIdentity = null;
                _candidates.Clear();
                _selectedCandidate = null;
                _candidateStatus = "Public game information is off.";
            }
            Changed();
        }
    }

    private bool HasValidFields =>
        GameName.Trim().Length <=
            GenerationSourceGameContext.MaximumGameNameLength &&
        ContextNotes.Trim().Length <=
            GenerationSourceGameContext.MaximumNotesLength;

    public bool IsValid => HasValidFields &&
        (!_useOpenGameKnowledge || _confirmedIdentity is not null);

    public bool RequiresIdentitySelection =>
        _useOpenGameKnowledge && _confirmedIdentity is null;

    internal GenerationSourceGameContext CreateContext() =>
        new(
            SourceFullPath,
            string.IsNullOrWhiteSpace(GameName)
                ? GenerationGamePathHintPolicy.UnconfirmedGameName
                : GameName,
            ContextNotes,
            _origin,
            UseOpenGameKnowledge,
            _confirmedIdentity);

    private void ConfirmGame()
    {
        _origin = GenerationGameContextOrigin.UserConfirmed;
        _candidateStatus = IsPublicLookupAvailable
            ? "Game name confirmed. Turn on public game information to find a match, or continue without it."
            : "Game name confirmed. Public lookup is unavailable, so Replay Foundry will use the confirmed name and your notes.";
        Changed();
    }

    private static string BuildUnconfirmedStatus(string gameName) =>
        string.IsNullOrWhiteSpace(gameName)
            ? "No game selected. Replay Foundry will use only what it finds in your video."
            : GenerationGamePathHintPolicy.IsMeaningfulSuggestion(gameName)
                ? "Confirm this name only if you want it used for game-specific titles, descriptions, or tags."
                : "This folder label is too broad to use as a game name. Replay Foundry will use only what it finds in your video.";

    private async Task DiscoverCandidatesAsync()
    {
        _candidateStatus = "Finding matching games on Wikimedia…";
        OnPropertyChanged(nameof(CandidateStatus));
        try
        {
            GameIdentityCandidateSet result =
                await _candidateProvider!.DiscoverAsync(
                    new GameIdentityDiscoveryRequest(
                        GameName,
                        "en",
                        GameKnowledgeSourcePermissions.WikimediaDefault),
                    CancellationToken.None);
            _isReplacingCandidates = true;
            try
            {
                _candidates.Clear();
                foreach (GameIdentityCandidate candidate in result.Candidates)
                {
                    _candidates.Add(
                        new GameIdentityCandidateViewModel(candidate));
                }
                _selectedCandidate = _candidates.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.EntityId,
                        _confirmedIdentity?.WikidataEntityId,
                        StringComparison.Ordinal));
            }
            finally
            {
                _isReplacingCandidates = false;
            }
            if (_confirmedIdentity is not null && _selectedCandidate is null)
            {
                _confirmedIdentity = null;
            }
            _candidateStatus = _candidates.Count == 0
                ? "No clear Wikimedia match was found. Continue without public information, or revise the game name."
                : _selectedCandidate is not null
                    ? $"{_selectedCandidate.Title} is still selected. This video is ready."
                    : "Select the exact game below. Replay Foundry never picks a match for you.";
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or
                TaskCanceledException)
        {
            SafeDiagnosticTrace.Write(
                "Wikimedia game identity discovery failed",
                exception);
            if (_confirmedIdentity is not null)
            {
                _candidateStatus =
                    $"Game matching is temporarily unavailable. {_confirmedIdentity.CanonicalTitle} remains selected, and this video is ready.";
            }
            else
            {
                _candidates.Clear();
                _selectedCandidate = null;
                _candidateStatus =
                    "Game matching is temporarily unavailable. Nothing else was sent; continue without public information.";
            }
        }
        Changed();
    }

    private async Task RefreshConfirmedContextAsync()
    {
        ConfirmedGameIdentity identity = _confirmedIdentity ??
            throw new InvalidOperationException(
                "Refreshing public game context requires the exact selected identity.");
        _candidateStatus =
            $"Refreshing public information for {identity.CanonicalTitle}…";
        OnPropertyChanged(nameof(CandidateStatus));
        try
        {
            await _gameKnowledge!.RefreshAsync(
                identity,
                CancellationToken.None);
            _candidateStatus =
                $"Public information refreshed for {identity.CanonicalTitle}.";
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or
                IOException or TaskCanceledException)
        {
            _candidateStatus =
                "Public information could not refresh. Your selected game and video work were kept.";
        }
        Changed();
    }

    private void SelectCandidate(GameIdentityCandidate candidate)
    {
        _confirmedIdentity = candidate.Confirm(
            DateTimeOffset.UtcNow,
            GameKnowledgeSourcePermissions.WikimediaDefault);
        _gameName = candidate.CanonicalTitle;
        _origin = GenerationGameContextOrigin.UserConfirmed;
        _useOpenGameKnowledge = true;
        _selectedCandidate = _candidates.FirstOrDefault(choice =>
            string.Equals(
                choice.EntityId,
                candidate.WikidataEntityId,
                StringComparison.Ordinal));
        _candidateStatus =
            $"{candidate.CanonicalTitle} selected. This video is ready.";
        Changed();
    }

    private void ChooseNone()
    {
        _confirmedIdentity = null;
        _useOpenGameKnowledge = false;
        _candidates.Clear();
        _selectedCandidate = null;
        _candidateStatus =
            "Public game information is off. Replay Foundry will use only what it finds in your video.";
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(DisplayGameName));
        OnPropertyChanged(nameof(ContextNotes));
        OnPropertyChanged(nameof(UseOpenGameKnowledge));
        OnPropertyChanged(nameof(OriginText));
        OnPropertyChanged(nameof(IsConfirmed));
        OnPropertyChanged(nameof(ConfirmedIdentity));
        OnPropertyChanged(nameof(HasConfirmedIdentity));
        OnPropertyChanged(nameof(CanUsePublicLookup));
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(SelectedCandidate));
        OnPropertyChanged(nameof(HasCandidateChoices));
        OnPropertyChanged(nameof(CandidateStatus));
        OnPropertyChanged(nameof(CanDiscoverCandidates));
        OnPropertyChanged(nameof(CanRefreshConfirmedContext));
        OnPropertyChanged(nameof(CanConfirmGame));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(RequiresIdentitySelection));
        _confirmGameCommand.RaiseCanExecuteChanged();
        _discoverCandidatesCommand.RaiseCanExecuteChanged();
        _refreshConfirmedContextCommand.RaiseCanExecuteChanged();
        _chooseNoneCommand.RaiseCanExecuteChanged();
        _changed();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GameIdentityCandidateViewModel
{
    private readonly GameIdentityCandidate _candidate;

    internal GameIdentityCandidateViewModel(GameIdentityCandidate candidate)
    {
        _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
    }

    public string Title => _candidate.CanonicalTitle;

    public string Details => string.Join(
        " · ",
        new[]
        {
            _candidate.Edition,
            _candidate.ReleaseYear?.ToString(),
            _candidate.Developer,
            _candidate.Series,
            _candidate.Source,
        }.Where(static value => !string.IsNullOrWhiteSpace(value)));

    public string EntityId => _candidate.WikidataEntityId;

    public string AccessibleName => string.IsNullOrWhiteSpace(Details)
        ? $"{Title}, {EntityId}"
        : $"{Title}, {Details}, {EntityId}";

    public string SelectionHelpText =>
        $"Choose {Title} as this video's game.";

    internal GameIdentityCandidate Candidate => _candidate;

}

public sealed class GameContextStepViewModel : INotifyPropertyChanged
{
    private readonly GenerationSetupDraft _draft;
    private readonly ReadOnlyCollection<GameContextSourceViewModel> _sources;
    private GameContextSourceViewModel _selectedSource;

    public GameContextStepViewModel(
        GenerationSetupDraft draft,
        IGameIdentityCandidateProvider? candidateProvider = null,
        IGenerationGameKnowledgeService? gameKnowledge = null)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        GameContextSourceViewModel[] sources = draft.GameContextSettings.Sources
            .Select(context => new GameContextSourceViewModel(
                context,
                SourceChanged,
                candidateProvider,
                gameKnowledge))
            .ToArray();
        if (sources.Length == 0)
        {
            throw new ArgumentException(
                "Game context requires at least one prepared source.",
                nameof(draft));
        }
        _sources = Array.AsReadOnly(sources);
        _selectedSource = sources[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<GameContextSourceViewModel> Sources => _sources;

    public GameContextSourceViewModel SelectedSource
    {
        get => _selectedSource;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_sources.Contains(value))
            {
                throw new ArgumentException(
                    "The selected game-context source is not available.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedSource, value))
            {
                return;
            }
            _selectedSource = value;
            OnPropertyChanged();
        }
    }

    public bool IsBatch => Sources.Count > 1;

    public bool IsValid => Sources.All(static source => source.IsValid);

    public string? ValidationMessage => IsValid
        ? null
        : Sources.Any(static source => source.RequiresIdentitySelection)
            ? "Choose the matching Wikimedia game, or continue without public game information."
            : "Optional game names may contain up to 120 characters; optional notes may contain up to 1,500 characters.";

    public string Summary => Sources.Count == 1
        ? Sources[0].IsConfirmed
            ? $"{Sources[0].GameName} · {Sources[0].OriginText}"
            : "No confirmed game · video only"
        : $"{Sources.Count} videos ready";

    private void SourceChanged()
    {
        if (IsValid)
        {
            _draft.UpdateGameContextSettings(
                new GenerationGameContextSettings(
                    Sources.Select(static source => source.CreateContext())));
        }
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(Summary));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
