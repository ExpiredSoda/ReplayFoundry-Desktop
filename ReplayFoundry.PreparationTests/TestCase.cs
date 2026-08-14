namespace ReplayFoundry.PreparationTests;

internal sealed record TestCase(
    string Name,
    Func<Task> ExecuteAsync);
