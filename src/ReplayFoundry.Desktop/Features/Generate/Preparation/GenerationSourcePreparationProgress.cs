namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public sealed class GenerationSourcePreparationProgress
{
    public GenerationSourcePreparationProgress(
        string phase,
        string detail,
        double progressPercent,
        string? sourceName = null,
        int? sourceNumber = null,
        int? sourceCount = null)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            throw new ArgumentException(
                "Source-preparation progress requires a phase.",
                nameof(phase));
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException(
                "Source-preparation progress requires details.",
                nameof(detail));
        }

        if (!double.IsFinite(progressPercent) ||
            progressPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(progressPercent),
                progressPercent,
                "Source-preparation progress must be between zero and 100.");
        }

        bool hasSourceNumber =
            sourceNumber is not null;

        bool hasSourceCount =
            sourceCount is not null;

        if (hasSourceNumber != hasSourceCount)
        {
            throw new ArgumentException(
                "Source number and source count must be supplied together.");
        }

        if (hasSourceNumber)
        {
            if (sourceNumber <= 0 ||
                sourceCount <= 0 ||
                sourceNumber > sourceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceNumber),
                    "Source progress indices must be positive and within the source count.");
            }

            if (string.IsNullOrWhiteSpace(sourceName))
            {
                throw new ArgumentException(
                    "Indexed source progress requires a source name.",
                    nameof(sourceName));
            }
        }
        else if (!string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException(
                "A source name requires a source number and source count.",
                nameof(sourceName));
        }

        Phase = phase.Trim();
        Detail = detail.Trim();
        ProgressPercent = progressPercent;
        SourceName =
            string.IsNullOrWhiteSpace(sourceName)
                ? null
                : sourceName.Trim();
        SourceNumber = sourceNumber;
        SourceCount = sourceCount;
    }

    public string Phase { get; }

    public string Detail { get; }

    public double ProgressPercent { get; }

    public string? SourceName { get; }

    public int? SourceNumber { get; }

    public int? SourceCount { get; }
}
