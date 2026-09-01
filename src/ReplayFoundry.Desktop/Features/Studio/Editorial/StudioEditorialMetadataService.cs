using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
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

internal sealed record StudioGameContextOperationResult(
    GameKnowledgeContextReceipt Receipt,
    string Status);

internal sealed class StudioEditorialMetadataService
{
    private readonly IGenerationOutputEditor? _outputEditor;
    private readonly IGenerationOutputSession? _outputSession;
    private readonly IClipEditorialMetadataGenerationService? _generator;
    private readonly IClipEditorialProfileEditor? _profileEditor;
    private readonly IGenerationGameKnowledgeService? _gameKnowledge;

    public StudioEditorialMetadataService(
        IGenerationOutputEditor? outputEditor,
        IClipEditorialMetadataGenerationService? generator,
        IClipEditorialProfileEditor? profileEditor,
        IGenerationGameKnowledgeService? gameKnowledge = null)
    {
        _outputEditor = outputEditor;
        _outputSession = outputEditor as IGenerationOutputSession;
        _generator = generator;
        _profileEditor = profileEditor;
        _gameKnowledge = gameKnowledge;
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
            ? "This clip does not have a title and description yet."
            : metadata.QualityIssues.Count > 0
                ? "This title and description are usable but could be stronger. Edit them or try another angle."
            : metadata.Readiness switch
            {
                ClipEditorialMetadataReadiness.WorkingLabel =>
                    "A working title and description are ready. You can use them as-is or make them your own.",
                ClipEditorialMetadataReadiness.GroundedDraft =>
                    "A title and description based on this clip are ready. Use them as-is or make changes.",
                ClipEditorialMetadataReadiness.UserEditedDraft =>
                    "Your changes are saved.",
                ClipEditorialMetadataReadiness.UserApproved =>
                    "You marked this title and description as reviewed.",
                _ => "The title and description are ready.",
            };
        if (providerWarning is not null)
        {
            status += providerWarning.Code ==
                ClipEditorialWarningCode.AiProviderUnavailable
                    ? " Local AI was not available, so Replay Foundry used its built-in writer."
                    : " Local AI could not finish, so Replay Foundry used its built-in writer.";
        }
        string draftState = metadata?.Readiness switch
        {
            ClipEditorialMetadataReadiness.WorkingLabel => "Ready",
            ClipEditorialMetadataReadiness.GroundedDraft => "Ready",
            ClipEditorialMetadataReadiness.UserEditedDraft => "Saved",
            ClipEditorialMetadataReadiness.UserApproved => "Reviewed",
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
                ? "The clip or its captions changed after this wording was created. Refresh it so the title and description match what is there now."
                : "This title and description match the current clip and captions.");
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
                "The selected clip does not have an editable title and description.");
        }

        (GenerationOutputProject currentProject,
            GenerationOutputAsset currentAsset) =
            ResolveCurrent(project, asset);
        if (currentProject.IsFinalized ||
            currentAsset.EditorialMetadata is null)
        {
            throw new InvalidOperationException(
                "The selected clip does not have an editable title and description.");
        }

        bool metadataWasCurrent =
            currentAsset.IsEditorialMetadataCurrentForCut;
        ClipEditorialContext currentCutContext =
            currentAsset.CreateCurrentCutEditorialContext();
        ClipEditorialMetadataDraft edited =
            currentAsset.EditorialMetadata.WithUserEdits(
                title,
                description,
                ClipEditorialProfileTags.Parse(tags),
                preservePriorTitleHistory:
                    metadataWasCurrent);
        edited = RemoveStaleGroundingAudit(
            edited,
            currentCutContext);
        _outputEditor.ReplaceAsset(
            currentProject.Id,
            currentAsset.WithCurrentCutEditorialMetadata(
                currentCutContext,
                edited));
    }

    public void MarkReviewed(
        GenerationOutputProject project,
        GenerationOutputAsset asset)
    {
        if (_outputEditor is null)
        {
            throw new InvalidOperationException(
                "The selected clip does not have an editable title and description.");
        }

        (GenerationOutputProject currentProject,
            GenerationOutputAsset currentAsset) =
            ResolveCurrent(project, asset);
        if (currentProject.IsFinalized ||
            currentAsset.EditorialMetadata is null)
        {
            throw new InvalidOperationException(
                "The selected clip does not have an editable title and description.");
        }

        ClipEditorialContext currentCutContext =
            currentAsset.CreateCurrentCutEditorialContext();
        ClipEditorialMetadataDraft reviewed = RemoveStaleGroundingAudit(
            currentAsset.EditorialMetadata.MarkReviewed(),
            currentCutContext);
        _outputEditor.ReplaceAsset(
            currentProject.Id,
            currentAsset.WithCurrentCutEditorialMetadata(
                currentCutContext,
                reviewed));
    }

    public async Task<StudioEditorialRerollResult> RerollAsync(
        GenerationOutputProject project,
        GenerationOutputAsset asset,
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature,
        bool requireAi,
        CancellationToken cancellationToken,
        StudioEditorialVariant variant =
            StudioEditorialVariant.DirectAction)
    {
        if (_generator is null ||
            _outputEditor is null)
        {
            throw new InvalidOperationException(
                "Replay Foundry does not have enough saved information to rewrite this clip yet.");
        }

        (GenerationOutputProject currentProject,
            GenerationOutputAsset currentAsset) =
            ResolveCurrent(project, asset);
        if (currentProject.IsFinalized ||
            currentAsset.EditorialContext is null ||
            currentAsset.EditorialMetadata is null)
        {
            throw new InvalidOperationException(
                "Replay Foundry does not have enough saved information to rewrite this clip yet.");
        }

        var profile = new ClipEditorialProfile(
            audienceAddress,
            namingGuidance,
            descriptionSignature,
            _profileEditor?.Current.DefaultTags ?? []);
        ClipEditorialContext requestedCutContext =
            currentAsset.CreateCurrentCutEditorialContext();
        if (requireAi && _gameKnowledge is not null)
        {
            requestedCutContext = await _gameKnowledge.EnrichAsync(
                requestedCutContext,
                cancellationToken);
        }
        IReadOnlyList<ClipEditorialPriorTitleExclusion> priorTitles =
            currentAsset.IsEditorialMetadataCurrentForCut
                ? currentAsset.EditorialMetadata
                    .CreatePriorTitleExclusions(requestedCutContext)
                : [];
        ClipEditorialGenerationPreference preference = requireAi
            ? ClipEditorialGenerationPreference.AiRequired
            : ClipEditorialGenerationPreference.HeuristicOnly;
        ClipEditorialMetadataDraft rerolled =
            await _generator.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    requestedCutContext,
                    profile,
                    currentAsset.EditorialMetadata.Attempt + 1,
                    preference,
                    currentAsset.SourceMedia,
                    priorAcceptedTitleExclusions: priorTitles)
                    .WithVariantIntent(MapVariant(variant)),
                cancellationToken);
        ClipEditorialMetadataGenerationPolicy.EnsureCompatible(
            preference,
            rerolled,
            "Studio metadata reroll");
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
                "The Studio project finished before the rewrite completed.");
        }
        if (currentAsset.SourceStart != requestedCutContext.SourceStart ||
            currentAsset.SourceEnd != requestedCutContext.SourceEnd)
        {
            throw new InvalidOperationException(
                "The clip start or end changed during the rewrite. " +
                "Replay Foundry kept the newer cut; try the rewrite again when the cut is settled.");
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
                ? "A new title and description are ready."
                : "A new version is ready.");
    }

    private static ClipEditorialVariantIntent MapVariant(
        StudioEditorialVariant variant) =>
        variant switch
        {
            StudioEditorialVariant.DirectAction =>
                ClipEditorialVariantIntent.DirectAction,
            StudioEditorialVariant.SpecificCuriosity =>
                ClipEditorialVariantIntent.SpecificCuriosity,
            StudioEditorialVariant.OutcomeFocused =>
                ClipEditorialVariantIntent.OutcomeFocused,
            _ => throw new ArgumentOutOfRangeException(
                nameof(variant),
                variant,
                "The Studio metadata angle is not defined."),
        };

    private static ClipEditorialMetadataDraft RemoveStaleGroundingAudit(
        ClipEditorialMetadataDraft metadata,
        ClipEditorialContext currentContext)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(currentContext);
        return metadata.GroundingAudit is { } audit &&
               !audit.BriefFingerprint.Equals(
                   currentContext.EditorialBrief.Fingerprint,
                   StringComparison.Ordinal)
            ? metadata.WithoutGroundingAudit()
            : metadata;
    }

    public GameKnowledgeContextReceipt InspectGameContext(
        GenerationOutputAsset? asset)
    {
        if (_gameKnowledge is null || asset?.EditorialContext is null)
        {
            return new GameKnowledgeContextReceipt(
                asset?.EditorialContext?.GameContext.GameName ?? "Gameplay",
                wikidataEntityId: null,
                GameKnowledgeContextFreshness.NotCached,
                retrievedAtUtc: null,
                nextRefreshAtUtc: null,
                canRefresh: false,
                canRemove: false,
                sources: [],
                claims: [],
                components: []);
        }
        return _gameKnowledge.Inspect(asset.CreateCurrentCutEditorialContext());
    }

    public async Task<StudioGameContextOperationResult>
        RefreshGameContextAsync(
            GenerationOutputProject project,
            GenerationOutputAsset asset,
            CancellationToken cancellationToken)
    {
        if (_gameKnowledge is null || _outputEditor is null)
        {
            throw new InvalidOperationException(
                "Public game context is unavailable in this Studio session.");
        }
        (GenerationOutputProject currentProject,
            GenerationOutputAsset currentAsset) = ResolveCurrent(project, asset);
        ClipEditorialContext requested = RequireEditableContext(
            currentProject,
            currentAsset);
        ClipEditorialContext refreshed = await _gameKnowledge.RefreshAsync(
            requested,
            cancellationToken);
        (currentProject, currentAsset) = ResolveCurrent(
            currentProject,
            currentAsset);
        EnsureSameCut(currentProject, currentAsset, requested);
        _outputEditor.ReplaceAsset(
            currentProject.Id,
            currentAsset.WithCurrentCutEditorialMetadata(
                refreshed,
                currentAsset.EditorialMetadata!));
        return new StudioGameContextOperationResult(
            _gameKnowledge.Inspect(refreshed),
            "Public game information was refreshed without scanning the video again.");
    }

    public StudioGameContextOperationResult RemoveCachedGameContext(
        GenerationOutputProject project,
        GenerationOutputAsset asset)
    {
        if (_gameKnowledge is null || _outputEditor is null)
        {
            throw new InvalidOperationException(
                "Public game context is unavailable in this Studio session.");
        }
        (GenerationOutputProject currentProject,
            GenerationOutputAsset currentAsset) = ResolveCurrent(project, asset);
        ClipEditorialContext current = RequireEditableContext(
            currentProject,
            currentAsset);
        ClipEditorialContext removed =
            _gameKnowledge.RemoveCachedContext(current);
        _outputEditor.ReplaceAsset(
            currentProject.Id,
            currentAsset.WithCurrentCutEditorialMetadata(
                removed,
                currentAsset.EditorialMetadata!));
        return new StudioGameContextOperationResult(
            _gameKnowledge.Inspect(removed),
            "Saved public game information was removed. Your video and clip work were kept.");
    }

    public void SaveProfile(
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature)
    {
        if (_profileEditor is null)
        {
            throw new InvalidOperationException(
                "Saved writing preferences are unavailable.");
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

    private static ClipEditorialContext RequireEditableContext(
        GenerationOutputProject project,
        GenerationOutputAsset asset)
    {
        if (project.IsFinalized ||
            asset.EditorialContext is null ||
            asset.EditorialMetadata is null)
        {
            throw new InvalidOperationException(
                "The selected clip has no editable public game context.");
        }
        return asset.CreateCurrentCutEditorialContext();
    }

    private static void EnsureSameCut(
        GenerationOutputProject project,
        GenerationOutputAsset asset,
        ClipEditorialContext requested)
    {
        if (project.IsFinalized ||
            asset.SourceStart != requested.SourceStart ||
            asset.SourceEnd != requested.SourceEnd)
        {
            throw new InvalidOperationException(
                "The clip changed while public game context was refreshing. Replay Foundry kept the newer edit.");
        }
    }

}
