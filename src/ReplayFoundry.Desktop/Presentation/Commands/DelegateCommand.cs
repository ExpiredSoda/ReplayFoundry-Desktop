using System;
using System.Windows.Input;

namespace ReplayFoundry.Desktop.Presentation.Commands;

internal sealed class DelegateCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;

    public DelegateCommand(
        Action execute,
        Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _canExecute = canExecute ?? (() => true);
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _canExecute();
    }

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            throw new InvalidOperationException(
                "The command cannot execute in the current state.");
        }

        _execute();
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(
            this,
            EventArgs.Empty);
    }
}
