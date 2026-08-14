using System;
using System.Collections.Generic;

namespace ReplayFoundry.InspectionTests;

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
        string message)
    {
        True(!condition, message);
    }

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
                $"{message} Expected: {expected}. Actual: {actual}.");
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
            throw new InvalidOperationException(
                $"{message} Expected: {expected}. Actual: {actual}. " +
                $"Tolerance: {tolerance}.");
        }
    }

    public static void Null(
        object? value,
        string message)
    {
        if (value is not null)
        {
            throw new InvalidOperationException(
                $"{message} Actual value: {value}.");
        }
    }

    public static TValue NotNull<TValue>(
        TValue? value,
        string message)
        where TValue : class
    {
        return value ??
               throw new InvalidOperationException(message);
    }

    public static void Contains<TValue>(
        IEnumerable<TValue> values,
        Func<TValue, bool> predicate,
        string message)
    {
        foreach (TValue value in values)
        {
            if (predicate(value))
            {
                return;
            }
        }

        throw new InvalidOperationException(message);
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
                $"{message} Expected {typeof(TException).Name}, but " +
                $"received {exception.GetType().Name}.",
                exception);
        }

        throw new InvalidOperationException(
            $"{message} Expected {typeof(TException).Name}, but no " +
            "exception was thrown.");
    }
}
