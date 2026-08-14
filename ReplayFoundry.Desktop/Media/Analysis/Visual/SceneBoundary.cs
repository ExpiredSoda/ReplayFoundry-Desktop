using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Visual;

public sealed class SceneBoundary
{
    public SceneBoundary(
        TimeSpan timestamp,
        double? scorePercent,
        double? meanAbsoluteFrameDifference)
    {
        if (timestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                timestamp,
                "Scene boundary timestamp cannot be negative.");
        }

        if (scorePercent is double score &&
            (!double.IsFinite(score) ||
             score is < 0 or > 100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scorePercent),
                scorePercent,
                "Scene score must be between 0 and 100 when supplied.");
        }

        if (meanAbsoluteFrameDifference is double difference &&
            (!double.IsFinite(difference) ||
             difference < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(meanAbsoluteFrameDifference),
                meanAbsoluteFrameDifference,
                "Mean absolute frame difference cannot be negative.");
        }

        Timestamp = timestamp;
        ScorePercent = scorePercent;
        MeanAbsoluteFrameDifference =
            meanAbsoluteFrameDifference;
    }

    public TimeSpan Timestamp { get; }

    public double? ScorePercent { get; }

    public double? MeanAbsoluteFrameDifference { get; }
}
