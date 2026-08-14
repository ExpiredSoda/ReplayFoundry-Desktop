using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

public enum ClipEditorialMetadataOrigin
{
    Heuristic,
    AiAssisted,
    UserEdited,
}

public enum ClipEditorialGenerationPreference
{
    HeuristicOnly,
    AiWhenAvailable,
    AiRequired,
}

public enum ClipEditorialEvidenceKind
{
    SourceIdentity,
    DeterministicMoment,
    CreatorTranscript,
    GameDialogueTranscript,
    MixedTranscript,
    VisualObservation,
    UserGameContext,
    GameKnowledge,
    UserContext,
}

public enum ClipEditorialWarningCode
{
    TranscriptUnavailable,
    VisualObservationUnavailable,
    AudioRoleUnknown,
    AiProviderUnavailable,
    AiProviderFailed,
    LimitedGrounding,
    GameContextUnconfirmed,
    GameKnowledgeUnavailable,
    GameKnowledgeNotClipLinked,
    MetadataReviewRequired,
    AiDraftRegenerated,
    GameKnowledgeNotSelected,
}

public enum ClipEditorialMetadataReadiness
{
    WorkingLabel,
    GroundedDraft,
    UserApproved,
}

public enum ClipEditorialMetadataQualityIssueCode
{
    ThirdPersonCreatorFraming,
    GenericOpening,
    UnsupportedMentalState,
    UnreviewedTranscriptReuse,
    TitleDescriptionRepetition,
    OverlongAudienceCopy,
    RedundantGameIdentity,
    AudienceCopyReview,
}

public sealed record ClipEditorialMetadataQualityIssue
{
    public ClipEditorialMetadataQualityIssue(
        ClipEditorialMetadataQualityIssueCode code,
        string message)
    {
        if (!Enum.IsDefined(code) || string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "Editorial metadata quality issues require a typed code and message.");
        }

        Code = code;
        Message = message.Trim();
    }

    public ClipEditorialMetadataQualityIssueCode Code { get; }

    public string Message { get; }
}

public sealed record ClipEditorialMetadataGeneratorIdentity
{
    public ClipEditorialMetadataGeneratorIdentity(
        string name,
        string version)
    {
        Name = Required(name, nameof(name));
        Version = Required(version, nameof(version));
    }

    public string Name { get; }

    public string Version { get; }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "Editorial generator identity values cannot be blank.",
                parameterName)
            : value.Trim();
}

public sealed record ClipEditorialAiProvenance
{
    public ClipEditorialAiProvenance(
        string providerName,
        string providerVersion,
        string runtimeVersion,
        string modelRepositoryId,
        string modelRevision,
        string modelManifestSha256,
        string promptName,
        string promptVersion,
        string promptSha256,
        TimeSpan batchElapsed,
        long? peakAllocatedGpuBytes)
    {
        ProviderName = Required(providerName, nameof(providerName));
        ProviderVersion = Required(providerVersion, nameof(providerVersion));
        RuntimeVersion = Required(runtimeVersion, nameof(runtimeVersion));
        ModelRepositoryId = Required(modelRepositoryId, nameof(modelRepositoryId));
        ModelRevision = Required(modelRevision, nameof(modelRevision));
        ModelManifestSha256 = Sha256(modelManifestSha256, nameof(modelManifestSha256));
        PromptName = Required(promptName, nameof(promptName));
        PromptVersion = Required(promptVersion, nameof(promptVersion));
        PromptSha256 = Sha256(promptSha256, nameof(promptSha256));
        if (batchElapsed < TimeSpan.Zero || peakAllocatedGpuBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchElapsed));
        }

        BatchElapsed = batchElapsed;
        PeakAllocatedGpuBytes = peakAllocatedGpuBytes;
    }

    public string ProviderName { get; }
    public string ProviderVersion { get; }
    public string RuntimeVersion { get; }
    public string ModelRepositoryId { get; }
    public string ModelRevision { get; }
    public string ModelManifestSha256 { get; }
    public string PromptName { get; }
    public string PromptVersion { get; }
    public string PromptSha256 { get; }
    public TimeSpan BatchElapsed { get; }
    public long? PeakAllocatedGpuBytes { get; }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("AI provenance values cannot be blank.", parameterName)
            : value.Trim();

    private static string Sha256(string value, string parameterName) =>
        value is null || value.Length != 64 ||
        value.Any(static character => !Uri.IsHexDigit(character))
            ? throw new ArgumentException("AI provenance hashes must be SHA-256 values.", parameterName)
            : value.ToLowerInvariant();
}

public sealed record ClipEditorialEvidenceReference
{
    public ClipEditorialEvidenceReference(
        string id,
        ClipEditorialEvidenceKind kind,
        string description)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Id = Required(id, nameof(id));
        Kind = kind;
        Description = Required(description, nameof(description));
    }

    public string Id { get; }

    public ClipEditorialEvidenceKind Kind { get; }

    public string Description { get; }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "Editorial evidence values cannot be blank.",
                parameterName)
            : value.Trim();
}

public sealed record ClipEditorialWarning
{
    public ClipEditorialWarning(
        ClipEditorialWarningCode code,
        string message)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        Code = code;
        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException(
                "Editorial warnings require a message.",
                nameof(message))
            : message.Trim();
    }

    public ClipEditorialWarningCode Code { get; }

    public string Message { get; }
}
