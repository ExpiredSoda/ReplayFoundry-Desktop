using System.IO;
using System.Text.Json;
using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

public sealed class YouTubeAnalyticsViewModel : ObservableObject, IDisposable
{
    private readonly IYouTubeAnalyticsService? _service;
    private readonly Func<IReadOnlyList<YouTubePublishHistoryEntry>> _history;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AsyncDelegateCommand _connect;
    private readonly AsyncDelegateCommand _refresh;
    private readonly AsyncDelegateCommand _disconnect;
    private readonly DelegateCommand _clear;
    private string _status = "Connect read-only analytics to compare how published clips performed.";
    private bool _busy;
    private bool _disposed;
    private Task _active = Task.CompletedTask;
    private DateTime? _from = DateTime.Today.AddDays(-28);
    private DateTime? _to = DateTime.Today.AddDays(-1);
    private IReadOnlyList<YouTubeVideoObservation> _observations = [];
    private string _comparisonFilter = "";
    private string? _referenceVideoId;
    private bool _matchGame = true, _matchFormat = true, _matchChannel = true, _matchAge = true, _matchDuration = true;
    private readonly IYouTubeObservationStore _store;

    public YouTubeAnalyticsViewModel(IYouTubeAnalyticsService? service,
        Func<IReadOnlyList<YouTubePublishHistoryEntry>> history, string? observationsStorePath = null)
    {
        _service = service;
        _history = history;
        _store = YouTubeObservationStoreFactory.Create(observationsStorePath);
        _connect = new AsyncDelegateCommand(() => TrackAsync(async token =>
        {
            await _service!.ConnectAsync(token);
            _status = "Read-only analytics connected. Choose a period and refresh.";
        }), CanRun);
        _refresh = new AsyncDelegateCommand(() => TrackAsync(RefreshAsync), CanRun);
        _disconnect = new AsyncDelegateCommand(() => TrackAsync(async token =>
        {
            await _service!.DisconnectAsync(token);
            _status = "Analytics disconnected. Saved observations remain local until cleared.";
        }), CanRun);
        _clear = new DelegateCommand(Clear, () => !_busy);
        if (service is not null) Load();
    }

    public bool IsAvailable => _service is not null;
    public string Status => _status;
    public bool IsBusy => _busy;
    public IReadOnlyList<YouTubeVideoObservation> Observations
    {
        get
        {
            IReadOnlyList<YouTubeVideoObservation> values = _observations;
            if (_observations.FirstOrDefault(item => item.VideoId == _referenceVideoId) is { } reference)
                values = YouTubeObservationCohorts.Match(values, reference, MatchGame, MatchFormat, MatchChannel, MatchAge, MatchDuration);
            return string.IsNullOrWhiteSpace(ComparisonFilter) ? values : values.Where(observation => observation.ComparisonText.Contains(
                ComparisonFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }
    public sealed record ReferenceChoice(string? VideoId, string Label) { public override string ToString() => Label; }
    public IReadOnlyList<ReferenceChoice> ReferenceChoices => [new(null, "All saved observations"),
        .. _observations.Select(item => new ReferenceChoice(item.VideoId, item.PublishedTitle))];
    public string? ReferenceVideoId { get => _referenceVideoId; set { _referenceVideoId = value; ComparisonChanged(); } }
    public bool MatchGame { get => _matchGame; set { _matchGame = value; ComparisonChanged(); } }
    public bool MatchFormat { get => _matchFormat; set { _matchFormat = value; ComparisonChanged(); } }
    public bool MatchChannel { get => _matchChannel; set { _matchChannel = value; ComparisonChanged(); } }
    public bool MatchAge { get => _matchAge; set { _matchAge = value; ComparisonChanged(); } }
    public bool MatchDuration { get => _matchDuration; set { _matchDuration = value; ComparisonChanged(); } }
    public string CohortSummary => _referenceVideoId is null ? "Choose a reference clip to compare the same reporting period and similar uploads." :
        YouTubeObservationCohorts.Describe(Observations);
    private void ComparisonChanged([System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    { OnPropertyChanged(property); OnPropertyChanged(nameof(Observations)); OnPropertyChanged(nameof(CohortSummary)); }
    public string ComparisonFilter
    {
        get => _comparisonFilter;
        set { _comparisonFilter = value ?? ""; ComparisonChanged(); }
    }
    public DateTime? From { get => _from; set { _from = value; OnPropertyChanged(); } }
    public DateTime? To { get => _to; set { _to = value; OnPropertyChanged(); } }
    public ICommand ConnectCommand => _connect;
    public ICommand RefreshCommand => _refresh;
    public ICommand DisconnectCommand => _disconnect;
    public ICommand ClearCommand => _clear;
    private bool CanRun() => IsAvailable && !_busy && !_lifetime.IsCancellationRequested;

    private async Task RefreshAsync(CancellationToken token)
    {
        if (From is not DateTime start || To is not DateTime end)
            throw new ArgumentException("Choose both reporting dates.");
        _observations = await _service!.ReadAsync(_history(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(end), token);
        if (!_observations.Any(item => item.VideoId == _referenceVideoId)) _referenceVideoId = null;
        await _store.SaveAsync(_observations, token);
        _status = $"Saved {_observations.Count} observations for {start:d}–{end:d}. Compare the same period and similar clips; these are not randomized experiments.";
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (!CanRun()) return;
        _busy = true; _status = "Reading YouTube Analytics…"; Notify();
        try { await operation(_lifetime.Token); }
        catch (OperationCanceledException) { _status = "Analytics request cancelled."; }
        catch (Exception exception) { _status = exception.Message; }
        finally { _busy = false; Notify(); }
    }

    private Task TrackAsync(Func<CancellationToken, Task> operation) => _active = RunAsync(operation);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifetime.CancelAsync();
        await _active.WaitAsync(cancellationToken);
    }

    private void Load()
    {
        try
        {
            YouTubeSavedObservations.LoadResult loaded = _store.Load();
            _observations = loaded.Observations;
            if (loaded.DiscardedCount > 0)
                _status = $"Skipped {loaded.DiscardedCount} invalid, duplicate, or excess saved observations. Refresh analytics to rebuild the report.";
            if (_observations.FirstOrDefault() is { } saved)
            { _from = saved.From.ToDateTime(TimeOnly.MinValue); _to = saved.To.ToDateTime(TimeOnly.MinValue); }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        { _status = "Saved observations could not be read. Refresh analytics to rebuild them."; }
    }

    private void Clear()
    {
        try
        {
            _store.Clear();
            _observations = []; _referenceVideoId = null; _status = "Saved analytics observations cleared.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { _status = exception.Message; }
        Notify();
    }

    private void Notify()
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(Observations));
        OnPropertyChanged(nameof(ReferenceChoices)); OnPropertyChanged(nameof(CohortSummary));
        OnPropertyChanged(nameof(ReferenceVideoId));
        _connect.RaiseCanExecuteChanged(); _refresh.RaiseCanExecuteChanged(); _disconnect.RaiseCanExecuteChanged(); _clear.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        if (_active.IsCompleted) _lifetime.Dispose();
        else _ = _active.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
    }
}
