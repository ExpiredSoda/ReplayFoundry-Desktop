namespace ReplayFoundry.InspectionTests;

internal sealed record TestCase(
    string Name,
    Action Execute);
