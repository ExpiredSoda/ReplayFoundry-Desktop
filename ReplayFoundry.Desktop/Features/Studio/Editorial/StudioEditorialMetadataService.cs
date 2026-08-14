using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

internal sealed record StudioEditorialDraftSnapshot(
    string Title,
    string Description,
    string Tags,
    string Status,
    string DraftState,
    bool NeedsCurrentCutRefresh,
    string CurrentCutStatus);

internal sealed record StudioEditorialProfileSnapshot(
    string AudienceAddress,
    string NamingGuidance,
    string DescriptionSignature);

internal sealed record StudioEditorialRerollResult(
    bool IsAiAssisted,
    string Status);

internal sealed class StudioEditorialMetadataService
{
    private readonly IGenerationOutputEditor? _outputEditor;
    private readonly IGenerationOutputSession? _outputSession;
    private readonly IClipEditorialMetadataGenerationService? _generator;
    private readonly IClipEditorialProfileEditor? _profileEditor;

    public StudioEditorialMetadataService(
        IGenerationOutputEditor? outputEditor,
        IClipEditorialMetadataGenerationService? generator,
        IClipEditorialProfileEditor? profileEditor)
    {
        _outputEditor = outputEditor;
        _outputSession = outputEditor as IGenerationOutputSession;
        _generator = generator;
        _profileEditor = profileEditor;
    }

    public int MaximumTitleLength =>
        ClipEditorialMetadataDraft.MaximumTitleLength;

    public int MaximumDescriptionLength =>
        ClipEditorialMetadataDraft.MaximumDescriptionLength;

    public bool IsAiAvailable => _generator?.IsAiAvailable == true;

    public bool CanEdit(
        GenerationOutputProject? project,
        GenerationOutputAsset? asset) =>
        project?.IsFinalized == false &&
        asset?.EditorialMetadata is not null &&
        _outputEditor is not null;

    public StudioEditorialDraftSnapshot LoadDraft(
        GenerationOutputAsset? asset)
    {
        ClipEditorialMetadataDraft? metadata = asset?.EditorialMetadata;
        ClipEditorialWarning? providerWarning = metadata?.Warnings
            .FirstOrDefault(static warning => warning.Code is
                ClipEditorialWarningCode.AiProviderFailed or
                ClipEditorialWarningCode.AiProviderUnavailable);
        bool needsCurrentCutRefresh =
            metadata is not null &&
            asset?.IsEditorialMetadataCurrentForCut == false;
        string status = metadata is null
            ? "This clip has no retained editorial metadata context."
            : metadata.QualityIssues.Count > 0
                ? "The AI draft is complete and usable, but its title or description needs review. Edit it or reroll for a genuinely different angle."
            : metadata.Readiness switch
            {
                ClipEditorialMetadataReadiness.WorkingLabel =>
                    "Working label only. Edit and save it, or create another metadata version, before queueing.",
                ClipEditorialMetadataReadiness.GroundedDraft =>
                    "Grounded AI draft ready. Review every claim before rendering.",
                ClipEditorialMetadataReadiness.UserApproved =>
                    "Your reviewed metadata is approved for this Studio draft.",
                _ => "Editorial metadata is ready.",
            };
        if (providerWarning is not null)
        {
            status = $"{status} {providerWarning.Message}";
        }
        string draftState = metadata?.Readiness switch
        {
            ClipEditorialMetadataReadiness.WorkingLabel => "Working draft",
            ClipEditorialMetadataReadiness.GroundedDraft => "AI-grounded draft",
            ClipEditorialMetadataReadiness.UserApproved => "Approved",
            _ => "Unavailable",
        };
        return new StudioEditorialDraftSnapshot(
            metadata?.Title ?? string.Empty,
            metadata?.Description ?? string.Empty,
            metadata?.TagsText ?? string.Empty,
            status,
            draftState,
            needsCurrentCutRefresh,
            needsCurrentCutRefresh
                ? "The clip boundaries changed after this copy was created. Refresh it so the wording and visual review use the current cut."
                : "This copy matches the current clip boundaries.");
    }

    public StudioEditorialProfileSnapshot LoadProfile()
    {
        ClipEditorialProfile profile =
            _profileEditor?.Current ?? ClipEditorialProfile.Default;
        return new StudioEditorialProfileSnapshot(
            profile.AudienceAddress,
            profile.NamingGuidance ?? string.Empty,
            profile.ReusableDescriptionSignature ?? string.Empty);
    }

