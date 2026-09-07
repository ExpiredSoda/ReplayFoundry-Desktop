using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using ReplayFoundry.Desktop.Media.Intelligence.Learning;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Personalization;

public sealed class TasteLearningSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ITasteLearningService? _learning;
    private readonly AsyncDelegateCommand _train;
    private readonly AsyncDelegateCommand _reset;
    private string? _notice;
    public TasteLearningSettingsViewModel(ITasteLearningService? learning)
    {
        _learning = learning;
        _train = new(() => RunAsync(() => _learning!.TrainAsync(CancellationToken.None)), () => IsAvailable && Enabled && !IsWorking && Ratings >= 8);
        _reset = new(() => RunAsync(() => _learning!.ResetAsync(CancellationToken.None)), () => IsAvailable && !IsWorking);
        if (learning is not null) learning.Changed += Changed;
    }
    public bool IsAvailable => _learning is not null;
    public bool Enabled
    {
        get => _learning?.Status.Enabled == true;
        set
        {
            try { _notice = null; _learning?.SetEnabled(value); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { _notice = e.Message; Changed(this, EventArgs.Empty); }
        }
    }
    public bool IsWorking => _learning?.Status.IsWorking == true;
    public int Ratings => _learning?.Status.Ratings ?? 0;
    public string State => !IsAvailable ? "Unavailable" : !Enabled ? "Paused" : IsWorking ? "Updating" : _learning!.Status.IsActive ? "Active" : "Learning";
    public string Summary => $"{Ratings} rated clips · {_learning?.Status.Recordings ?? 0} recordings";
    public string Status => _notice ?? _learning?.Status.Message ?? "Local learning is unavailable right now.";
    public ICommand TrainCommand => _train;
    public ICommand ResetCommand => _reset;
    private async Task RunAsync(Func<Task> action)
    {
        _notice = null;
        try { await action(); }
        catch (OperationCanceledException) { _notice = "The update stopped. Your saved model is kept."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { _notice = e.Message; }
        Changed(this, EventArgs.Empty);
    }
    private void Changed(object? sender, EventArgs e)
    {
        foreach (string property in new[] { nameof(Enabled), nameof(IsWorking), nameof(Ratings), nameof(State), nameof(Summary), nameof(Status) })
            OnPropertyChanged(property);
        _train.RaiseCanExecuteChanged(); _reset.RaiseCanExecuteChanged();
    }
    public void Dispose() { if (_learning is not null) _learning.Changed -= Changed; }
}
