namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public enum VisualSemanticExecutionTimingSource
{
    TorchCodecFrameBatchActualPtsAndDuration = 0,
}

public enum VisualSemanticExecutionTimingWarningCode
{
    InferredTimestampDrift = 0,
    ContainerDurationExceedsVideoStreamEnd = 1,
}

public sealed record VisualSemanticExecutionTimingCoveragePolicy
{
    internal VisualSemanticExecutionTimingCoveragePolicy(
        string version,
        double frozenSamplingFramesPerSecond,
        double frozenSamplingIntervalSeconds,
        int minimumDistinctCandidateFrames,
        string candidateIntervalSemantics,
        string reviewFrameIntervalTolerance,
        string candidateEdgeDistanceTolerance,
        string inferredTimestampUse,
        string inferredActualDriftWarningTolerance,
        double containerTimestampResolutionToleranceSeconds,
        bool candidateMutationPermitted)
    {
        if (!double.IsFinite(frozenSamplingFramesPerSecond) ||
            frozenSamplingFramesPerSecond <= 0 ||
            !double.IsFinite(frozenSamplingIntervalSeconds) ||
            frozenSamplingIntervalSeconds <= 0 ||
            minimumDistinctCandidateFrames <= 0 ||
            !double.IsFinite(containerTimestampResolutionToleranceSeconds) ||
            containerTimestampResolutionToleranceSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frozenSamplingFramesPerSecond));
        }

        Version = VisualSemanticContractText.Required(
            version,
            nameof(version),
            128);
        FrozenSamplingFramesPerSecond = frozenSamplingFramesPerSecond;
        FrozenSamplingIntervalSeconds = frozenSamplingIntervalSeconds;
        MinimumDistinctCandidateFrames = minimumDistinctCandidateFrames;
        CandidateIntervalSemantics = VisualSemanticContractText.Required(
            candidateIntervalSemantics,
            nameof(candidateIntervalSemantics),
            128);
        ReviewFrameIntervalTolerance = VisualSemanticContractText.Required(
            reviewFrameIntervalTolerance,
            nameof(reviewFrameIntervalTolerance),
            128);
        CandidateEdgeDistanceTolerance = VisualSemanticContractText.Required(
            candidateEdgeDistanceTolerance,
            nameof(candidateEdgeDistanceTolerance),
            128);
        InferredTimestampUse = VisualSemanticContractText.Required(
            inferredTimestampUse,
            nameof(inferredTimestampUse),
            128);
        InferredActualDriftWarningTolerance =
            VisualSemanticContractText.Required(
                inferredActualDriftWarningTolerance,
                nameof(inferredActualDriftWarningTolerance),
                128);
        ContainerTimestampResolutionToleranceSeconds =
            containerTimestampResolutionToleranceSeconds;
        CandidateMutationPermitted = candidateMutationPermitted;
    }

    public string Version { get; }
    public double FrozenSamplingFramesPerSecond { get; }
    public double FrozenSamplingIntervalSeconds { get; }
    public int MinimumDistinctCandidateFrames { get; }
    public string CandidateIntervalSemantics { get; }
    public string ReviewFrameIntervalTolerance { get; }
    public string CandidateEdgeDistanceTolerance { get; }
    public string InferredTimestampUse { get; }
    public string InferredActualDriftWarningTolerance { get; }
    public double ContainerTimestampResolutionToleranceSeconds { get; }
    public bool CandidateMutationPermitted { get; }
}
