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
                "Using prepared source details",
                "The retained structural inspection is ready.",
                isIndeterminate: false,
                progressPercent: 5));

        GenerationPreflightValidator
            .ValidateSupportedInputs(
                request.Preparation,
                request.SetupOptions,
                request.CompositionReview);

        progress.Report(
            new GenerationProgressUpdate(
                "Generation choices accepted",
                "The current detection and audio choices are supported.",
                isIndeterminate: false,
                progressPercent: 10));

        cancellationToken.ThrowIfCancellationRequested();

        progress.Report(
            new GenerationProgressUpdate(
                "Confirmed video layouts accepted",
                "Every prepared source has a matching user-confirmed layout.",
                isIndeterminate: false,
                progressPercent: 15));

        GenerationPreflightValidator.ValidateEvidence(
            request.Preparation,
            request.CompositionReview,
            request.EvidenceAnalysis);

        cancellationToken.ThrowIfCancellationRequested();

        progress.Report(
            new GenerationProgressUpdate(
                "Deterministic evidence ready",
                BuildEvidenceSummary(
                    request.EvidenceAnalysis),
                isIndeterminate: false,
                progressPercent: 20));
    }

    private static string BuildEvidenceSummary(
        GenerationEvidenceAnalysisResult evidence)
    {
        return evidence.Sources.Count == 1
            ? "Full-frame, confirmed-region, and global-audio evidence is retained for 1 source."
            : $"Full-frame, confirmed-region, and global-audio evidence is retained for {evidence.Sources.Count} sources.";
    }
}
