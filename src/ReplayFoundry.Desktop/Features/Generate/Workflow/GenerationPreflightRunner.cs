using ReplayFoundry.Desktop.Features.Generate.Evidence;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal sealed class GenerationPreflightRunner
{
    public void Validate(
        GenerationRequest request,
        IProgress<GenerationProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);

        cancellationToken.ThrowIfCancellationRequested();

        progress.Report(
            new GenerationProgressUpdate(
                "Checking your videos",
                "The selected videos are ready.",
                isIndeterminate: false,
                progressPercent: 5));

        GenerationPreflightValidator
            .ValidateSupportedInputs(
                request.Preparation,
                request.SetupOptions,
                request.CompositionReview);

        progress.Report(
            new GenerationProgressUpdate(
                "Choices ready",
                "The clip-finding and audio choices are ready.",
                isIndeterminate: false,
                progressPercent: 10));

        cancellationToken.ThrowIfCancellationRequested();

        progress.Report(
            new GenerationProgressUpdate(
                "Video layouts ready",
                "Every video has the layout you confirmed.",
                isIndeterminate: false,
                progressPercent: 15));

        GenerationPreflightValidator.ValidateEvidence(
            request.Preparation,
            request.CompositionReview,
            request.EvidenceAnalysis);

        cancellationToken.ThrowIfCancellationRequested();

        progress.Report(
            new GenerationProgressUpdate(
                "Picture and sound checked",
                BuildEvidenceSummary(
                    request.EvidenceAnalysis),
                isIndeterminate: false,
                progressPercent: 20));
    }

    private static string BuildEvidenceSummary(
        GenerationEvidenceAnalysisResult evidence)
    {
        return evidence.Sources.Count == 1
            ? "Replay Foundry checked the full picture, your selected areas, and the audio for 1 video."
            : $"Replay Foundry checked the full picture, your selected areas, and the audio for {evidence.Sources.Count} videos.";
    }
}
