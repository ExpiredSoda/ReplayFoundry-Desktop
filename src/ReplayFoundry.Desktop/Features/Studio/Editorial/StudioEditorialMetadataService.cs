using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.Media;
using System.IO;
using System.Text.Json;

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
    string DescriptionSignature,
    ClipEditorialCopyObjective CopyObjective =
        ClipEditorialCopyObjective.BalancedActionAndCommentary,
    string Tone = "Natural");

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

    public bool CanGenerate => _generator is not null;
    public bool IsAiAvailable => _generator?.IsAiAvailable == true;

    public string? AiUnavailableReason => _generator?.AiUnavailableReason;

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
            : StudioEditorialDraftPresentation.IsUnwritten(asset)
                ? "Write a title and description below, or use the writer to create a first draft for this clip."
            : metadata.QualityIssues.Count > 0
                ? string.Join(Environment.NewLine, metadata.QualityIssues
                    .Select(ClipEditorialMetadataReview.Describe)
                    .Distinct(StringComparer.Ordinal))
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
        string draftState = metadata?.Readiness == ClipEditorialMetadataReadiness.UserApproved
            ? "Reviewed"
            : metadata?.QualityIssues.Count > 0
            ? "Check copy"
            : metadata?.Readiness switch
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
                ? asset?.EditorialAuthoredContextRevision is null
                    ? "Review and save this older wording against the current clip and captions, or refresh it."
                    : "The clip or its captions changed after this wording was created. Refresh it so the title and description match what is there now."
                : "This wording was saved for the current clip and captions.");
    }

    public StudioEditorialProfileSnapshot LoadProfile()
    {
        ClipEditorialProfile profile =
            _profileEditor?.Current ?? ClipEditorialProfile.Default;
        return new StudioEditorialProfileSnapshot(
            profile.AudienceAddress,
            profile.NamingGuidance ?? string.Empty,
            profile.ReusableDescriptionSignature ?? string.Empty,
            profile.CopyObjective, profile.DefaultTone);
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
        edited = edited.RememberPreviousCopy(currentAsset.EditorialMetadata,
            currentAsset.EditorialAuthoredContextRevision ?? StudioEditorialContextRevision.UnknownAuthoredContext);
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

    public async Task<StudioEditorialRerollResult> RerollMontageAsync(GenerationOutputProject project, CancellationToken cancellationToken)
    {
        if (_generator is null || _outputEditor is not IGenerationTimelineEditor timeline)
            throw new InvalidOperationException("The montage writer is unavailable.");
        string fingerprint = project.MontageFingerprint;
        var requests = MontageEditorialRequests.Create(project, _profileEditor?.Current ?? ClipEditorialProfile.Default);
        var copy = await _generator.GenerateMontageAsync(requests, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        timeline.SetMontageMetadata(project.Id, copy, fingerprint);
        return new(true, "The whole montage has a title and description. Review the sequence before publishing.");
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
            StudioEditorialVariant.DirectAction,
        bool keepTitle = false,
        bool keepDescription = false,
        string tone = "Natural",
        ClipEditorialWritingAction action = ClipEditorialWritingAction.NewAngle)
    {
        if (keepTitle && keepDescription) throw new InvalidOperationException("Unlock a field before rewriting.");
        using IDisposable priority = MediaWorkBudget.WithPriority(MediaWorkPriority.Foreground);
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
            _profileEditor?.Current.DefaultTags ?? [],
            _profileEditor?.Current.VoicePerspective ??
                ClipEditorialVoicePerspective.CreatorFirstPerson,
            _profileEditor?.Current.CopyObjective ??
                ClipEditorialCopyObjective.BalancedActionAndCommentary);
        ClipEditorialContext requestedCutContext =
            currentAsset.CreateCurrentCutEditorialContext();
        string startingContextRevision = StudioEditorialContextRevision.Create(requestedCutContext);
        var startingCopy = currentAsset.EditorialMetadata;
        var startingDefaults = _profileEditor?.Current;
        requestedCutContext = requestedCutContext.PrepareForEditorialGeneration();
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
                    .WithVariantIntent(MapVariant(variant)).WithTone(tone)
                    .WithWriting(StudioEditorialDraftPresentation.IsUnwritten(currentAsset) ||
                        !currentAsset.IsEditorialMetadataCurrentForCut && !keepTitle && !keepDescription ? null :
                        new(action, startingCopy.Title, startingCopy.Description, keepTitle, keepDescription,
                        Array.AsReadOnly(startingCopy.CopyVersions.Where(version => version.ContextFingerprint ==
                            currentAsset.EditorialAuthoredContextRevision).TakeLast(8).ToArray()))),
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
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(currentAsset.EditorialMetadata, startingCopy))
            throw new InvalidOperationException("The saved wording changed during generation. Your newer wording was kept.");
        if (!ReferenceEquals(startingDefaults, _profileEditor?.Current))
            throw new InvalidOperationException("The writing defaults changed during generation. Your saved wording was kept; try again with the new defaults.");
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
        if (!StudioEditorialContextRevision.Create(currentAsset.CreateCurrentCutEditorialContext()).Equals(
            startingContextRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The captions or clip context changed during the rewrite. Replay Foundry kept the newer edits; try the rewrite again.");
        }
        if (currentAsset.EditorialMetadata is { } previous)
        {
            if (keepTitle && rerolled.Title != previous.Title || keepDescription && rerolled.Description != previous.Description)
                rerolled = rerolled.WithUserEdits(keepTitle ? previous.Title : rerolled.Title,
                    keepDescription ? previous.Description : rerolled.Description, rerolled.Tags);
            rerolled = rerolled.RememberPreviousCopy(previous,
                currentAsset.EditorialAuthoredContextRevision ?? StudioEditorialContextRevision.UnknownAuthoredContext);
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
            keepTitle || keepDescription ? "Unlocked wording updated; your locked field was preserved. Review how the two read together." : isAiAssisted
                ? "A new title and description are ready."
                : "A new version is ready.");
    }

    internal IReadOnlyList<ClipEditorialAlternative> Alternatives(GenerationOutputAsset? asset,
        string audience, string naming, string signature, string tone)
    {
        if (asset?.IsEditorialMetadataCurrentForCut != true || asset.EditorialMetadata is not { } metadata) return [];
        var profile = new ClipEditorialProfile(audience, naming, signature, _profileEditor?.Current.DefaultTags ?? [],
            _profileEditor?.Current.VoicePerspective ?? ClipEditorialVoicePerspective.CreatorFirstPerson,
            _profileEditor?.Current.CopyObjective ?? ClipEditorialCopyObjective.BalancedActionAndCommentary);
        string fingerprint = ClipEditorialAlternative.ProfileKey(profile, tone);
        return metadata.Alternatives.Where(choice => choice.ProfileFingerprint == fingerprint &&
            (choice.Title != metadata.Title || choice.Description != metadata.Description)).ToArray();
    }

    internal void SelectAlternative(GenerationOutputProject project, GenerationOutputAsset asset,
        ClipEditorialAlternative choice, string audience, string naming, string signature, string tone,
        bool keepTitle, bool keepDescription)
    {
        var (currentProject, currentAsset) = ResolveCurrent(project, asset);
        if (_outputEditor is null || currentProject.IsFinalized ||
            !Alternatives(currentAsset, audience, naming, signature, tone).Contains(choice) ||
            keepTitle && choice.Title != currentAsset.EditorialMetadata!.Title ||
            keepDescription && choice.Description != currentAsset.EditorialMetadata!.Description)
            throw new InvalidOperationException("This alternative no longer matches the clip, writing preferences or locked fields.");
        var metadata = currentAsset.EditorialMetadata!;
        var context = currentAsset.CreateCurrentCutEditorialContext();
        if (!AlternativeSourceIsCurrent(context))
            throw new InvalidOperationException("The original recording has changed or is unavailable. Rewrite for the current clip before choosing an alternative.");
        var selected = metadata.SelectAlternative(choice,
            currentAsset.EditorialAuthoredContextRevision ?? StudioEditorialContextRevision.UnknownAuthoredContext);
        _outputEditor.ReplaceAsset(currentProject.Id, currentAsset.WithCurrentCutEditorialMetadata(context, selected));
    }

    private static bool AlternativeSourceIsCurrent(ClipEditorialContext context)
    {
        var binding = context.Evidence.SingleOrDefault(item => item.Id == "scene-review-source-binding");
        if (binding is null) return false;
        try
        {
            using var document = JsonDocument.Parse(binding.Description);
            var row = document.RootElement;
            var source = new FileInfo(context.SourceFullPath);
            return source.Exists && row.GetProperty("start").GetInt64() == context.SourceStart.Ticks &&
                row.GetProperty("end").GetInt64() == context.SourceEnd.Ticks &&
                row.GetProperty("length").GetInt64() == source.Length &&
                row.GetProperty("modified").GetInt64() == source.LastWriteTimeUtc.Ticks &&
                source.FullName.Equals(row.GetProperty("source").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        { return false; }
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
            StudioEditorialVariant.ConcreteDetail =>
                ClipEditorialVariantIntent.ConcreteDetail,
            StudioEditorialVariant.CommentaryLed =>
                ClipEditorialVariantIntent.CommentaryLed,
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
                currentAsset.EditorialMetadata!, markAuthoredForCurrentContext: false));
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
                currentAsset.EditorialMetadata!, markAuthoredForCurrentContext: false));
        return new StudioGameContextOperationResult(
            _gameKnowledge.Inspect(removed),
            "Saved public game information was removed. Your video and clip work were kept.");
    }

    public void SaveProfile(
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature,
        string tone = "Natural")
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
                _profileEditor.Current.VoicePerspective,
                _profileEditor.Current.CopyObjective, tone));
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
