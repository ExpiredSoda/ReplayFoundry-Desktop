using System.Windows.Input;

namespace ReplayFoundry.Desktop.Presentation.Commands;

internal sealed class AsyncDelegateCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool> _canExecute;
    private bool _isExecuting;

    public AsyncDelegateCommand(
        Func<Task> executeAsync,
        Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);

        _executeAsync = executeAsync;
        _canExecute = canExecute ?? (() => true);
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting &&
               _canExecute();
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync();
    }

    public async Task ExecuteAsync()
    {
        if (!CanExecute(parameter: null))
        {
            throw new InvalidOperationException(
                "The command cannot execute in the current state.");
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _executeAsync();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(
            this,
            EventArgs.Empty);
    }
}
