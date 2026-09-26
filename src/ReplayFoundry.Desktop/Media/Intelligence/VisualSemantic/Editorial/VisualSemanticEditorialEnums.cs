namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public enum VisualSemanticEvidenceBasis
{
    Visual,
    TranscriptContext,
    Both,
}

public enum VisualSemanticTranscriptContextSupport
{
    Supports,
    DoesNotSupport,
    NotSupplied,
    UnreliableOrAmbiguous,
}

public enum VisualSemanticEditorialDisposition
{
    Keep,
    Reject,
    Unsure,
}

public enum VisualSemanticEditorialRejectReason
{
    RoutineTraversal,
    MenuOrInventoryOnly,
    NoObservablePayoff,
    AmbientChangeOnly,
    MissingRequiredContext,
    NoDistinctEvent,
    InsufficientEvidence,
    None,
}

public enum VisualSemanticEditorialUncertaintyCode
{
    InsufficientVisualEvidence,
    AmbiguousEventBoundary,
    TranscriptMayBeInaccurate,
    OccludedOrObscured,
    FrameSamplingMayMissBriefEvent,
    CompositionRegionUnavailable,
    TranscriptContextContradictory,
    Other,
}
