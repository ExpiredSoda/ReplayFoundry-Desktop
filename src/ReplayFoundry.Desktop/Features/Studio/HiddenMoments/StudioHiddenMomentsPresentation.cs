using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Studio.HiddenMoments;

public enum StudioHiddenMomentAcceptanceStage
{
    None,
    PreparingCaptions,
    PreparingTitleAndDescription,
    AddingToStudio,
}

public sealed class StudioHiddenMomentAcceptedEventArgs : EventArgs
{
    public StudioHiddenMomentAcceptedEventArgs(
        string candidateId,
        bool shouldFocus = false)
    {
        CandidateId = candidateId;
        ShouldFocus = shouldFocus;
    }

    public string CandidateId { get; }
    public bool ShouldFocus { get; }
}

internal static class StudioHiddenMomentsPresentation
{
    internal static IReadOnlyList<string> NotificationPropertyNames { get; } =
        Array.AsReadOnly(
        [
            nameof(StudioHiddenMomentsViewModel.Current),
            nameof(StudioHiddenMomentsViewModel.IsOpen),
            nameof(StudioHiddenMomentsViewModel.HasAvailableMoments),
            nameof(StudioHiddenMomentsViewModel.HasQueueItems),
            nameof(StudioHiddenMomentsViewModel.HasUnfinishedQueueItems),
            nameof(StudioHiddenMomentsViewModel.HasCancelableQueueItems),
            nameof(StudioHiddenMomentsViewModel.IsExhausted),
            nameof(StudioHiddenMomentsViewModel.RemainingCount),
            nameof(StudioHiddenMomentsViewModel.ReviewedCount),
            nameof(StudioHiddenMomentsViewModel.SessionTotal),
            nameof(StudioHiddenMomentsViewModel.QueueItems),
            nameof(StudioHiddenMomentsViewModel.QueueSummaryText),
            nameof(StudioHiddenMomentsViewModel.OpenButtonText),
            nameof(StudioHiddenMomentsViewModel.ProgressText),
            nameof(StudioHiddenMomentsViewModel.MomentTitle),
            nameof(StudioHiddenMomentsViewModel.MomentDetail),
            nameof(StudioHiddenMomentsViewModel.EvidenceText),
            nameof(StudioHiddenMomentsViewModel.Error),
            nameof(StudioHiddenMomentsViewModel.HasError),
            nameof(StudioHiddenMomentsViewModel.IsPreparingAcceptedMoment),
            nameof(StudioHiddenMomentsViewModel.AcceptanceStage),
            nameof(StudioHiddenMomentsViewModel.AcceptanceStatus),
            nameof(StudioHiddenMomentsViewModel.IsAcceptanceProgressIndeterminate),
            nameof(StudioHiddenMomentsViewModel.AcceptanceProgressPercentage),
            nameof(StudioHiddenMomentsViewModel.IsAcceptanceLivenessVisible),
            nameof(StudioHiddenMomentsViewModel.AcceptanceLivenessText),
            nameof(StudioHiddenMomentsViewModel.IsAcceptanceTakingLong),
            nameof(StudioHiddenMomentsViewModel.AcceptanceWaitGuidance),
            nameof(StudioHiddenMomentsViewModel.CloseButtonText),
            nameof(StudioHiddenMomentsViewModel.CloseButtonAutomationName),
            nameof(StudioHiddenMomentsViewModel.CloseButtonHelpText),
            nameof(StudioHiddenMomentsViewModel.IsProjectMutationBlocked),
        ]);

    internal static string OpenButtonText(
        int failureCount,
        bool hasQueueItems,
        bool hasAvailableMoments,
        int remainingCount,
        int workCount) => failureCount > 0
            ? failureCount == 1
                ? "Review moments · 1 needs attention"
                : $"Review moments · {failureCount} need attention"
            : hasQueueItems && hasAvailableMoments
                ? $"Review {remainingCount} moments · {workCount} queued"
                : hasQueueItems
                    ? $"View {workCount} queued moments"
                    : hasAvailableMoments
                        ? $"Review {remainingCount} alternate moments"
                        : "No hidden moments remaining";

