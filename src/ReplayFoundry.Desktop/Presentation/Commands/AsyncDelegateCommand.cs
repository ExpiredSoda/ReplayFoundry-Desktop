using System.Windows.Input;

namespace ReplayFoundry.Desktop.Presentation.Commands;

internal sealed class AsyncDelegateCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool> _canExecute;
    private readonly AsyncOperationLifetime? _operationLifetime;
    private bool _isExecuting;

    internal static event EventHandler<AsyncCommandFailureEventArgs>?
        UnhandledExecutionFailure;

    public AsyncDelegateCommand(
        Func<Task> executeAsync,
        Func<bool>? canExecute = null,
        AsyncOperationLifetime? operationLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);

        _executeAsync = executeAsync;
        _canExecute = canExecute ?? (() => true);
        _operationLifetime = operationLifetime;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _operationLifetime?.IsSealed != true &&
               CanExecuteCore();
    }

    public async void Execute(object? parameter)
    {
        if (_operationLifetime is not null)
        {
            await _operationLifetime.RunAsync(ExecuteAndReportAsync);
            return;
        }
        await ExecuteAndReportAsync();
    }

    private async Task ExecuteAndReportAsync()
    {
        try
        {
            await ExecuteCoreAsync();
        }
        catch (Exception exception)
        {
            if (!TryReportUnhandled(this, exception)) throw;
        }
    }

    public Task ExecuteAsync() =>
        _operationLifetime?.RunAsync(ExecuteCoreAsync) ?? ExecuteCoreAsync();

    private async Task ExecuteCoreAsync()
    {
        if (!CanExecuteCore())
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

    private bool CanExecuteCore() => !_isExecuting && _canExecute();

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(
            this,
            EventArgs.Empty);
    }

    internal static bool TryReportUnhandled(
        object sender,
        Exception exception)
    {
        EventHandler<AsyncCommandFailureEventArgs>? handler =
            UnhandledExecutionFailure;
        if (handler is null)
        {
            return false;
        }

        handler(sender, new AsyncCommandFailureEventArgs(exception));
        return true;
    }
}

internal sealed class AsyncDelegateCommand<T> : ICommand
{
    private readonly Func<T, Task> _executeAsync;
    private readonly Predicate<T> _canExecute;
    private bool _isExecuting;

    public AsyncDelegateCommand(
        Func<T, Task> executeAsync,
        Predicate<T>? canExecute = null)
    {
        _executeAsync = executeAsync ??
            throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute ?? (_ => true);
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting &&
        parameter is T value &&
        _canExecute(value);

    public async void Execute(object? parameter)
    {
        try
        {
            if (parameter is not T value)
            {
                throw new ArgumentException(
                    $"The command requires a {typeof(T).Name} parameter.",
                    nameof(parameter));
            }

            await ExecuteAsync(value);
        }
        catch (Exception exception)
        {
            if (!AsyncDelegateCommand.TryReportUnhandled(this, exception))
            {
                throw;
            }
        }
    }

    internal async Task ExecuteAsync(T parameter)
    {
        if (!CanExecute(parameter))
        {
            throw new InvalidOperationException(
                "The command cannot execute in the current state.");
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _executeAsync(parameter);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class AsyncCommandFailureEventArgs : EventArgs
{
    internal AsyncCommandFailureEventArgs(Exception exception) =>
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));

    internal Exception Exception { get; }
}
