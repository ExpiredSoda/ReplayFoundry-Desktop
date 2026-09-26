namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MediaMomentFindingOptions
{
    public const string CurrentPolicyVersion = "1.4";

    public MediaMomentFindingOptions(
        MomentOutputKind outputKind,
        MomentContentEmphasis contentEmphasis,
        int desiredCandidateCount,
        double minimumHeuristicScore,
        TimeSpan minimumDuration,
        TimeSpan targetDuration,
        TimeSpan maximumDuration,
        TimeSpan preRoll,
        TimeSpan postRoll,
        TimeSpan sceneClusterMaximumGap,
        TimeSpan gameplayActivityPeakMergeGap,
        TimeSpan crossSignalAgreementWindow,
        TimeSpan meaningfulAudioSilenceDuration,
        double fullFrameBlackHardRejectionRatio,
        double fullFrameFreezeHardRejectionRatio,
        double candidateOverlapSuppressionRatio,
        int proposalPoolMaximum,
        double visualNormalizationLowPercentile,
        double visualNormalizationHighPercentile,
        double audioNormalizationLowPercentile,
        double audioNormalizationHighPercentile,
        MomentComponentWeightProfile componentWeights,
        string policyVersion = CurrentPolicyVersion,
        MomentCalibrationPolicy? calibrationPolicy = null,
        MomentEpisodePolicy? episodePolicy = null,
        MomentDistinctivenessPolicy? distinctivenessPolicy = null,
        MomentSignalAblation signalAblation = MomentSignalAblation.None)
    {
        if (!Enum.IsDefined(outputKind))
        {
            throw new ArgumentOutOfRangeException(nameof(outputKind));
        }

        if (!Enum.IsDefined(contentEmphasis))
        {
            throw new ArgumentOutOfRangeException(nameof(contentEmphasis));
        }

        if (!Enum.IsDefined(signalAblation))
        {
            throw new ArgumentOutOfRangeException(nameof(signalAblation));
        }

        if (desiredCandidateCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(desiredCandidateCount));
        }

        if (proposalPoolMaximum <= 0 ||
            proposalPoolMaximum < desiredCandidateCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(proposalPoolMaximum),
                "The proposal pool must be positive and at least the desired count.");
        }

        ValidateScore(minimumHeuristicScore, nameof(minimumHeuristicScore));
        ValidatePositive(minimumDuration, nameof(minimumDuration));
        ValidatePositive(targetDuration, nameof(targetDuration));
        ValidatePositive(maximumDuration, nameof(maximumDuration));

        if (minimumDuration > targetDuration ||
            targetDuration > maximumDuration)
        {
            throw new ArgumentException(
                "Moment durations must satisfy minimum <= target <= maximum.");
        }

        ValidateNonNegative(preRoll, nameof(preRoll));
        ValidateNonNegative(postRoll, nameof(postRoll));
        ValidateNonNegative(sceneClusterMaximumGap, nameof(sceneClusterMaximumGap));
        ValidateNonNegative(gameplayActivityPeakMergeGap, nameof(gameplayActivityPeakMergeGap));
        ValidateNonNegative(crossSignalAgreementWindow, nameof(crossSignalAgreementWindow));
        ValidatePositive(meaningfulAudioSilenceDuration, nameof(meaningfulAudioSilenceDuration));
        ValidateRatio(fullFrameBlackHardRejectionRatio, nameof(fullFrameBlackHardRejectionRatio));
        ValidateRatio(fullFrameFreezeHardRejectionRatio, nameof(fullFrameFreezeHardRejectionRatio));
        ValidateRatio(candidateOverlapSuppressionRatio, nameof(candidateOverlapSuppressionRatio));
        ValidatePercentiles(
            visualNormalizationLowPercentile,
            visualNormalizationHighPercentile,
            nameof(visualNormalizationLowPercentile));
        ValidatePercentiles(
            audioNormalizationLowPercentile,
            audioNormalizationHighPercentile,
            nameof(audioNormalizationLowPercentile));

        ArgumentNullException.ThrowIfNull(componentWeights);
        calibrationPolicy ??= MomentCalibrationPolicy.CreateDefaults();
        episodePolicy ??= MomentEpisodePolicy.CreateDefaults();
        distinctivenessPolicy ??=
            MomentDistinctivenessPolicy.CreateDefaults();

        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException(
                "Moment-finding options require a policy version.",
                nameof(policyVersion));
        }

        OutputKind = outputKind;
        ContentEmphasis = contentEmphasis;
        DesiredCandidateCount = desiredCandidateCount;
        MinimumHeuristicScore = minimumHeuristicScore;
        MinimumDuration = minimumDuration;
        TargetDuration = targetDuration;
        MaximumDuration = maximumDuration;
        PreRoll = preRoll;
        PostRoll = postRoll;
        SceneClusterMaximumGap = sceneClusterMaximumGap;
        GameplayActivityPeakMergeGap = gameplayActivityPeakMergeGap;
        CrossSignalAgreementWindow = crossSignalAgreementWindow;
        MeaningfulAudioSilenceDuration = meaningfulAudioSilenceDuration;
        FullFrameBlackHardRejectionRatio = fullFrameBlackHardRejectionRatio;
        FullFrameFreezeHardRejectionRatio = fullFrameFreezeHardRejectionRatio;
        CandidateOverlapSuppressionRatio = candidateOverlapSuppressionRatio;
        ProposalPoolMaximum = proposalPoolMaximum;
        VisualNormalizationLowPercentile = visualNormalizationLowPercentile;
        VisualNormalizationHighPercentile = visualNormalizationHighPercentile;
        AudioNormalizationLowPercentile = audioNormalizationLowPercentile;
        AudioNormalizationHighPercentile = audioNormalizationHighPercentile;
        ComponentWeights = componentWeights;
        CalibrationPolicy = calibrationPolicy;
        EpisodePolicy = episodePolicy;
        DistinctivenessPolicy = distinctivenessPolicy;
        SignalAblation = signalAblation;
        PolicyVersion = policyVersion.Trim();
    }

    public MomentOutputKind OutputKind { get; }
    public MomentContentEmphasis ContentEmphasis { get; }
    public int DesiredCandidateCount { get; }
    public double MinimumHeuristicScore { get; }
    public TimeSpan MinimumDuration { get; }
    public TimeSpan TargetDuration { get; }
    public TimeSpan MaximumDuration { get; }
    public TimeSpan PreRoll { get; }
    public TimeSpan PostRoll { get; }
    public TimeSpan SceneClusterMaximumGap { get; }
    public TimeSpan GameplayActivityPeakMergeGap { get; }
    public TimeSpan CrossSignalAgreementWindow { get; }
    public TimeSpan MeaningfulAudioSilenceDuration { get; }
    public double FullFrameBlackHardRejectionRatio { get; }
    public double FullFrameFreezeHardRejectionRatio { get; }
    public double CandidateOverlapSuppressionRatio { get; }
    public int ProposalPoolMaximum { get; }
    public double VisualNormalizationLowPercentile { get; }
    public double VisualNormalizationHighPercentile { get; }
    public double AudioNormalizationLowPercentile { get; }
    public double AudioNormalizationHighPercentile { get; }
    public MomentComponentWeightProfile ComponentWeights { get; }
    public MomentCalibrationPolicy CalibrationPolicy { get; }
    public MomentEpisodePolicy EpisodePolicy { get; }
    public MomentDistinctivenessPolicy DistinctivenessPolicy { get; }
    public MomentSignalAblation SignalAblation { get; }
    public string PolicyVersion { get; }

    public static MediaMomentFindingOptions CreateDefaults(
        MomentOutputKind outputKind,
        MomentContentEmphasis contentEmphasis = MomentContentEmphasis.Balanced,
        int desiredCandidateCount = 5,
        double minimumHeuristicScore = 50)
    {
        return outputKind switch
        {
            MomentOutputKind.StandaloneClip =>
                Create(
                    outputKind,
                    contentEmphasis,
                    desiredCandidateCount,
                    minimumHeuristicScore,
                    TimeSpan.FromSeconds(12),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(60),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(20)),
            MomentOutputKind.MontageSegment =>
                Create(
                    outputKind,
                    contentEmphasis,
                    desiredCandidateCount,
                    minimumHeuristicScore,
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(7),
                    TimeSpan.FromSeconds(12),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5)),
            _ => throw new ArgumentOutOfRangeException(nameof(outputKind)),
        };
    }

    public MediaMomentFindingOptions WithMaximumDuration(
        TimeSpan maximumDuration)
    {
        ValidatePositive(maximumDuration, nameof(maximumDuration));
        TimeSpan target = TargetDuration <= maximumDuration
            ? TargetDuration
            : maximumDuration;
        TimeSpan minimum = MinimumDuration <= target
            ? MinimumDuration
            : target;

        return new MediaMomentFindingOptions(
            OutputKind,
            ContentEmphasis,
            DesiredCandidateCount,
            MinimumHeuristicScore,
            minimum,
            target,
            maximumDuration,
            PreRoll,
            PostRoll,
            SceneClusterMaximumGap,
            GameplayActivityPeakMergeGap,
            CrossSignalAgreementWindow,
            MeaningfulAudioSilenceDuration,
            FullFrameBlackHardRejectionRatio,
            FullFrameFreezeHardRejectionRatio,
            CandidateOverlapSuppressionRatio,
            ProposalPoolMaximum,
            VisualNormalizationLowPercentile,
            VisualNormalizationHighPercentile,
            AudioNormalizationLowPercentile,
            AudioNormalizationHighPercentile,
            ComponentWeights,
            PolicyVersion,
            CalibrationPolicy,
            EpisodePolicy,
            DistinctivenessPolicy,
            SignalAblation);
    }

    private static MediaMomentFindingOptions Create(
        MomentOutputKind outputKind,
        MomentContentEmphasis emphasis,
        int desiredCount,
        double minimumScore,
        TimeSpan minimum,
        TimeSpan target,
        TimeSpan maximum,
        TimeSpan preRoll,
        TimeSpan postRoll) =>
        new(
            outputKind,
            emphasis,
            desiredCount,
            minimumScore,
            minimum,
            target,
            maximum,
            preRoll,
            postRoll,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(2),
            0.60,
            0.75,
            outputKind == MomentOutputKind.StandaloneClip
                ? 0.50
                : 0.65,
            200,
            0.20,
            0.90,
            0.10,
            0.90,
            MomentComponentWeightProfile.Create(emphasis, outputKind));

    private static void ValidateScore(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateRatio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidatePercentiles(
        double low,
        double high,
        string name)
    {
        ValidateRatio(low, name);
        ValidateRatio(high, name);

        if (low >= high)
        {
            throw new ArgumentException(
                "Normalization percentiles must satisfy low < high.",
                name);
        }
    }

    private static void ValidatePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateNonNegative(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