    internal static string ProgressText(
        GenerationHiddenMoment? current,
        bool hasQueueItems,
        string queueSummaryText,
        int reviewedCount,
        int sessionTotal) => current is null
            ? hasQueueItems
                ? queueSummaryText
                : $"Reviewed {reviewedCount} of {sessionTotal}"
            : $"Moment {current.ReviewOrder} of {sessionTotal}";

    internal static string MomentTitle(
        GenerationHiddenMoment? current,
        bool hasQueueItems,
        int failureCount) => current is null
            ? hasQueueItems
                ? failureCount > 0
                    ? "A queued moment needs attention"
                    : "Your moments are being added"
                : "You reviewed every hidden moment"
            : current.SourceName;

    internal static string MomentDetail(
        GenerationHiddenMoment? current,
        bool hasQueueItems) => current is null
            ? hasQueueItems
                ? "You can close this review while queued moments keep preparing in the background."
                : "Accepted moments are now in Studio. Skipped moments remain separate from Like, Neutral, and Dislike feedback."
            : $"{MediaTimeFormatter.Format(current.SourceStart)}–" +
              $"{MediaTimeFormatter.Format(current.SourceEnd)} · " +
              $"{MediaTimeFormatter.Format(current.Duration)} · " +
              current.ReviewReason;

    internal static string EvidenceText(
        GenerationHiddenMoment? current,
        bool hasQueueItems) => current?.Explanation ??
            (hasQueueItems
                ? "Queued moments appear in the Studio browser only after their captions, title, and description are ready."
                : "Nothing else is waiting in this review session.");

    internal static StudioHiddenMomentAcceptanceStage AcceptanceStage(
        StudioHiddenMomentQueueItem? activeItem) => activeItem?.State switch
        {
            StudioHiddenMomentQueueItemState.PreparingCaptions =>
                StudioHiddenMomentAcceptanceStage.PreparingCaptions,
            StudioHiddenMomentQueueItemState.PreparingTitleAndDescription =>
                StudioHiddenMomentAcceptanceStage.PreparingTitleAndDescription,
            StudioHiddenMomentQueueItemState.AddingToStudio =>
                StudioHiddenMomentAcceptanceStage.AddingToStudio,
            _ => StudioHiddenMomentAcceptanceStage.None,
        };

    internal static string AcceptanceStatus(
        StudioHiddenMomentAcceptanceStage stage) => stage switch
        {
            StudioHiddenMomentAcceptanceStage.PreparingCaptions =>
                "Preparing captions and transcript…",
            StudioHiddenMomentAcceptanceStage.PreparingTitleAndDescription =>
                "Preparing the title and description…",
            StudioHiddenMomentAcceptanceStage.AddingToStudio =>
                "Adding the prepared clip to Studio…",
            _ => string.Empty,
        };

    internal static string QueueSummaryText(
        IEnumerable<StudioHiddenMomentQueueItem> queueItems)
    {
        StudioHiddenMomentQueueItem[] items = queueItems.ToArray();
        int active = items.Count(static item => item.IsActive);
        int waiting = items.Count(static item =>
            item.State == StudioHiddenMomentQueueItemState.Waiting);
        int failed = items.Count(static item =>
            item.State == StudioHiddenMomentQueueItemState.NeedsAttention);
        var parts = new List<string>(3);
        if (active > 0)
        {
            parts.Add("1 preparing");
        }
        if (waiting > 0)
        {
            parts.Add($"{waiting} waiting");
        }
        if (failed == 1)
        {
            parts.Add("1 needs attention");
        }
        else if (failed > 1)
        {
            parts.Add($"{failed} need attention");
        }
        return parts.Count == 0
            ? "Queue is clear"
            : string.Join(" · ", parts);
    }

    internal static string CloseButtonHelpText(bool hasQueueItems) =>
        hasQueueItems
            ? "Closes this review. Queued moments keep preparing while Replay Foundry is open."
            : "Closes Hidden Moments review.";
}
