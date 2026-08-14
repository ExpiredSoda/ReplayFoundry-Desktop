namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

public sealed record GenerationRuntimeCapabilities(
    bool IsCaptionTranscriptionAvailable,
    bool IsSpeechActivityAvailable = false,
    bool IsVisualSemanticReviewAvailable = false)
{
    public static GenerationRuntimeCapabilities DeterministicOnly { get; } =
        new(
            IsCaptionTranscriptionAvailable: false,
            IsSpeechActivityAvailable: false,
            IsVisualSemanticReviewAvailable: false);
}
