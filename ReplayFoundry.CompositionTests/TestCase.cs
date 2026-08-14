namespace ReplayFoundry.CompositionTests;

internal sealed record TestCase(
    string Name,
    Action Execute);
