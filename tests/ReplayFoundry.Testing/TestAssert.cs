namespace ReplayFoundry.Testing;

public static class TestAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message) =>
        True(!condition, message);

    public static void Equal<TValue>(TValue expected, TValue actual, string message)
    {
        if (!EqualityComparer<TValue>.Default.Equals(expected, actual))
        {
            throw Failure(message, $"Expected: {expected}; Actual: {actual}.");
        }
    }

    public static void NearlyEqual(
        double expected,
        double actual,
        double tolerance,
        string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw Failure(
                message,
                $"Expected: {expected}; Actual: {actual}; Tolerance: {tolerance}.");
        }
    }

    public static void Same(object expected, object actual, string message) =>
        True(ReferenceEquals(expected, actual), message);

    public static void Null(object? value, string message)
    {
        if (value is not null)
        {
            throw Failure(message, $"Actual: {value}.");
        }
    }

    public static TValue NotNull<TValue>(TValue? value, string message)
        where TValue : class =>
        value ?? throw new InvalidOperationException(message);

    public static void Contains<TValue>(
        IEnumerable<TValue> values,
        Func<TValue, bool> predicate,
        string message) =>
        True(values.Any(predicate), message);

    public static void Contains(
        string expectedSubstring,
        IEnumerable<string> values,
        string message) =>
        True(
            values.Any(value => value.Contains(expectedSubstring, StringComparison.Ordinal)),
            $"{message} Missing substring: {expectedSubstring}.");

    public static TException Throws<TException>(Action action, string message)
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
            throw WrongException<TException>(message, exception);
        }

        throw MissingException<TException>(message);
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
            throw WrongException<TException>(message, exception);
        }

        throw MissingException<TException>(message);
    }

    private static InvalidOperationException Failure(string message, string detail) =>
        new($"{message} {detail}");

    private static InvalidOperationException MissingException<TException>(string message) =>
        Failure(message, $"Expected {typeof(TException).Name}, but no exception was thrown.");

    private static InvalidOperationException WrongException<TException>(
        string message,
        Exception exception) =>
        new(
            $"{message} Expected {typeof(TException).Name}, but received " +
            $"{exception.GetType().Name}.",
            exception);
}
