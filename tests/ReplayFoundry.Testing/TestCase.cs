namespace ReplayFoundry.Testing;

public sealed class TestCase
{
    public TestCase(string name, Action execute)
        : this(name, RunSynchronously(execute))
    {
    }

    public TestCase(string name, Func<Task> executeAsync)
    {
        Name = name;
        ExecuteAsync = executeAsync;
    }

    public string Name { get; }

    public Func<Task> ExecuteAsync { get; }

    private static Func<Task> RunSynchronously(Action execute) =>
        () =>
        {
            execute();
            return Task.CompletedTask;
        };
}
