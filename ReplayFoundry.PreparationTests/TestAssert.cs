namespace ReplayFoundry.PreparationTests;

internal static class TestAssert
{
    public static void True(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(
        bool condition,
        string message) =>
        True(
            !condition,
            message);

    public static void Equal<TValue>(
        TValue expected,
        TValue actual,
        string message)
    {
        if (!EqualityComparer<TValue>.Default.Equals(
                expected,
                actual))
        {
            throw new InvalidOperationException(
                $"{message} Expected: {expected}; Actual: {actual}.");
        }
    }

    public static void Same(
        object expected,
        object actual,
        string message)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Null(
        object? value,
        string message)
    {
        if (value is not null)
        {
            throw new InvalidOperationException(
                $"{message} Actual: {value}.");
        }
    }

    public static TException Throws<TException>(
        Action action,
        string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{message} Expected {typeof(TException).Name}, but received " +
                $"{exception.GetType().Name}.",
                exception);
        }

        throw new InvalidOperationException(
            $"{message} Expected exception: {typeof(TException).Name}.");
    }

    public static async Task<TException> ThrowsAsync<TException>(
        Func<Task> action,
        string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{message} Expected {typeof(TException).Name}, but received " +
                $"{exception.GetType().Name}.",
                exception);
        }

        throw new InvalidOperationException(
            $"{message} Expected exception: {typeof(TException).Name}.");
    }
}
