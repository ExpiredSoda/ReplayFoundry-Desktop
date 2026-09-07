using System.Windows.Threading;

namespace ReplayFoundry.Desktop.Presentation.Commands;

internal sealed class DebouncedUiAction : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action _action;

    public DebouncedUiAction(TimeSpan delay, Action action)
    {
        _action = action;
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += OnTick;
    }

    public void Restart() { _timer.Stop(); _timer.Start(); }
    public void Cancel() => _timer.Stop();
    public void Dispose() { _timer.Stop(); _timer.Tick -= OnTick; }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _action();
    }
}
