namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

public enum GenerateWorkflowState
{
    SourceSelection,
    PreparingSources,
    ReviewingComposition,
    AnalyzingEvidence,
    Generating,
    Completed,
    Failed,
    Cancelled,
}
