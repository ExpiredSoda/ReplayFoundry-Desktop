using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

public sealed record GenerationRuntimeCapabilities(
    bool IsCaptionTranscriptionAvailable,
    bool IsSpeechActivityAvailable = false,
    bool IsVisualSemanticReviewAvailable = false,
    bool IsEditorialAiAvailable = false,
    string? EditorialAiUnavailableReason = null,
    Func<string?>? EditorialGpuAdmissionCheck = null,
    Func<string>? EditorialGpuReadiness = null,
    AudioTranscriptionModelLanguageCapabilities? CaptionLanguageCapabilities = null)
{
    public const string DefaultEditorialAiUnavailableReason =
        "Local AI title writing is not ready. Install or repair " +
        "Advanced AI, restart Replay Foundry, and try again.";

    public string EditorialAiBlockingReason =>
        string.IsNullOrWhiteSpace(EditorialAiUnavailableReason)
            ? DefaultEditorialAiUnavailableReason
            : EditorialAiUnavailableReason.Trim();

    public static GenerationRuntimeCapabilities DeterministicOnly { get; } =
        new(
            IsCaptionTranscriptionAvailable: false,
            IsSpeechActivityAvailable: false,
            IsVisualSemanticReviewAvailable: false,
            IsEditorialAiAvailable: false,
            EditorialAiUnavailableReason:
                DefaultEditorialAiUnavailableReason);
}
