namespace ReplayFoundry.Desktop.Media.Moments;

public enum MomentOutputKind
{
    StandaloneClip,
    MontageSegment,
}

public enum MomentContentEmphasis
{
    GameplayFocused,
    Balanced,
    CommentaryFocused,
}

public enum MomentSignalAblation
{
    None,
    NoPresenter,
    NoAudio,
    NoScene,
    GameplayOnly,
}

public enum MomentAnchorKind
{
    GameplayActivityBurst,
    GameplaySceneCluster,
    GameplaySceneBoundary,
    AudioNovelty,
    AudioReentry,
    PresenterGatedSupport,
    PresenterAudioAgreement,
    EpisodeActivationPeak,
    UserConfirmedPriority,
}

public enum MomentCandidateDisposition
{
    Eligible,
    BelowThreshold,
    RejectedBlack,
    RejectedFreeze,
    SuppressedOverlap,
    SuppressedNeighborhood,
    SuppressedEpisode,
    SuppressedSubepisode,
    SuppressedCooldown,
    Selected,
}

public enum MomentCandidateConstructionReason
{
    SingleAnchor,
    MultiSignalNeighborhood,
    DenseSceneCluster,
    ActivityBurst,
    ShortSource,
    StandaloneEpisode,
    MontageRepresentativeSegment,
}

public enum MomentScoreComponentCode
{
    GameplayProminence,
    GameplayOnset,
    GameplayBurstIntegration,
    GameplaySceneChange,
    GameplaySceneDensity,
    AudioNovelty,
    AudioReentry,
    PresenterGatedSupport,
    MultiSignalOnsetAgreement,
    DurationFit,
    ClusterCoherence,
    VisualContextChange,
    PayoffSupport,
    ContinuousActivityPenalty,
    FullFrameBlackPenalty,
    FullFrameFreezePenalty,
    GameplayLowInformationPenalty,
    SourceEdgePenalty,
    NeighborhoodRedundancyPenalty,
    EpisodePeakStrength,
    EpisodeIntegratedStrength,
    EpisodeOnsetStrength,
    EpisodeRecoverySupport,
    EpisodeCohesion,
    MontageRepresentativeCoverage,
    MontageEpisodeRedundancyPenalty,
    EpisodeDistinctiveness,
    BaselineCoreSeparation,
    CoreRecoverySeparation,
    IndependentFamilyAgreement,
    CorrelatedVisualSupportPenalty,
    SingleFamilyDominancePenalty,
    ContinuousUniformityPenalty,
    StandaloneEpisodeCompleteness,
    MontageRepresentativeDensity,
}

public enum MomentEvidenceReferenceKind
{
    GameplayActivitySample,
    GameplayActivityBurst,
    PresenterActivitySample,
    AudioSignalWindow,
    AudioNoveltyEvent,
    SilenceInterval,
    SceneBoundary,
    SceneCluster,
    BlackInterval,
    FreezeInterval,
    LumaChange,
    SaturationChange,
    UserConfirmedMomentGuidance,
}

public enum MomentFindingWarningCode
{
    NoGameplayActivityEvidence,
    NoGameplaySceneEvidence,
    NoCandidateAnchors,
    NoAudioStreams,
    CommentarySemanticEvidenceUnavailable,
    NoPresenterEvidence,
    SourceShorterThanMinimumWindow,
    CandidateRejectedForBlackOverlap,
    CandidateRejectedForFreezeOverlap,
    DesiredResultCountNotMet,
    AllCandidatesBelowThreshold,
    MontageSequencingNotImplemented,
    SparseSignalCoverage,
    EpisodeRecoveryUnavailable,
}

public enum MomentSignalFamily
{
    GameplayBurst,
    GameplayScene,
    AudioNovelty,
    PresenterProminence,
    VisualContextChange,
    EpisodeActivation,
    UserGuidance,
}

public enum MomentNeighborhoodSplitReason
{
    None,
    ActivityValley,
}

public enum MomentActivationComponentCode
{
    GameplayProminence,
    GameplayOnset,
    GameplayIntegratedBurst,
    GameplaySceneSupport,
    AudioNovelty,
    AudioReentry,
    PresenterGatedSupport,
    VisualContextSupport,
    ContinuousActivityPenalty,
    GatedEventAnchorSupport,
    CorrelatedVisualSupportPenalty,
}

public enum MomentActivationIntegrityState
{
    Clear,
    FullFrameBlack,
    FullFrameFrozen,
    FullFrameBlackAndFrozen,
}

public enum MomentEventEpisodePhaseKind
{
    LeadIn,
    Rising,
    Core,
    Falling,
    Recovery,
}

public enum MomentEpisodeSplitRationale
{
    None,
    DeepSustainedValley,
}

public enum MontageSegmentSelectionReason
{
    PrimaryEpisodeRepresentative,
    ValidatedSubepisodeRepresentative,
}

public enum MontageSegmentObjectiveComponentCode
{
    IntegratedActivationCoverage,
    PeakContainment,
    OnsetProximity,
    RecoveryCoverage,
    MultiSignalAgreement,
    SceneClusterContainment,
    IntegrityPenalty,
    SourceEdgePenalty,
    IntraEpisodeRedundancyPenalty,
}
