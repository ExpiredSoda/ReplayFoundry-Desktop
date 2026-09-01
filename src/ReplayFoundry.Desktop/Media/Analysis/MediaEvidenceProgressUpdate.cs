using System;

namespace ReplayFoundry.Desktop.Media.Analysis;

public enum MediaEvidenceAnalysisPhase
{
    Preparing,
    ScenePassStarted,
    ScenePassCompleted,
    VisualIntervalPassStarted,
    VisualIntervalPassCompleted,
    AudioPassStarted,
    AudioPassCompleted,
    Completed,
}

public sealed class MediaEvidenceProgressUpdate
{
    public MediaEvidenceProgressUpdate(
        MediaEvidenceAnalysisPhase phase,
        string detail,
        double progressPercent,
        int? streamIndex = null)
    {
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(
                nameof(phase),
                phase,
                "The evidence-analysis phase is not defined.");
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException(
                "An evidence progress update requires details.",
                nameof(detail));
        }

        if (!double.IsFinite(progressPercent) ||
            progressPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(progressPercent),
                progressPercent,
                "Evidence progress must be between 0 and 100 percent.");
        }

        if (streamIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(streamIndex),
                streamIndex,
                "Stream index cannot be negative.");
        }

        Phase = phase;
        Detail = detail;
        ProgressPercent = progressPercent;
        StreamIndex = streamIndex;
    }

    public MediaEvidenceAnalysisPhase Phase { get; }

    public string PhaseDisplayName =>
        Phase switch
        {
            MediaEvidenceAnalysisPhase.Preparing =>
                "Preparing analysis",

            MediaEvidenceAnalysisPhase.ScenePassStarted =>
                "Studying scene changes",

            MediaEvidenceAnalysisPhase.ScenePassCompleted =>
                "Scene analysis complete",

            MediaEvidenceAnalysisPhase.VisualIntervalPassStarted =>
                "Checking dark and frozen sections",

            MediaEvidenceAnalysisPhase.VisualIntervalPassCompleted =>
                "Dark and frozen section analysis complete",

            MediaEvidenceAnalysisPhase.AudioPassStarted =>
                "Listening for quiet sections",

            MediaEvidenceAnalysisPhase.AudioPassCompleted =>
                "Quiet-section analysis complete",

            MediaEvidenceAnalysisPhase.Completed =>
                "Evidence analysis complete",

            _ => throw new InvalidOperationException(
                "The evidence-analysis phase is not supported."),
        };

    public string Detail { get; }

    public double ProgressPercent { get; }

    public int? StreamIndex { get; }
}
