using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Settings;

public sealed class SharedTrainingSettingsViewModel : ObservableObject
{
    private readonly ISharedTrainingContributions service;
    public SharedTrainingSettingsViewModel(ISharedTrainingContributions service)
    {
        this.service = service;
        EnableCommand = new DelegateCommand(() => Execute(service.Enable));
        DisableCommand = new DelegateCommand(() => Execute(service.Disable));
        SendCommand = new AsyncDelegateCommand(async () =>
        {
            await service.SendPendingAsync();
            _message = null;
            Refresh();
        });
        DeleteCommand = new AsyncDelegateCommand(async () =>
        {
            try { await service.DeleteSharedAsync(); _message = "Sharing is off. Submitted records were deleted."; }
            catch (Exception) { _message = "Deletion has not been confirmed; check the sharing status and retry when connected."; }
            Refresh();
        });
    }
    public bool IsEnabled => service.IsEnabled;
    public string ConsentStatus => IsEnabled ? "Text sharing enabled" : "Text sharing off";
    public string Status => _message ?? service.Status;
    private string? _message;
    public ICommand EnableCommand { get; }
    public ICommand DisableCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand DeleteCommand { get; }
    private void Execute(Action action)
    {
        try { action(); _message = null; }
        catch (Exception) { _message = "The sharing preference could not be saved. No new permission was granted."; }
        Refresh();
    }
    private void Refresh() { OnPropertyChanged(nameof(IsEnabled)); OnPropertyChanged(nameof(ConsentStatus)); OnPropertyChanged(nameof(Status)); }
}
