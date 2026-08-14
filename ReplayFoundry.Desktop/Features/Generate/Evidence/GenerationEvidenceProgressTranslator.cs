using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Analysis;

namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

internal static class GenerationEvidenceProgressTranslator
{
    public static void Translate(
        IProgress<GenerationEvidenceAnalysisProgress>? progress,
        MediaEvidenceProgressUpdate update,
        PreparedGenerationSource source,
        int sourceNumber,
        int sourceCount)
    {
        if (progress is null)
        {
            return;
        }

        int totalPasses = 2 + source.Media.AudioStreams.Count;
        switch (update.Phase)
        {
            case MediaEvidenceAnalysisPhase.Preparing:
                progress.Report(Create(
                    GenerationEvidenceAnalysisPhase.PreparingAnalysis,
                    "Preparing analysis",
                    "Replay Foundry is preparing the full frame and confirmed video regions.",
                    source,
                    sourceNumber,
                    sourceCount,
                    isIndeterminate: true));
                return;

            case MediaEvidenceAnalysisPhase.ScenePassStarted:
                ReportActive(
                    progress,
                    GenerationEvidenceAnalysisPhase.StudyingSceneChanges,
                    "Studying scene changes",
                    "Looking for visual transitions across the full video and confirmed regions.",
                    source,
                    sourceNumber,
                    sourceCount);
                return;

            case MediaEvidenceAnalysisPhase.ScenePassCompleted:
                ReportBoundary(
                    progress,
                    GenerationEvidenceAnalysisPhase.StudyingSceneChanges,
                    "Scene changes studied",
                    "The shared scene-change pass is complete.",
                    source,
                    sourceNumber,
                    sourceCount,
                    completedPasses: 1,
                    totalPasses);
                return;

            case MediaEvidenceAnalysisPhase.VisualIntervalPassStarted:
                ReportActive(
                    progress,
                    GenerationEvidenceAnalysisPhase.CheckingDarkAndFrozenSections,
                    "Reading the visual rhythm",
                    "Mapping scene changes, motion, and still stretches across the video.",
                    source,
                    sourceNumber,
                    sourceCount);
                return;

            case MediaEvidenceAnalysisPhase.VisualIntervalPassCompleted:
                ReportBoundary(
                    progress,
                    GenerationEvidenceAnalysisPhase.CheckingDarkAndFrozenSections,
                    "Dark and frozen sections checked",
                    "The shared dark/freeze pass is complete.",
                    source,
                    sourceNumber,
                    sourceCount,
                    completedPasses: 2,
                    totalPasses);
                return;

            case MediaEvidenceAnalysisPhase.AudioPassStarted:
                {
                    string streamText = update.StreamIndex is int streamIndex
                        ? $"audio stream {streamIndex}"
                        : "the current audio stream";
                    progress.Report(Create(
                        GenerationEvidenceAnalysisPhase.ListeningForQuietSections,
                        "Mapping the soundscape",
                        $"Finding quiet and active stretches in {streamText} across the video.",
                        source,
                        sourceNumber,
                        sourceCount,
                        isIndeterminate: true,
                        audioStreamIndex: update.StreamIndex));
                    return;
                }

            case MediaEvidenceAnalysisPhase.AudioPassCompleted:
                {
                    int audioOrdinal = GetAudioOrdinal(source, update.StreamIndex);
                    progress.Report(CreateBoundary(
                        GenerationEvidenceAnalysisPhase.ListeningForQuietSections,
                        "Quiet sections checked",
                        $"Global audio stream {update.StreamIndex} is complete.",
                        source,
                        sourceNumber,
                        sourceCount,
                        completedPasses: 2 + audioOrdinal,
                        totalPasses,
                        update.StreamIndex));
                    return;
                }

            case MediaEvidenceAnalysisPhase.Completed:
                progress.Report(CreateBoundary(
                    GenerationEvidenceAnalysisPhase.FinishingSourceEvidence,
                    "Finishing source evidence",
                    "All deterministic media passes for this video are complete.",
                    source,
                    sourceNumber,
                    sourceCount,
                    totalPasses,
                    totalPasses));
                return;

            default:
                throw new InvalidOperationException(
                    "The low-level evidence progress phase is not supported.");
        }
    }

    public static GenerationEvidenceAnalysisProgress CreateBoundary(
        GenerationEvidenceAnalysisPhase phase,
        string title,
        string detail,
        PreparedGenerationSource source,
        int sourceNumber,
        int sourceCount,
        int completedPasses,
        int totalPasses,
        int? audioStreamIndex = null)
    {
        double completedSourceFraction = completedPasses / (double)totalPasses;
        double overallPercentage =
            ((sourceNumber - 1) + completedSourceFraction) /
            sourceCount *
            100;
        return Create(
            phase,
            title,
            detail,
            source,
            sourceNumber,
            sourceCount,
            isIndeterminate: false,
            overallPercentage,
            audioStreamIndex);
    }

    public static GenerationEvidenceAnalysisProgress Create(
        GenerationEvidenceAnalysisPhase phase,
        string title,
        string detail,
        PreparedGenerationSource source,
        int sourceNumber,
        int sourceCount,
        bool isIndeterminate,
        double? overallPercentage = null,
        int? audioStreamIndex = null) =>
        new(
            phase,
            title,
            detail,
            source.Source.FileName,
            sourceNumber,
            sourceCount,
            audioStreamIndex,
            isIndeterminate,
            overallPercentage);

    private static int GetAudioOrdinal(
        PreparedGenerationSource source,
        int? streamIndex)
    {
        if (streamIndex is null)
        {
            throw new InvalidOperationException(
                "Completed audio progress requires an absolute stream index.");
        }

        int ordinal = source.Media.AudioStreams
            .Select(static (stream, index) => (stream.Index, Ordinal: index + 1))
            .SingleOrDefault(item => item.Index == streamIndex.Value)
            .Ordinal;
        return ordinal > 0
            ? ordinal
            : throw new InvalidOperationException(
                $"Audio progress referenced unknown stream {streamIndex}.");
    }

    private static void ReportActive(
        IProgress<GenerationEvidenceAnalysisProgress> progress,
        GenerationEvidenceAnalysisPhase phase,
        string title,
        string detail,
        PreparedGenerationSource source,
        int sourceNumber,
        int sourceCount) =>
        progress.Report(Create(
            phase,
            title,
            detail,
            source,
            sourceNumber,
            sourceCount,
            isIndeterminate: true));

    private static void ReportBoundary(
        IProgress<GenerationEvidenceAnalysisProgress> progress,
        GenerationEvidenceAnalysisPhase phase,
        string title,
        string detail,
        PreparedGenerationSource source,
        int sourceNumber,
        int sourceCount,
        int completedPasses,
        int totalPasses) =>
        progress.Report(CreateBoundary(
            phase,
            title,
            detail,
            source,
            sourceNumber,
            sourceCount,
            completedPasses,
            totalPasses));
}
