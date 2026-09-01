namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed record Qwen3VlActualPtsCandidateVisibility(
    int[] IntersectingFrameIndices,
    double[] IntersectingPts,
    double[] SampledPtsInsideCandidate,
    double? NearestStartDistance,
    double? NearestEndDistance,
    double? MaximumGap,
    bool HasAtLeastTwo,
    bool BeginningSupportable,
    bool OutcomeSupportable,
    double SamplingInterval,
    double SourceFrameTolerance,
    string[] Warnings);

internal sealed record Qwen3VlActualPtsReviewCoverage(
    bool RequestedTrimHonored,
    bool AllPtsInside,
    bool AllIntervalsInside,
    double? FirstRelation,
    double? LastRelation,
    double? SourceBegin,
    double? SourceEnd,
    bool SourceTimestampOriginNonZero,
    bool VariableDurations,
    bool AverageFpsValid);

internal sealed record Qwen3VlActualPtsSourceTimeline(
    bool ReviewOutsideSource,
    bool CandidateInsideSource,
    bool ContainerTailWithinTolerance);

internal sealed record Qwen3VlActualPtsDrift(
    double[] PerFrameSeconds,
    double MaximumAbsoluteSeconds,
    double MeanAbsoluteSeconds,
    double WarningToleranceSeconds,
    bool WarningRequired);

internal static class Qwen3VlActualPtsCoverageCalculator
{
    public const double ComparisonEpsilon = 1e-9;
    public const double
        ContainerTimestampResolutionToleranceSeconds = 0.001;

    public static Qwen3VlActualPtsCandidateVisibility
        CalculateCandidateVisibility(
            IReadOnlyList<int> indices,
            IReadOnlyList<double> pts,
            IReadOnlyList<double> durations,
            double candidateStart,
            double candidateEnd,
            double samplingFramesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(pts);
        ArgumentNullException.ThrowIfNull(durations);
        RequireParallelFrameArrays(
            indices,
            pts,
            durations);

        int[] positions =
            pts.Zip(
                    durations,
                    (value, duration) =>
                        value < candidateEnd &&
                        value + duration > candidateStart)
                .Select(
                    (intersects, index) =>
                        (intersects, index))
                .Where(
                    static item => item.intersects)
                .Select(
                    static item => item.index)
                .ToArray();
        double[] intersectingPts =
            positions
                .Select(index => Round9(pts[index]))
                .ToArray();
        double[] sampledInside =
            pts.Where(
                    value =>
                        value >= candidateStart &&
                        value < candidateEnd)
                .Select(Round9)
                .ToArray();
        double[] ends =
            pts.Zip(
                    durations,
                    static (value, duration) =>
                        value + duration)
                .ToArray();
        double? startDistance =
            pts.Count == 0
                ? null
                : Round9(
                    pts.Min(
                        value =>
                            Math.Abs(
                                value - candidateStart)));
        double? endDistance =
            ends.Length == 0
                ? null
                : Round9(
                    ends.Min(
                        value =>
                            Math.Abs(
                                value - candidateEnd)));
        var aroundPositions =
            new List<int>(positions);
        int[] before =
            pts.Select(
                    (value, index) =>
                        (value, index))
                .Where(
                    item =>
                        item.value < candidateStart)
                .Select(static item => item.index)
                .ToArray();
        int[] after =
            pts.Select(
                    (value, index) =>
                        (value, index))
                .Where(
                    item =>
                        item.value >= candidateEnd)
                .Select(static item => item.index)
                .ToArray();

        if (before.Length > 0)
        {
            aroundPositions.Add(before[^1]);
        }

        if (after.Length > 0)
        {
            aroundPositions.Add(after[0]);
        }

        double[] around =
            aroundPositions
                .Select(index => Round9(pts[index]))
                .Distinct()
                .Order()
                .ToArray();
        double? maximumGap =
            around.Length < 2
                ? null
                : Round9(
                    around.Zip(
                            around.Skip(1),
                            static (first, second) =>
                                second - first)
                        .Max());
        int distinct =
            positions
                .Select(index => Round9(pts[index]))
                .Distinct()
                .Count();
        bool hasTwo = distinct >= 2;
        double tolerance =
            durations.Count == 0
                ? 0
                : durations.Max();
        double interval =
            1.0 / samplingFramesPerSecond;
        double allowed = interval + tolerance;
        bool beginning =
            hasTwo &&
            startDistance.HasValue &&
            startDistance.Value <=
                allowed + ComparisonEpsilon;
        bool outcome =
            hasTwo &&
            endDistance.HasValue &&
            endDistance.Value <=
                allowed + ComparisonEpsilon;
        var warnings = new List<string>();

        if (positions.Length == 0)
        {
            warnings.Add("CandidateHasNoSampledFrame");
        }
        else if (!hasTwo)
        {
            warnings.Add("CandidateHasOnlyOneSampledFrame");
        }

        if (!beginning)
        {
            warnings.Add("CandidateStartCoverageInsufficient");
        }

        if (!outcome)
        {
            warnings.Add("CandidateEndCoverageInsufficient");
        }

        return new Qwen3VlActualPtsCandidateVisibility(
            positions.Select(index => indices[index]).ToArray(),
            intersectingPts,
            sampledInside,
            startDistance,
            endDistance,
            maximumGap,
            hasTwo,
            beginning,
            outcome,
            interval,
            Round9(tolerance),
            warnings.ToArray());
    }

