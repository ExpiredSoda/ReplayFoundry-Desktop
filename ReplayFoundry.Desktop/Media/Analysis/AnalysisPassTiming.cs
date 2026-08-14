using System;

namespace ReplayFoundry.Desktop.Media.Analysis;

public sealed class AnalysisPassTiming
{
    public AnalysisPassTiming(
        string name,
        TimeSpan elapsed)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "An analysis pass requires a name.",
                nameof(name));
        }

        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                elapsed,
                "Analysis pass duration cannot be negative.");
        }

        Name = name;
        Elapsed = elapsed;
    }

    public string Name { get; }

    public TimeSpan Elapsed { get; }
}
