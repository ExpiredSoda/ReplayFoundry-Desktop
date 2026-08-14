using ReplayFoundry.Desktop.Features.Generate.Workflow;

namespace ReplayFoundry.Desktop.Features.Generate;

internal static class GenerateWorkflowPresentation
{
    internal static string StatusText(GenerateWorkflowState state) => state switch
    {
        GenerateWorkflowState.SourceSelection => "Ready for sources",
        GenerateWorkflowState.PreparingSources => "Preparing selected videos",
        GenerateWorkflowState.ReviewingComposition => "Reviewing video layouts",
        GenerateWorkflowState.AnalyzingEvidence => "Studying selected videos",
        GenerateWorkflowState.Generating => "Building clip selections",
        GenerateWorkflowState.Completed => "Studio project ready",
        GenerateWorkflowState.Failed => "Generation needs attention",
        GenerateWorkflowState.Cancelled => "Generation cancelled",
        _ => "Generate",
    };

    internal static bool ShowsSourceSelection(GenerateWorkflowState state) =>
        state is GenerateWorkflowState.SourceSelection or
            GenerateWorkflowState.ReviewingComposition;

    internal static bool ShowsProgress(GenerateWorkflowState state) =>
        !ShowsSourceSelection(state);

    internal static string SelectionSummary(int sourceCount) =>
        sourceCount == 1 ? "1 file selected" : $"{sourceCount} files selected";

    internal static string? CompositionSummary(int? sourceCount) => sourceCount switch
    {
        null => null,
        1 => "1 source layout confirmed",
        int count => $"{count} source layouts confirmed",
    };

    internal static string SetupButtonText(bool hasSetup) =>
        hasSetup ? "Edit Generation Setup" : "Continue to Generation Setup";
}