    public static Qwen3VlActualPtsReviewCoverage
        CalculateReviewCoverage(
            IReadOnlyList<double> pts,
            IReadOnlyList<double> durations,
            double reviewStart,
            double reviewEnd,
            double? sourceBegin,
            double? sourceEnd,
            double samplingFramesPerSecond,
            double maximumDrift)
    {
        ArgumentNullException.ThrowIfNull(pts);
        ArgumentNullException.ThrowIfNull(durations);

        if (pts.Count != durations.Count)
        {
            throw new ArgumentException(
                "Actual PTS and duration arrays must have equal cardinality.");
        }

        double[] ends =
            pts.Zip(
                    durations,
                    static (value, duration) =>
                        value + duration)
                .ToArray();
        double tolerance =
            durations.Count == 0
                ? 0
                : durations.Max();
        bool allPts =
            pts.Count > 0 &&
            pts.All(
                value =>
                    value >=
                        reviewStart - tolerance -
                        ComparisonEpsilon &&
                    value <
                        reviewEnd + tolerance +
                        ComparisonEpsilon);
        bool allIntervals =
            ends.Length > 0 &&
            pts.Zip(
                    ends,
                    (value, frameEnd) =>
                        value >=
                            reviewStart - tolerance -
                            ComparisonEpsilon &&
                        frameEnd <=
                            reviewEnd + tolerance +
                            ComparisonEpsilon)
                .All(static value => value);
        double? firstRelation =
            pts.Count == 0
                ? null
                : Round9(pts[0] - reviewStart);
        double? lastRelation =
            ends.Length == 0
                ? null
                : Round9(ends[^1] - reviewEnd);
        double allowed =
            (1.0 / samplingFramesPerSecond) +
            tolerance;
        bool trim =
            allPts &&
            allIntervals &&
            firstRelation.HasValue &&
            lastRelation.HasValue &&
            firstRelation.Value <=
                allowed + ComparisonEpsilon &&
            lastRelation.Value >=
                -allowed - ComparisonEpsilon;
        double durationVariation =
            durations.Count == 0
                ? 0
                : durations.Max() - durations.Min();

        return new Qwen3VlActualPtsReviewCoverage(
            trim,
            allPts,
            allIntervals,
            firstRelation,
            lastRelation,
            sourceBegin,
            sourceEnd,
            sourceBegin.HasValue &&
            Math.Abs(sourceBegin.Value) >
                ComparisonEpsilon,
            durationVariation > ComparisonEpsilon,
            maximumDrift <=
                tolerance + ComparisonEpsilon);
    }

