using System;
using System.Windows.Input;

namespace ReplayFoundry.Desktop.Presentation.Commands;

internal sealed class DelegateCommand<T> : ICommand
    where T : notnull
{
    private readonly Action<T> _execute;
    private readonly Predicate<T> _canExecute;

    public DelegateCommand(
        Action<T> execute,
        Predicate<T>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _canExecute = canExecute ?? (_ => true);
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return TryGetParameter(
                   parameter,
                   out T value) &&
               _canExecute(value);
    }

    public void Execute(object? parameter)
    {
        if (!TryGetParameter(
                parameter,
                out T value))
        {
            throw new ArgumentException(
                $"A command parameter of type '{typeof(T).Name}' is required.",
                nameof(parameter));
        }

        if (!_canExecute(value))
        {
            throw new InvalidOperationException(
                "The command cannot execute for the supplied parameter in the current state.");
        }

        _execute(value);
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(
            this,
            EventArgs.Empty);
    }

    private static bool TryGetParameter(
        object? parameter,
        out T value)
    {
        if (parameter is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default!;
        return false;
    }
}
