using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.GameContext;

public sealed class GameContextSourceViewModel : INotifyPropertyChanged
{
    private readonly Action _changed;
    private string _gameName;
    private string _contextNotes;
    private GenerationGameContextOrigin _origin;
    private bool _useOpenGameKnowledge;

    internal GameContextSourceViewModel(
        GenerationSourceGameContext context,
        Action changed)
    {
        ArgumentNullException.ThrowIfNull(context);
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        SourceFullPath = context.SourceFullPath;
        SourceName = Path.GetFileName(context.SourceFullPath);
        _gameName = context.GameName;
        _contextNotes = context.ContextNotes ?? string.Empty;
        _origin = context.Origin;
        _useOpenGameKnowledge = context.UseOpenGameKnowledge;
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
            _origin = GenerationGameContextOrigin.UserConfirmed;
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
            _origin = GenerationGameContextOrigin.UserConfirmed;
            Changed();
        }
    }

    public string OriginText => _origin switch
    {
        GenerationGameContextOrigin.UserConfirmed => "Confirmed by you",
        GenerationGameContextOrigin.ReusedUserMemory => "Reused from your local memory",
        GenerationGameContextOrigin.SourcePathHint => "Suggested from the folder name",
        _ => throw new InvalidOperationException("Unknown game-context origin."),
    };

    public bool UseOpenGameKnowledge
    {
        get => _useOpenGameKnowledge;
        set
        {
            if (_useOpenGameKnowledge == value)
            {
                return;
            }
            _useOpenGameKnowledge = value;
            if (value)
            {
                _origin = GenerationGameContextOrigin.UserConfirmed;
            }
            Changed();
        }
    }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(GameName) &&
        GameName.Trim().Length <=
            GenerationSourceGameContext.MaximumGameNameLength &&
        ContextNotes.Trim().Length <=
            GenerationSourceGameContext.MaximumNotesLength;

    internal GenerationSourceGameContext CreateContext() =>
        new(
            SourceFullPath,
            GameName,
            ContextNotes,
            _origin,
            UseOpenGameKnowledge);

    private void Changed()
    {
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(ContextNotes));
        OnPropertyChanged(nameof(UseOpenGameKnowledge));
        OnPropertyChanged(nameof(OriginText));
        OnPropertyChanged(nameof(IsValid));
        _changed();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GameContextStepViewModel : INotifyPropertyChanged
{
    private readonly GenerationSetupDraft _draft;
    private readonly ReadOnlyCollection<GameContextSourceViewModel> _sources;
    private GameContextSourceViewModel _selectedSource;

    public GameContextStepViewModel(GenerationSetupDraft draft)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        GameContextSourceViewModel[] sources = draft.GameContextSettings.Sources
            .Select(context => new GameContextSourceViewModel(
                context,
                SourceChanged))
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
        : "Give every source a game name of 120 characters or fewer; optional notes may contain up to 1,500 characters.";

    public string Summary => Sources.Count == 1
        ? $"{Sources[0].GameName} · {Sources[0].OriginText}"
        : $"{Sources.Count} source game contexts ready";

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
