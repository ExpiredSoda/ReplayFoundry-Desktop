namespace ReplayFoundry.Desktop.Composition;

internal sealed record ApplicationUpdateActivity(
    bool Generation, bool Rendering, bool Publishing, bool Learning,
    bool UnsavedEdits, bool OpenWorkflow);

internal static class ApplicationUpdateReadiness
{
    public static string? GetBlockReason(ApplicationUpdateActivity activity) => activity switch
    {
        { Generation: true } => "Finish or cancel moment finding before installing the update.",
        { Rendering: true } => "Wait for clip preparation and rendering to finish before updating.",
        { Publishing: true } => "Wait for publishing and writing to finish before updating.",
        { Learning: true } => "Wait for local model work to finish before updating.",
        { UnsavedEdits: true } => "Save or discard your pending edits before installing the update.",
        { OpenWorkflow: true } => "Finish or close the open setup or clip-creation workflow before updating.",
        _ => null,
    };
}
