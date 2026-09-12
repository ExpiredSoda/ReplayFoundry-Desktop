using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Settings;

public interface IApplicationUpdateService
{
    event EventHandler? Changed;
    bool IsAvailable { get; }
    bool AutomaticChecksEnabled { get; set; }
    string Status { get; }
    void CheckForUpdates();
}

public sealed class ApplicationUpdateViewModel : ObservableObject, IDisposable
{
    private IApplicationUpdateService? _service;
    private readonly DelegateCommand _check;

    public ApplicationUpdateViewModel()
    {
        _check = new(() => _service?.CheckForUpdates(), () => IsAvailable);
    }

    public bool IsAvailable => _service?.IsAvailable == true;
    public string Status => _service?.Status ?? "Update checks are available in installed release builds.";
    public bool AutomaticChecksEnabled
    {
        get => _service?.AutomaticChecksEnabled == true;
        set { if (_service is not null) _service.AutomaticChecksEnabled = value; }
    }
    public ICommand CheckForUpdatesCommand => _check;

    public void Attach(IApplicationUpdateService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service is not null) _service.Changed -= OnChanged;
        _service = service;
        _service.Changed += OnChanged;
        OnChanged(this, EventArgs.Empty);
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(AutomaticChecksEnabled));
        _check.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_service is not null) _service.Changed -= OnChanged;
    }
}
