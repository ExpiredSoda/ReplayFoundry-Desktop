namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

public enum GenerationEvidenceAnalysisPhase
{
    PreparingAnalysis,
    StudyingSceneChanges,
    CheckingDarkAndFrozenSections,
    ListeningForQuietSections,
    FinishingSourceEvidence,
    SourceEvidenceComplete,
    UsingSavedEvidence,
    BatchComplete,
}

public sealed class GenerationEvidenceAnalysisProgress
{
    public GenerationEvidenceAnalysisProgress(
        GenerationEvidenceAnalysisPhase phase,
        string title,
        string detail,
        string? sourceFileName,
        int? sourceNumber,
        int? sourceCount,
        int? audioStreamIndex,
        bool isIndeterminate,
        double? overallPercentage)
    {
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(
                nameof(phase),
                phase,
                "The generation evidence-analysis phase is not defined.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "Evidence-analysis progress requires a title.",
                nameof(title));
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException(
                "Evidence-analysis progress requires details.",
                nameof(detail));
        }

        bool hasSourcePosition =
            sourceNumber is not null ||
            sourceCount is not null;

        if (hasSourcePosition)
        {
            if (sourceNumber is null ||
                sourceCount is null ||
                sourceNumber <= 0 ||
                sourceCount <= 0 ||
                sourceNumber > sourceCount ||
                string.IsNullOrWhiteSpace(sourceFileName))
            {
                throw new ArgumentException(
                    "Indexed evidence progress requires a valid source name, number, and count.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            throw new ArgumentException(
                "A source filename requires a source number and count.",
                nameof(sourceFileName));
        }

        if (audioStreamIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioStreamIndex),
                audioStreamIndex,
                "An audio stream index cannot be negative.");
        }

        if (isIndeterminate &&
            overallPercentage is not null)
        {
            throw new ArgumentException(
                "Indeterminate evidence progress cannot include a percentage.",
                nameof(overallPercentage));
        }

        if (overallPercentage is not null &&
            (!double.IsFinite(overallPercentage.Value) ||
             overallPercentage is < 0 or > 100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(overallPercentage),
                overallPercentage,
                "Evidence progress must be between zero and 100 percent.");
        }

        Phase = phase;
        Title = title.Trim();
        Detail = detail.Trim();
        SourceFileName =
            string.IsNullOrWhiteSpace(sourceFileName)
                ? null
                : sourceFileName.Trim();
        SourceNumber = sourceNumber;
        SourceCount = sourceCount;
        AudioStreamIndex = audioStreamIndex;
        IsIndeterminate = isIndeterminate;
        OverallPercentage = overallPercentage;
    }

    public GenerationEvidenceAnalysisPhase Phase { get; }

    public string Title { get; }

    public string Detail { get; }

    public string? SourceFileName { get; }

    public int? SourceNumber { get; }

    public int? SourceCount { get; }

    public int? AudioStreamIndex { get; }

    public bool IsIndeterminate { get; }

    public double? OverallPercentage { get; }
}
