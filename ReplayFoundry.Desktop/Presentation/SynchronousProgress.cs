namespace ReplayFoundry.Desktop.Presentation;

internal sealed class SynchronousProgress<TValue> :
    IProgress<TValue>
{
    private readonly Action<TValue> _report;

    public SynchronousProgress(
        Action<TValue> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _report = report;
    }

    public void Report(
        TValue value)
    {
        _report(value);
    }
}
