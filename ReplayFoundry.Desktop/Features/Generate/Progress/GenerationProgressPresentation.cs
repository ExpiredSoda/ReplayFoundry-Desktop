using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;

namespace ReplayFoundry.Desktop.Features.Generate.Progress;

internal sealed record GenerationProgressPresentation(
    GenerationProgressState State,
    string Title,
    string Detail,
    string ModeDisplayName,
    string SourceSummary,
    string? SourceProgressText,
    string? ErrorMessage,
    string? TechnicalDetails,
    string? CompletionSummary,
    double ProgressPercent,
    bool IsIndeterminate,
    bool IsCancellationRequested,
    string CancelButtonLabel);

internal sealed record GenerationProgressRunContext(
    string ModeDisplayName,
    string SourceSummary,
    string CancelButtonLabel);

internal static class GenerationProgressPresentationFactory
{
    public static GenerationProgressPresentation Reset() =>
        new(
            GenerationProgressState.Idle,
            "Getting ready",
            "Preparing the generation workflow.",
            string.Empty,
            string.Empty,
            null,
            null,
            null,
            null,
            0,
            IsIndeterminate: true,
            IsCancellationRequested: false,
            "Cancel Generation");

    public static GenerationProgressPresentation BeginPreparation(
        GenerationMode mode,
        int sourceCount) =>
        Running(
            mode,
            sourceCount,
            "Cancel Preparation",
            "Preparing your videos",
            "Checking each selected source before Generation Setup.",
            isIndeterminate: false);

    public static GenerationProgressPresentation BeginEvidenceAnalysis(
        GenerationMode mode,
        int sourceCount) =>
        Running(
            mode,
            sourceCount,
            "Cancel Analysis",
            "Studying your videos",
            "Replay Foundry is gathering deterministic evidence from the full video and confirmed regions. Long recordings can take several minutes.",
            isIndeterminate: true);

    public static GenerationProgressPresentation BeginGeneration(
        GenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Running(
            request.Mode,
            request.SourceCount,
            "Cancel Generation",
            "Getting ready",
            "Preparing your selected videos for clip detection.",
            isIndeterminate: true);
    }

    public static GenerationProgressPresentation Complete(
        GenerationResult result,
        GenerationProgressRunContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        string outputSummary = result.CandidateCount == 1
            ? "1 editable moment is ready in Studio."
            : $"{result.CandidateCount} editable moments are ready in Studio.";
        string completionSummary = result.Moments.IsRequestedCountMet
            ? outputSummary
            : outputSummary + Environment.NewLine +
              result.Moments.FulfillmentMessage;

        return new GenerationProgressPresentation(
            GenerationProgressState.Completed,
            "Your Studio project is ready",
            "Replay Foundry finished selecting moments. No final video was rendered; complete your edits in Studio when you are ready.",
            context.ModeDisplayName,
            context.SourceSummary,
            null,
            null,
            null,
            completionSummary,
            100,
            IsIndeterminate: false,
            IsCancellationRequested: false,
            context.CancelButtonLabel);
    }

    public static GenerationProgressPresentation Failure(
        string title,
        string friendlyMessage,
        Exception exception,
        GenerationProgressRunContext context,
        double progressPercent = 0)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "A progress failure requires a title.",
                nameof(title));
        }

        if (string.IsNullOrWhiteSpace(friendlyMessage))
        {
            throw new ArgumentException(
                "A progress failure requires a friendly message.",
                nameof(friendlyMessage));
        }

        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);
        return new GenerationProgressPresentation(
            GenerationProgressState.Failed,
            title,
            friendlyMessage,
            context.ModeDisplayName,
            context.SourceSummary,
            null,
            friendlyMessage,
            exception.ToString(),
            null,
            progressPercent,
            IsIndeterminate: false,
            IsCancellationRequested: false,
            context.CancelButtonLabel);
    }

    public static GenerationProgressPresentation Cancelled(
        string title,
        string detail,
        string completionSummary,
        GenerationProgressRunContext context)
    {
        if (string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(detail) ||
            string.IsNullOrWhiteSpace(completionSummary))
        {
            throw new ArgumentException(
                "A cancellation presentation requires complete user-facing copy.");
        }

        ArgumentNullException.ThrowIfNull(context);
        return new GenerationProgressPresentation(
            GenerationProgressState.Cancelled,
            title,
            detail,
            context.ModeDisplayName,
            context.SourceSummary,
            null,
            null,
            null,
            completionSummary,
            0,
            IsIndeterminate: false,
            IsCancellationRequested: false,
            context.CancelButtonLabel);
    }

    public static string? FormatSourceProgress(
        int? sourceNumber,
        int? sourceCount,
        string? sourceName) =>
        sourceNumber is not null &&
        sourceCount is not null &&
        !string.IsNullOrWhiteSpace(sourceName)
            ? $"Video {sourceNumber} of {sourceCount}: {sourceName}"
            : null;

    private static GenerationProgressPresentation Running(
        GenerationMode mode,
        int sourceCount,
        string cancelButtonLabel,
        string title,
        string detail,
        bool isIndeterminate)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The generation mode is not defined.");
        }

        if (sourceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceCount),
                sourceCount,
                "Progress requires at least one source.");
        }

        return new GenerationProgressPresentation(
            GenerationProgressState.Running,
            title,
            detail,
            ModeDisplayName(mode),
            sourceCount == 1
                ? "1 source video"
                : $"{sourceCount} source videos",
            null,
            null,
            null,
            null,
            0,
            isIndeterminate,
            IsCancellationRequested: false,
            cancelButtonLabel);
    }

    private static string ModeDisplayName(GenerationMode mode) =>
        mode switch
        {
            GenerationMode.IndividualClips => "Individual Clips",
            GenerationMode.Montage => "Montage",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
}
