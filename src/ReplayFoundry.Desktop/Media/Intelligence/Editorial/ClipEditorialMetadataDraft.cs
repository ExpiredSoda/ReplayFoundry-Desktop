using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

public sealed class ClipEditorialMetadataDraft
{
    public const int MaximumTitleLength = 100;
    public const int MaximumDescriptionLength = 5_000;

    private readonly ReadOnlyCollection<string> _tags;
    private readonly ReadOnlyCollection<ClipEditorialEvidenceReference>
        _evidence;
    private readonly ReadOnlyCollection<ClipEditorialWarning> _warnings;
    private readonly ReadOnlyCollection<ClipEditorialMetadataQualityIssue>
        _qualityIssues;
    private readonly ReadOnlyCollection<string> _priorAcceptedTitles;

    public ClipEditorialMetadataDraft(
        string title,
        string description,
        IEnumerable<string> tags,
        ClipEditorialMetadataOrigin origin,
        ClipEditorialMetadataGeneratorIdentity generator,
        int attempt,
        IEnumerable<ClipEditorialEvidenceReference>? evidence = null,
        IEnumerable<ClipEditorialWarning>? warnings = null,
        ClipEditorialAiProvenance? aiProvenance = null,
        ClipEditorialMetadataReadiness? readiness = null,
        IEnumerable<ClipEditorialMetadataQualityIssue>? qualityIssues = null,
        IEnumerable<string>? priorAcceptedTitles = null,
        GameKnowledgeInfluenceAudit? groundingAudit = null,
        IEnumerable<ClipEditorialCopyVersion>? copyVersions = null)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(tags);
        if (attempt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        Title = RequiredBounded(
            title,
            MaximumTitleLength,
            nameof(title));
        Description = RequiredBounded(
            description,
            MaximumDescriptionLength,
            nameof(description));
        string[] tagSnapshot = tags
            .Select(ClipEditorialProfile.NormalizeTag)
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToArray();
        ClipEditorialEvidenceReference[] evidenceSnapshot =
            evidence?.ToArray() ?? [];
        ClipEditorialWarning[] warningSnapshot =
            warnings?.ToArray() ?? [];
        ClipEditorialMetadataQualityIssue[] qualityIssueSnapshot =
            qualityIssues?.ToArray() ?? [];
        if (evidenceSnapshot.Any(static value => value is null) ||
            warningSnapshot.Any(static value => value is null) ||
            qualityIssueSnapshot.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Editorial evidence and warnings cannot contain null entries.");
        }

        _tags = Array.AsReadOnly(tagSnapshot);
        Origin = origin;
        Generator = generator;
        Attempt = attempt;
        _evidence = Array.AsReadOnly(evidenceSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
        AiProvenance = aiProvenance;
        Readiness = readiness ?? origin switch
        {
            ClipEditorialMetadataOrigin.Heuristic =>
                ClipEditorialMetadataReadiness.WorkingLabel,
            ClipEditorialMetadataOrigin.AiAssisted =>
                ClipEditorialMetadataReadiness.GroundedDraft,
            ClipEditorialMetadataOrigin.UserEdited =>
                ClipEditorialMetadataReadiness.UserEditedDraft,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (!Enum.IsDefined(Readiness))
        {
            throw new ArgumentException(
                "Editorial metadata readiness is invalid.",
                nameof(readiness));
        }
        _qualityIssues = Array.AsReadOnly(qualityIssueSnapshot);
        _priorAcceptedTitles = new ReadOnlyCollection<string>(
            ClipEditorialPriorTitleExclusion.MergeTitleHistory(
                    priorAcceptedTitles)
                .ToArray());
        GroundingAudit = groundingAudit;
        ClipEditorialCopyVersion[] versions = copyVersions?.ToArray() ?? [];
        if (versions.Any(static version => version is null))
            throw new ArgumentException("Earlier copy cannot contain null entries.", nameof(copyVersions));
        CopyVersions = Array.AsReadOnly(versions.TakeLast(20).ToArray());
    }

    public string Title { get; }
    public string Description { get; }

    public IReadOnlyList<string> Tags => _tags;

    public string TagsText => string.Join(", ", _tags);

    public ClipEditorialMetadataOrigin Origin { get; }

    public ClipEditorialMetadataGeneratorIdentity Generator { get; }

    public int Attempt { get; }

    public IReadOnlyList<ClipEditorialEvidenceReference> Evidence =>
        _evidence;

    public IReadOnlyList<ClipEditorialWarning> Warnings => _warnings;

    public ClipEditorialAiProvenance? AiProvenance { get; }

    public ClipEditorialMetadataReadiness Readiness { get; }

    public bool IsPublishReady => Readiness is
        ClipEditorialMetadataReadiness.WorkingLabel or
        ClipEditorialMetadataReadiness.GroundedDraft or
        ClipEditorialMetadataReadiness.UserEditedDraft or
        ClipEditorialMetadataReadiness.UserApproved;

    public IReadOnlyList<ClipEditorialMetadataQualityIssue> QualityIssues =>
        _qualityIssues;

    /// <summary>
    /// Earlier audience copy for this exact cut. These values are retained
    /// solely to prevent a reroll from returning a prior title; they are not
    /// factual clip evidence.
    /// </summary>
    public IReadOnlyList<string> PriorAcceptedTitles =>
        _priorAcceptedTitles;

    public GameKnowledgeInfluenceAudit? GroundingAudit { get; }

    public IReadOnlyList<ClipEditorialCopyVersion> CopyVersions { get; }

    public ClipEditorialMetadataDraft RememberPreviousCopy(
        ClipEditorialMetadataDraft previous, string contextFingerprint)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var version = new ClipEditorialCopyVersion(previous.Title, previous.Description,
            previous.Tags, contextFingerprint, DateTimeOffset.UtcNow);
        IEnumerable<ClipEditorialCopyVersion> versions = previous.CopyVersions
            .Concat(CopyVersions)
            .Append(version)
            .Reverse()
            .DistinctBy(static value => (value.ContextFingerprint, value.Title, value.Description, string.Join(",", value.Tags)))
            .Reverse();
        return new(Title, Description, Tags, Origin, Generator, Attempt, Evidence,
            Warnings, AiProvenance, Readiness, QualityIssues, PriorAcceptedTitles, GroundingAudit, versions);
    }

    public IReadOnlyList<ClipEditorialPriorTitleExclusion>
        CreatePriorTitleExclusions(ClipEditorialContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ClipEditorialPriorTitleExclusion.MergeTitleHistory(
                PriorAcceptedTitles,
                Title)
            .Select(title => ClipEditorialPriorTitleExclusion.ForContext(
                context,
                title))
            .ToArray();
    }

    public ClipEditorialMetadataDraft WithUserEdits(
        string title,
        string description,
        IEnumerable<string> tags,
        bool preservePriorTitleHistory = true) =>
        new(
            title,
            description,
            tags,
            ClipEditorialMetadataOrigin.UserEdited,
            Generator,
            Attempt,
            Evidence,
            Warnings,
            AiProvenance,
            ClipEditorialMetadataReadiness.UserEditedDraft,
            qualityIssues: [],
            priorAcceptedTitles: preservePriorTitleHistory
                ? ClipEditorialPriorTitleExclusion.MergeTitleHistory(
                    PriorAcceptedTitles,
                    Title)
                : [],
            groundingAudit: GroundingAudit,
            copyVersions: CopyVersions);

    public ClipEditorialMetadataDraft MarkReviewed() =>
        new(
            Title,
            Description,
            Tags,
            Origin,
            Generator,
            Attempt,
            Evidence,
            Warnings,
            AiProvenance,
            ClipEditorialMetadataReadiness.UserApproved,
            QualityIssues,
            PriorAcceptedTitles,
            GroundingAudit,
            CopyVersions);

    internal ClipEditorialMetadataDraft WithoutGroundingAudit() =>
        GroundingAudit is null
            ? this
            : new ClipEditorialMetadataDraft(
                Title,
                Description,
                Tags,
                Origin,
                Generator,
                Attempt,
                Evidence,
                Warnings,
                AiProvenance,
                Readiness,
                QualityIssues,
                PriorAcceptedTitles,
                groundingAudit: null,
                copyVersions: CopyVersions);

    private static string RequiredBounded(
        string value,
        int maximum,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Editorial text cannot be blank.",
                parameterName);
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maximum
            ? trimmed
            : throw new ArgumentException(
                $"Editorial text cannot exceed {maximum} characters.",
                parameterName);
    }
}
