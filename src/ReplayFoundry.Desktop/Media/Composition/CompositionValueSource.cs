namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// Records where one composition value came from.
/// </summary>
public enum CompositionValueSource
{
    NotAvailable,
    UserConfirmed,
    RecordingProfile,
    AutomaticAnalyzer,
    DefaultAssumption,
}
