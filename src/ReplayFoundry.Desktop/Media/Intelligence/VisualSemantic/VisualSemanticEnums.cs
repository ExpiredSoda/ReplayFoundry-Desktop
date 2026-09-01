using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public enum VisualSemanticTranscriptContextPolicy
{
    FullContextV1,
    VisualOnlyV1,
}

public enum VisualSemanticObservableContentType
{
    Action,
    Dialogue,
    Discovery,
    Failure,
    Humor,
    Story,
    MenuOrTraversal,
    Cinematic,
    Other,
    Unknown,
}

public enum VisualSemanticTernary
{
    Yes,
    No,
    Unsure,
}

public enum VisualSemanticRelevance
{
    Yes,
    No,
    Unknown,
}

public enum VisualSemanticReviewCertainty
{
    High,
    Medium,
    Low,
}

public enum VisualSemanticUncertaintyCode
{
    InsufficientVisualEvidence,
    AmbiguousEventBoundary,
    TranscriptMayBeInaccurate,
    OccludedOrObscured,
    FrameSamplingMayMissBriefEvent,
    CompositionRegionUnavailable,
    SpokenContentNotDirectlyObserved,
    Other,
}

public enum VisualSemanticIntegrityStatus
{
    Clear,
    FullFrameBlack,
    FullFrameFrozen,
    FullFrameBlackAndFrozen,
}

public enum VisualSemanticWarningCode
{
    ProviderReportedWarning,
    OutputNormalized,
    TranscriptApproximate,
    CompositionGeometryUnavailable,
    RuntimeIdentityUnavailable,
    PeakMemoryUnavailable,
    InferredTimestampDrift,
    ContainerDurationExceedsVideoStreamEnd,
    ProviderIdentityEchoMismatch,
}
