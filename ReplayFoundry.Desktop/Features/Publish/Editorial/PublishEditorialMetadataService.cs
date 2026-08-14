using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Publish.Editorial;

public sealed record PublishEditorialProfileSnapshot(
    string AudienceAddress,
    string NamingGuidance,
    string DescriptionSignature);

public sealed record PublishEditorialRerollResult(
    string Title,
    string Description,
    string Tags,
    int Attempt,
    IReadOnlyList<string> PriorAcceptedTitles,
    string Status);

public interface IPublishEditorialMetadataService
{
    bool IsAiAvailable { get; }

    PublishEditorialProfileSnapshot LoadProfile();

    bool CanReroll(LibraryMediaAsset asset);

    Task<PublishEditorialRerollResult> RerollAsync(
        LibraryMediaAsset asset,
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature,
        int? previousCompletedAttempt,
        string currentTitle,
        IReadOnlyList<string> priorAcceptedTitles,
        bool requireAi,
        CancellationToken cancellationToken);
}

/// <summary>
/// Gives Publish the same grounded metadata-generation boundary used by
/// Generate and Studio. A Library video may resolve its verified source context
/// from either the active session or its durable Studio project.
/// </summary>
internal sealed class PublishEditorialMetadataService :
    IPublishEditorialMetadataService
{
    private readonly IGenerationOutputSession _outputSession;
    private readonly IClipEditorialMetadataGenerationService _generator;
    private readonly IClipEditorialProfileEditor _profileEditor;
    private readonly IStudioProjectStore? _projectStore;

    public PublishEditorialMetadataService(
        IGenerationOutputSession outputSession,
        IClipEditorialMetadataGenerationService generator,
        IClipEditorialProfileEditor profileEditor,
        IStudioProjectStore? projectStore = null)
    {
        _outputSession = outputSession ??
            throw new ArgumentNullException(nameof(outputSession));
        _generator = generator ??
            throw new ArgumentNullException(nameof(generator));
        _profileEditor = profileEditor ??
            throw new ArgumentNullException(nameof(profileEditor));
        _projectStore = projectStore;
    }

    public bool IsAiAvailable => _generator.IsAiAvailable;

    public PublishEditorialProfileSnapshot LoadProfile()
    {
        ClipEditorialProfile profile = _profileEditor.Current;
        return new PublishEditorialProfileSnapshot(
            profile.AudienceAddress,
            profile.NamingGuidance ?? string.Empty,
            profile.ReusableDescriptionSignature ?? string.Empty);
    }

    public bool CanReroll(LibraryMediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Resolve(asset) is not null;
    }

    public async Task<PublishEditorialRerollResult> RerollAsync(
        LibraryMediaAsset asset,
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature,
        int? previousCompletedAttempt,
        string currentTitle,
        IReadOnlyList<string> priorAcceptedTitles,
        bool requireAi,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentTitle);
        ArgumentNullException.ThrowIfNull(priorAcceptedTitles);
        cancellationToken.ThrowIfCancellationRequested();
        GenerationOutputAsset source = Resolve(asset) ??
            throw new InvalidOperationException(
                "This Library video no longer has the saved source context needed for another grounded version. You can still edit and save its title, description, and tags.");

        ClipEditorialMetadataDraft retainedDraft =
            source.EditorialMetadata!;
        if (previousCompletedAttempt is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(previousCompletedAttempt));
        }
        int nextAttempt = checked(
            Math.Max(
                retainedDraft.Attempt,
                previousCompletedAttempt ?? retainedDraft.Attempt) + 1);
        ClipEditorialContext currentCutContext =
            source.CreateCurrentCutEditorialContext();
        var profile = new ClipEditorialProfile(
            audienceAddress,
            namingGuidance,
            descriptionSignature,
            _profileEditor.Current.DefaultTags);
        IReadOnlyList<string> titleHistory =
            ClipEditorialPriorTitleExclusion.MergeTitleHistory(
                retainedDraft.PriorAcceptedTitles
                    .Concat([retainedDraft.Title])
                    .Concat(priorAcceptedTitles),
                currentTitle);
        ClipEditorialPriorTitleExclusion[] exclusions = titleHistory
            .Select(title => ClipEditorialPriorTitleExclusion.ForContext(
                currentCutContext,
                title))
            .ToArray();
        ClipEditorialMetadataDraft rerolled =
            await _generator.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    currentCutContext,
                    profile,
                    nextAttempt,
                    requireAi
                        ? ClipEditorialGenerationPreference.AiRequired
                        : ClipEditorialGenerationPreference.HeuristicOnly,
                    source.SourceMedia,
                    priorAcceptedTitleExclusions: exclusions),
                cancellationToken);
        if (rerolled.Attempt < nextAttempt)
        {
            throw new InvalidOperationException(
                "The metadata generator returned an older Publish reroll attempt.");
        }

        return new PublishEditorialRerollResult(
            rerolled.Title,
            rerolled.Description,
            rerolled.TagsText,
            rerolled.Attempt,
            rerolled.PriorAcceptedTitles,
            rerolled.QualityIssues.Count > 0
                ? "A new local-AI draft is ready, with a copy-review flag. Edit it or reroll again for a different structure before uploading."
                : rerolled.Origin == ClipEditorialMetadataOrigin.AiAssisted
                ? "A new local-AI draft is ready. Review the title, description, and tags before uploading."
                : "A new deterministic working label is ready in this YouTube draft. Review it before uploading.");
    }

    private GenerationOutputAsset? Resolve(LibraryMediaAsset asset)
    {
        GenerationOutputAsset? active = Resolve(
            _outputSession.Current,
            asset);
        if (active is not null)
        {
            return active;
        }

        if (_projectStore is null)
        {
            return null;
        }

        StudioProjectLoadResult retained = _projectStore.Load(
            asset.ProjectId);
        GenerationOutputAsset? rendered = retained.CanOpen
            ? Resolve(retained.Project, asset)
            : null;
        if (rendered is not null)
        {
            return rendered;
        }

        string? sourceProjectId = SourceProjectIdFor(asset.ProjectId);
        if (sourceProjectId is null || asset.SourceCandidateIds.Count != 1)
        {
            return null;
        }

        StudioProjectLoadResult sourceProject = _projectStore.Load(
            sourceProjectId);
        if (!sourceProject.CanOpen ||
            sourceProject.Project is not { } project ||
            !project.Id.Equals(sourceProjectId, StringComparison.Ordinal))
        {
            return null;
        }

        string candidateId = asset.SourceCandidateIds[0];
        GenerationOutputAsset? source = project.Assets.SingleOrDefault(
            candidate =>
                candidate.Id.Equals(candidateId, StringComparison.Ordinal) &&
                candidate.EditorialContext is not null &&
                candidate.EditorialMetadata is not null &&
                candidate.IsEditorialMetadataCurrentForCut);
        return source is not null && File.Exists(source.SourceMedia.FullPath)
            ? source
            : null;
    }

    private static GenerationOutputAsset? Resolve(
        GenerationOutputProject? project,
        LibraryMediaAsset asset)
    {
        if (project?.IsFinalized != true ||
            !project.Id.Equals(asset.ProjectId, StringComparison.Ordinal) ||
            asset.ContributingCandidateCount != 1 ||
            !asset.IsAvailable)
        {
            return null;
        }

        GenerationOutputAsset? source = project.IncludedAssets
            .SingleOrDefault(candidate =>
            candidate.IsRendered &&
            candidate.OutputFullPath!.Equals(
                asset.OutputFullPath,
                StringComparison.OrdinalIgnoreCase) &&
            (asset.SourceCandidateIds.Count == 0 ||
             asset.SourceCandidateIds.Contains(
                 candidate.Id,
                 StringComparer.Ordinal)) &&
            candidate.EditorialContext is not null &&
            candidate.EditorialMetadata is not null &&
            candidate.IsEditorialMetadataCurrentForCut);
        return source is not null && File.Exists(source.SourceMedia.FullPath)
            ? source
            : null;
    }

    private static string? SourceProjectIdFor(string renderedProjectId)
    {
        const string marker = "-render-";
        int markerIndex = renderedProjectId.LastIndexOf(
            marker,
            StringComparison.Ordinal);
        if (markerIndex <= 0)
        {
            return null;
        }

        ReadOnlySpan<char> token = renderedProjectId.AsSpan(
            markerIndex + marker.Length);
        return token.Length >= 8 && token.ToArray().All(char.IsAsciiLetterOrDigit)
            ? renderedProjectId[..markerIndex]
            : null;
    }
}