    public void Save(
        GenerationOutputProject project,
        GenerationOutputAsset asset,
        string title,
        string description,
        string tags)
    {
        if (_outputEditor is null)
        {
            throw new InvalidOperationException(
                "The selected clip has no editable metadata draft.");
        }

        (GenerationOutputProject currentProject,
            GenerationOutputAsset currentAsset) =
            ResolveCurrent(project, asset);
        if (currentProject.IsFinalized ||
            currentAsset.EditorialMetadata is null)
        {
            throw new InvalidOperationException(
                "The selected clip has no editable metadata draft.");
        }

        ClipEditorialMetadataDraft edited =
            currentAsset.EditorialMetadata.WithUserEdits(
                title,
                description,
                ClipEditorialProfileTags.Parse(tags),
                preservePriorTitleHistory:
                    currentAsset.IsEditorialMetadataCurrentForCut);
        ClipEditorialContext currentCutContext =
            currentAsset.CreateCurrentCutEditorialContext();
        _outputEditor.ReplaceAsset(
            currentProject.Id,
            currentAsset.WithCurrentCutEditorialMetadata(
                currentCutContext,
                edited));
    }

    public async Task<StudioEditorialRerollResult> RerollAsync(
        GenerationOutputProject project,
        GenerationOutputAsset asset,
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature,
        bool requireAi,
        CancellationToken cancellationToken)
    {
        if (_generator is null ||
            _outputEditor is null)
        {
            throw new InvalidOperationException(
                "The selected clip has no retained context for a grounded reroll.");
        }

        (GenerationOutputProject currentProject,
            GenerationOutputAsset currentAsset) =
            ResolveCurrent(project, asset);
        if (currentProject.IsFinalized ||
            currentAsset.EditorialContext is null ||
            currentAsset.EditorialMetadata is null)
        {
            throw new InvalidOperationException(
                "The selected clip has no retained context for a grounded reroll.");
        }

        var profile = new ClipEditorialProfile(
            audienceAddress,
            namingGuidance,
            descriptionSignature,
            _profileEditor?.Current.DefaultTags ?? []);
        ClipEditorialContext requestedCutContext =
            currentAsset.CreateCurrentCutEditorialContext();
        IReadOnlyList<ClipEditorialPriorTitleExclusion> priorTitles =
            currentAsset.IsEditorialMetadataCurrentForCut
                ? currentAsset.EditorialMetadata
                    .CreatePriorTitleExclusions(requestedCutContext)
                : [];
        ClipEditorialMetadataDraft rerolled =
            await _generator.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    requestedCutContext,
                    profile,
                    currentAsset.EditorialMetadata.Attempt + 1,
                    requireAi
                        ? ClipEditorialGenerationPreference.AiRequired
                        : ClipEditorialGenerationPreference.HeuristicOnly,
                    currentAsset.SourceMedia,
                    priorAcceptedTitleExclusions: priorTitles),
                cancellationToken);
        // A trim, appearance, or preference edit may complete while the
        // external metadata provider is running. Apply only the new metadata
        // to the latest immutable asset so an older request object cannot
        // erase newer Studio work.
        (currentProject, currentAsset) = ResolveCurrent(
            currentProject,
            currentAsset);
        if (currentProject.IsFinalized)
        {
            throw new InvalidOperationException(
                "The Studio project was finalized before the metadata reroll completed.");
        }
        if (currentAsset.SourceStart != requestedCutContext.SourceStart ||
            currentAsset.SourceEnd != requestedCutContext.SourceEnd)
        {
            throw new InvalidOperationException(
                "The clip boundaries changed while metadata was being generated. " +
                "Replay Foundry kept the newer cut and did not apply copy made " +
                "for the older window; refresh it again when the cut is settled.");
        }
        _outputEditor.ReplaceAsset(
            currentProject.Id,
            currentAsset.WithCurrentCutEditorialMetadata(
                requestedCutContext,
                rerolled));
        bool isAiAssisted = rerolled.Origin ==
            ClipEditorialMetadataOrigin.AiAssisted;
        return new StudioEditorialRerollResult(
            isAiAssisted,
            isAiAssisted
                ? "Grounded AI metadata is ready. Review every claim before rendering."
                : "A new working label is ready. Edit and save it before rendering.");
    }

    public void SaveProfile(
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature)
    {
        if (_profileEditor is null)
        {
            throw new InvalidOperationException(
                "Reusable metadata wording is unavailable.");
        }

        _profileEditor.Update(
            new ClipEditorialProfile(
                audienceAddress,
                namingGuidance,
                descriptionSignature,
                _profileEditor.Current.DefaultTags,
                _profileEditor.Current.VoicePerspective));
    }

    private (GenerationOutputProject Project,
        GenerationOutputAsset Asset) ResolveCurrent(
        GenerationOutputProject project,
        GenerationOutputAsset asset)
    {
        GenerationOutputProject? current = _outputSession?.Current;
        if (current is null ||
            !current.Id.Equals(project.Id, StringComparison.Ordinal))
        {
            return (project, asset);
        }

        GenerationOutputAsset? resolved = current.Assets.FirstOrDefault(
            candidate => candidate.Id.Equals(
                asset.Id,
                StringComparison.Ordinal));
        if (resolved is null)
        {
            throw new InvalidOperationException(
                "The selected clip no longer belongs to the current Studio project.");
        }

        return (current, resolved);
    }
}
