using System;
using System.Windows.Input;

namespace ReplayFoundry.Desktop.Shell.Navigation;

internal sealed class NavigateCommand : ICommand
{
    private readonly Action<ShellDestination> _execute;
    private readonly Predicate<ShellDestination> _canExecute;

    public NavigateCommand(
        Action<ShellDestination> execute,
        Predicate<ShellDestination> canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(canExecute);

        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return TryGetDestination(
                   parameter,
                   out ShellDestination destination) &&
               _canExecute(destination);
    }

    public void Execute(object? parameter)
    {
        if (!TryGetDestination(
                parameter,
                out ShellDestination destination))
        {
            throw new ArgumentException(
                "A valid shell destination is required.",
                nameof(parameter));
        }

        if (!_canExecute(destination))
        {
            throw new InvalidOperationException(
                $"Navigation to '{destination}' is not currently available.");
        }

        _execute(destination);
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(
            this,
            EventArgs.Empty);
    }

    private static bool TryGetDestination(
        object? parameter,
        out ShellDestination destination)
    {
        if (parameter is ShellDestination candidate &&
            Enum.IsDefined(
                typeof(ShellDestination),
                candidate))
        {
            destination = candidate;
            return true;
        }

        destination = default;
        return false;
    }
}