    public static Qwen3VlActualPtsSourceTimeline
        CalculateSourceTimeline(
            double reviewStart,
            double reviewEnd,
            double candidateStart,
            double candidateEnd,
            double? sourceBegin,
            double? sourceEnd,
            double sourceFrameTolerance,
            double sourceAverageFramesPerSecond)
    {
        if (!sourceBegin.HasValue ||
            !sourceEnd.HasValue)
        {
            return new Qwen3VlActualPtsSourceTimeline(
                ReviewOutsideSource: false,
                CandidateInsideSource: true,
                ContainerTailWithinTolerance: false);
        }

        bool startOutside =
            reviewStart <
            sourceBegin.Value -
            sourceFrameTolerance -
            ComparisonEpsilon;
        bool endOutside =
            reviewEnd >
            sourceEnd.Value +
            sourceFrameTolerance +
            ComparisonEpsilon;
        bool candidateInside =
            candidateStart >=
                sourceBegin.Value -
                sourceFrameTolerance -
                ComparisonEpsilon &&
            candidateEnd <=
                sourceEnd.Value +
                sourceFrameTolerance +
                ComparisonEpsilon;
        double nominalFramePeriod =
            double.IsFinite(sourceAverageFramesPerSecond) &&
            sourceAverageFramesPerSecond > 0
                ? 1.0 / sourceAverageFramesPerSecond
                : sourceFrameTolerance;
        double containerTailTolerance =
            Math.Max(
                sourceFrameTolerance,
                nominalFramePeriod) +
            ContainerTimestampResolutionToleranceSeconds;
        bool containerTailWithinTolerance =
            !startOutside &&
            endOutside &&
            reviewEnd <=
                sourceEnd.Value +
                containerTailTolerance +
                ComparisonEpsilon;

        return new Qwen3VlActualPtsSourceTimeline(
            startOutside || endOutside,
            candidateInside,
            containerTailWithinTolerance);
    }

    public static Qwen3VlActualPtsDrift CalculateDrift(
        IReadOnlyList<double> inferred,
        IReadOnlyList<double> actual,
        IReadOnlyList<double> durations)
    {
        ArgumentNullException.ThrowIfNull(inferred);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(durations);

        if (inferred.Count == 0 ||
            inferred.Count != actual.Count ||
            actual.Count != durations.Count)
        {
            throw new ArgumentException(
                "Inferred timestamps, actual PTS, and durations must have equal nonzero cardinality.");
        }

        double[] perFrame =
            inferred.Zip(
                    actual,
                    static (nominal, pts) =>
                        Round9(nominal - pts))
                .ToArray();
        double maximum =
            Round9(
                perFrame.Max(
                    static value =>
                        Math.Abs(value)));
        double mean =
            Round9(
                perFrame.Average(
                    static value =>
                        Math.Abs(value)));
        double tolerance =
            Round9(durations.Max());

        return new Qwen3VlActualPtsDrift(
            perFrame,
            maximum,
            mean,
            tolerance,
            maximum >
                tolerance + ComparisonEpsilon);
    }

    public static bool StrictlyIncreasing(
        IReadOnlyList<int> values)
    {
        for (int index = 1;
             index < values.Count;
             index++)
        {
            if (values[index] <= values[index - 1])
            {
                return false;
            }
        }

        return true;
    }

    public static bool StrictlyIncreasing(
        IReadOnlyList<double> values)
    {
        for (int index = 1;
             index < values.Count;
             index++)
        {
            if (values[index] <= values[index - 1])
            {
                return false;
            }
        }

        return true;
    }

    public static double Round9(double value) =>
        Math.Round(
            value,
            9,
            MidpointRounding.ToEven);

    private static void RequireParallelFrameArrays(
        IReadOnlyList<int> indices,
        IReadOnlyList<double> pts,
        IReadOnlyList<double> durations)
    {
        if (indices.Count != pts.Count ||
            pts.Count != durations.Count)
        {
            throw new ArgumentException(
                "Frame index, actual PTS, and duration arrays must have equal cardinality.");
        }
    }
}
