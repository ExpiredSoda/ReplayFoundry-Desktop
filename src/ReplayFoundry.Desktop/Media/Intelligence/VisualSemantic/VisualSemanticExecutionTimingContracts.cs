using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed class VisualSemanticCaseExecutionTiming
{
    private readonly ReadOnlyCollection<int> _selectedFrameIndices;
    private readonly ReadOnlyCollection<double>
        _inferredTimestampsSeconds;
    private readonly ReadOnlyCollection<double> _actualPtsSeconds;
    private readonly ReadOnlyCollection<double>
        _actualFrameDurationsSeconds;
    private readonly ReadOnlyCollection<string>
        _qwenFinalFrameSha256;
    private readonly ReadOnlyCollection<string>
        _directCompatibleFrameSha256;
    private readonly ReadOnlyCollection<
        VisualSemanticExecutionTimingWarningCode> _warningCodes;

    internal VisualSemanticCaseExecutionTiming(
        string caseId,
        string candidateId,
        int caseOrdinal,
        string reviewVideoSha256,
        double requestedAbsoluteReviewStartSeconds,
        double requestedAbsoluteReviewEndSeconds,
        double candidateAbsoluteStartSeconds,
        double candidateAbsoluteEndSeconds,
        double? sourceBeginStreamSeconds,
        double? sourceEndStreamSeconds,
        double sourceAverageFramesPerSecond,
        IEnumerable<int> selectedFrameIndices,
        IEnumerable<double> inferredTimestampsSeconds,
        IEnumerable<double> actualPtsSeconds,
        IEnumerable<double> actualFrameDurationsSeconds,
        string qwenFinalTensorSha256,
        IEnumerable<string> qwenFinalFrameSha256,
        string directCompatibleTensorSha256,
        IEnumerable<string> directCompatibleFrameSha256,
        bool compatibleTensorIdentityEqual,
        bool compatibleFrameIdentityEqual,
        int candidateIntersectingFrameCount,
        bool hasAtLeastTwoTemporallyDistinctFrames,
        bool beginningJudgmentSupportable,
        bool outcomeJudgmentSupportable,
        double? nearestSampleDistanceToCandidateStartSeconds,
        double? nearestFrameEndDistanceToCandidateEndSeconds,
        double? maximumGapSeconds,
        bool allActualPtsInsideRequestedReview,
        bool allActualFrameIntervalsInsideRequestedReview,
        bool requestedTrimHonored,
        double maximumAbsoluteInferredPtsDriftSeconds,
        double meanAbsoluteInferredPtsDriftSeconds,
        double inferredPtsDriftWarningToleranceSeconds,
        bool containerDurationExceedsVideoStreamEnd,
        IEnumerable<VisualSemanticExecutionTimingWarningCode>
            warningCodes,
        bool passed,
        string canonicalCaseTimingSha256)
    {
        ArgumentNullException.ThrowIfNull(selectedFrameIndices);
        ArgumentNullException.ThrowIfNull(inferredTimestampsSeconds);
        ArgumentNullException.ThrowIfNull(actualPtsSeconds);
        ArgumentNullException.ThrowIfNull(actualFrameDurationsSeconds);
        ArgumentNullException.ThrowIfNull(qwenFinalFrameSha256);
        ArgumentNullException.ThrowIfNull(
            directCompatibleFrameSha256);
        ArgumentNullException.ThrowIfNull(warningCodes);

        int[] indexSnapshot = selectedFrameIndices.ToArray();
        double[] inferredSnapshot =
            inferredTimestampsSeconds.ToArray();
        double[] ptsSnapshot = actualPtsSeconds.ToArray();
        double[] durationSnapshot =
            actualFrameDurationsSeconds.ToArray();
        string[] qwenFrameHashSnapshot =
            qwenFinalFrameSha256.ToArray();
        string[] directFrameHashSnapshot =
            directCompatibleFrameSha256.ToArray();
        VisualSemanticExecutionTimingWarningCode[]
            warningSnapshot = warningCodes.ToArray();

        if (caseOrdinal <= 0 ||
            !FiniteOrderedRange(
                requestedAbsoluteReviewStartSeconds,
                requestedAbsoluteReviewEndSeconds) ||
            !FiniteOrderedRange(
                candidateAbsoluteStartSeconds,
                candidateAbsoluteEndSeconds) ||
            candidateAbsoluteStartSeconds <
                requestedAbsoluteReviewStartSeconds ||
            candidateAbsoluteEndSeconds >
                requestedAbsoluteReviewEndSeconds ||
            !FiniteNullable(sourceBeginStreamSeconds) ||
            !FiniteNullable(sourceEndStreamSeconds) ||
            sourceBeginStreamSeconds.HasValue !=
                sourceEndStreamSeconds.HasValue ||
            sourceBeginStreamSeconds.HasValue &&
            sourceEndStreamSeconds <= sourceBeginStreamSeconds ||
            !double.IsFinite(sourceAverageFramesPerSecond) ||
            sourceAverageFramesPerSecond <= 0 ||
            indexSnapshot.Length == 0 ||
            inferredSnapshot.Length != indexSnapshot.Length ||
            ptsSnapshot.Length != indexSnapshot.Length ||
            durationSnapshot.Length != indexSnapshot.Length ||
            qwenFrameHashSnapshot.Length != indexSnapshot.Length ||
            directFrameHashSnapshot.Length != indexSnapshot.Length ||
            indexSnapshot.Any(static value => value < 0) ||
            inferredSnapshot.Any(static value => !double.IsFinite(value)) ||
            ptsSnapshot.Any(static value => !double.IsFinite(value)) ||
            durationSnapshot.Any(
                static value =>
                    !double.IsFinite(value) ||
                    value <= 0) ||
            candidateIntersectingFrameCount < 0 ||
            candidateIntersectingFrameCount > indexSnapshot.Length ||
            !FiniteNullable(
                nearestSampleDistanceToCandidateStartSeconds,
                requireNonnegative: true) ||
            !FiniteNullable(
                nearestFrameEndDistanceToCandidateEndSeconds,
                requireNonnegative: true) ||
            !FiniteNullable(
                maximumGapSeconds,
                requireNonnegative: true) ||
            !FiniteNonnegative(
                maximumAbsoluteInferredPtsDriftSeconds) ||
            !FiniteNonnegative(
                meanAbsoluteInferredPtsDriftSeconds) ||
            !FiniteNonnegative(
                inferredPtsDriftWarningToleranceSeconds) ||
            warningSnapshot.Any(static value => !Enum.IsDefined(value)) ||
            warningSnapshot.Distinct().Count() !=
                warningSnapshot.Length ||
            !warningSnapshot.SequenceEqual(
                warningSnapshot.OrderBy(static value => value)) ||
            !compatibleTensorIdentityEqual ||
            !compatibleFrameIdentityEqual ||
            !passed)
        {
            throw new ArgumentException(
                "Execution timing must contain one complete, finite, successful, identity-preserving actual-PTS case.");
        }

        CaseId = VisualSemanticContractText.Required(
            caseId,
            nameof(caseId),
            128);
        CandidateId = VisualSemanticContractText.Required(
            candidateId,
            nameof(candidateId),
            128);
        CaseOrdinal = caseOrdinal;
        ReviewVideoSha256 = ModelArtifactManifest.Sha256Value(
            reviewVideoSha256,
            nameof(reviewVideoSha256));
        RequestedAbsoluteReviewStartSeconds =
            requestedAbsoluteReviewStartSeconds;
        RequestedAbsoluteReviewEndSeconds =
            requestedAbsoluteReviewEndSeconds;
        CandidateAbsoluteStartSeconds =
            candidateAbsoluteStartSeconds;
        CandidateAbsoluteEndSeconds =
            candidateAbsoluteEndSeconds;
        SourceBeginStreamSeconds = sourceBeginStreamSeconds;
        SourceEndStreamSeconds = sourceEndStreamSeconds;
        SourceAverageFramesPerSecond =
            sourceAverageFramesPerSecond;
        _selectedFrameIndices =
            Array.AsReadOnly(indexSnapshot);
        _inferredTimestampsSeconds =
            Array.AsReadOnly(inferredSnapshot);
        _actualPtsSeconds =
            Array.AsReadOnly(ptsSnapshot);
        _actualFrameDurationsSeconds =
            Array.AsReadOnly(durationSnapshot);
        QwenFinalTensorSha256 = ModelArtifactManifest.Sha256Value(
            qwenFinalTensorSha256,
            nameof(qwenFinalTensorSha256));
        _qwenFinalFrameSha256 =
            Array.AsReadOnly(
                RequireHashes(
                    qwenFrameHashSnapshot,
                    nameof(qwenFinalFrameSha256)));
        DirectCompatibleTensorSha256 =
            ModelArtifactManifest.Sha256Value(
                directCompatibleTensorSha256,
                nameof(directCompatibleTensorSha256));
        _directCompatibleFrameSha256 =
            Array.AsReadOnly(
                RequireHashes(
                    directFrameHashSnapshot,
                    nameof(directCompatibleFrameSha256)));
        CompatibleTensorIdentityEqual =
            compatibleTensorIdentityEqual;
        CompatibleFrameIdentityEqual =
            compatibleFrameIdentityEqual;
        CandidateIntersectingFrameCount =
            candidateIntersectingFrameCount;
        HasAtLeastTwoTemporallyDistinctFrames =
            hasAtLeastTwoTemporallyDistinctFrames;
        BeginningJudgmentSupportable =
            beginningJudgmentSupportable;
        OutcomeJudgmentSupportable =
            outcomeJudgmentSupportable;
        NearestSampleDistanceToCandidateStartSeconds =
            nearestSampleDistanceToCandidateStartSeconds;
        NearestFrameEndDistanceToCandidateEndSeconds =
            nearestFrameEndDistanceToCandidateEndSeconds;
        MaximumGapSeconds = maximumGapSeconds;
        AllActualPtsInsideRequestedReview =
            allActualPtsInsideRequestedReview;
        AllActualFrameIntervalsInsideRequestedReview =
            allActualFrameIntervalsInsideRequestedReview;
        RequestedTrimHonored = requestedTrimHonored;
        MaximumAbsoluteInferredPtsDriftSeconds =
            maximumAbsoluteInferredPtsDriftSeconds;
        MeanAbsoluteInferredPtsDriftSeconds =
            meanAbsoluteInferredPtsDriftSeconds;
        InferredPtsDriftWarningToleranceSeconds =
            inferredPtsDriftWarningToleranceSeconds;
        ContainerDurationExceedsVideoStreamEnd =
            containerDurationExceedsVideoStreamEnd;
        _warningCodes = Array.AsReadOnly(warningSnapshot);
        Passed = passed;
        CanonicalCaseTimingSha256 =
            ModelArtifactManifest.Sha256Value(
                canonicalCaseTimingSha256,
                nameof(canonicalCaseTimingSha256));
    }

    public string CaseId { get; }

    public string CandidateId { get; }

    public int CaseOrdinal { get; }

    public string ReviewVideoSha256 { get; }

    public double RequestedAbsoluteReviewStartSeconds { get; }

    public double RequestedAbsoluteReviewEndSeconds { get; }

    public double CandidateAbsoluteStartSeconds { get; }

    public double CandidateAbsoluteEndSeconds { get; }

    public double? SourceBeginStreamSeconds { get; }

    public double? SourceEndStreamSeconds { get; }

    public double SourceAverageFramesPerSecond { get; }

    public IReadOnlyList<int> SelectedFrameIndices =>
        _selectedFrameIndices;

    public IReadOnlyList<double> InferredTimestampsSeconds =>
        _inferredTimestampsSeconds;

    public IReadOnlyList<double> ActualPtsSeconds =>
        _actualPtsSeconds;

    public IReadOnlyList<double> ActualFrameDurationsSeconds =>
        _actualFrameDurationsSeconds;

    public string QwenFinalTensorSha256 { get; }

    public IReadOnlyList<string> QwenFinalFrameSha256 =>
        _qwenFinalFrameSha256;

    public string DirectCompatibleTensorSha256 { get; }

    public IReadOnlyList<string> DirectCompatibleFrameSha256 =>
        _directCompatibleFrameSha256;

    public bool CompatibleTensorIdentityEqual { get; }

    public bool CompatibleFrameIdentityEqual { get; }

    public int CandidateIntersectingFrameCount { get; }

    public bool HasAtLeastTwoTemporallyDistinctFrames { get; }

    public bool BeginningJudgmentSupportable { get; }

    public bool OutcomeJudgmentSupportable { get; }

    public double?
        NearestSampleDistanceToCandidateStartSeconds
    { get; }

    public double?
        NearestFrameEndDistanceToCandidateEndSeconds
    { get; }

    public double? MaximumGapSeconds { get; }

    public bool AllActualPtsInsideRequestedReview { get; }

    public bool AllActualFrameIntervalsInsideRequestedReview { get; }

    public bool RequestedTrimHonored { get; }

    public double MaximumAbsoluteInferredPtsDriftSeconds { get; }

    public double MeanAbsoluteInferredPtsDriftSeconds { get; }

    public double InferredPtsDriftWarningToleranceSeconds { get; }

    public bool ContainerDurationExceedsVideoStreamEnd { get; }

    public IReadOnlyList<VisualSemanticExecutionTimingWarningCode>
        WarningCodes => _warningCodes;

    public bool Passed { get; }

    public string CanonicalCaseTimingSha256 { get; }

    private static bool FiniteOrderedRange(
        double start,
        double end) =>
        double.IsFinite(start) &&
        double.IsFinite(end) &&
        start >= 0 &&
        end > start;

    private static bool FiniteNullable(
        double? value,
        bool requireNonnegative = false) =>
        !value.HasValue ||
        double.IsFinite(value.Value) &&
        (!requireNonnegative || value.Value >= 0);

    private static bool FiniteNonnegative(double value) =>
        double.IsFinite(value) &&
        value >= 0;

    private static string[] RequireHashes(
        string[] hashes,
        string parameterName)
    {
        for (int index = 0;
             index < hashes.Length;
             index++)
        {
            hashes[index] =
                ModelArtifactManifest.Sha256Value(
                    hashes[index],
                    $"{parameterName}[{index}]");
        }

        return hashes;
    }
}
