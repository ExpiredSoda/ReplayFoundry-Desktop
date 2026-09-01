using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

public sealed class StudioEditorialMetadataViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly StudioEditorialMetadataService _service;
    private readonly IEditorialRerollPreference _rerollPreference;
    private readonly IStudioEditorialMetadataCorrectionRecorder?
        _preferenceRecorder;
    private readonly DelegateCommand _saveCommand;
    private readonly DelegateCommand _markReviewedCommand;
    private readonly AsyncDelegateCommand _rerollCommand;
    private readonly AsyncDelegateCommand _refreshCurrentCutCommand;
    private readonly AsyncDelegateCommand _refreshGameContextCommand;
    private readonly DelegateCommand _removeCachedGameContextCommand;
    private readonly DelegateCommand _saveProfileCommand;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private CancellationTokenSource? _generationCancellation;
    private readonly object _operationSync = new();
    private TaskCompletionSource<bool>? _generationCompletion;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _tags = string.Empty;
    private string _savedTitle = string.Empty;
    private string _savedDescription = string.Empty;
    private string _savedTags = string.Empty;
    private string _audienceAddress = "Chat";
    private string _namingGuidance = string.Empty;
    private string _descriptionSignature = string.Empty;
    private string _savedAudienceAddress = "Chat";
    private string _savedNamingGuidance = string.Empty;
    private string _savedDescriptionSignature = string.Empty;
    private string _status =
        "Select a generated clip to review its title and description.";
    private string _draftState = "Unavailable";
    private bool _needsCurrentCutRefresh;
    private string _currentCutStatus =
        "Select a clip to edit its title and description.";
    private bool _isGenerating;
    private bool _isGameContextUpdating;
    private bool _isStopping;
    private bool _isHostBusy;
    private bool _isDisposed;
    private GameKnowledgeContextReceipt _gameContextReceipt;
    private StudioEditorialVariantChoice _selectedVariantChoice;

    public StudioEditorialMetadataViewModel(
        IGenerationOutputEditor? outputEditor,
        IClipEditorialMetadataGenerationService? generator,
        IClipEditorialProfileEditor? profileEditor,
        IEditorialRerollPreference? rerollPreference = null,
        IStudioEditorialMetadataCorrectionRecorder? preferenceRecorder = null,
        IGenerationGameKnowledgeService? gameKnowledge = null)
    {
        _service = new StudioEditorialMetadataService(
            outputEditor,
            generator,
            profileEditor,
            gameKnowledge);
        _gameContextReceipt = _service.InspectGameContext(asset: null);
        _rerollPreference = rerollPreference ??
            new EditorialRerollPreferenceState(
                new InMemoryEditorialRerollPreferenceStore());
        VariantChoices =
        [
            new(
                StudioEditorialVariant.DirectAction,
                "Straightforward",
                "Say clearly what happens in the clip."),
            new(
                StudioEditorialVariant.SpecificCuriosity,
                "Curiosity",
                "Create interest without giving away the result."),
            new(
                StudioEditorialVariant.OutcomeFocused,
                "Lead with the result",
                "Start with the clearest visible result."),
        ];
        _selectedVariantChoice = VariantChoices[0];
        _preferenceRecorder = preferenceRecorder;
        _saveCommand = new DelegateCommand(Save, CanSave);
        _markReviewedCommand = new DelegateCommand(
            MarkReviewed,
            CanMarkReviewed);
        _rerollCommand = new AsyncDelegateCommand(
            RerollAsync,
            CanReroll);
        _refreshCurrentCutCommand = new AsyncDelegateCommand(
            RefreshCurrentCutAsync,
            CanRefreshCurrentCut);
        _refreshGameContextCommand = new AsyncDelegateCommand(
            RefreshGameContextAsync,
            CanRefreshGameContext);
        _removeCachedGameContextCommand = new DelegateCommand(
            RemoveCachedGameContext,
            CanRemoveCachedGameContext);
        _saveProfileCommand = new DelegateCommand(
            SaveProfile,
            () => !_isHostBusy &&
                  !IsGenerating);
        _rerollPreference.Changed += RerollPreference_Changed;
        LoadProfile();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        set
        {
            string normalized = value ?? string.Empty;
            if (_title == normalized)
            {
                return;
            }

            _title = normalized;
            NotifyState();
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            string normalized = value ?? string.Empty;
            if (_description == normalized)
            {
                return;
            }

            _description = normalized;
            NotifyState();
        }
    }

    public string Tags
    {
        get => _tags;
        set
        {
            string normalized = value ?? string.Empty;
            if (_tags == normalized)
            {
                return;
            }

            _tags = normalized;
            NotifyState();
        }
    }

    public string AudienceAddress
    {
        get => _audienceAddress;
        set
        {
            string normalized = value ?? string.Empty;
            if (_audienceAddress == normalized)
            {
                return;
            }

            _audienceAddress = normalized;
            NotifyState();
        }
    }

    public string NamingGuidance
    {
        get => _namingGuidance;
        set
        {
            string normalized = value ?? string.Empty;
            if (_namingGuidance == normalized)
            {
                return;
            }

            _namingGuidance = normalized;
            NotifyState();
        }
    }

    public string DescriptionSignature
    {
        get => _descriptionSignature;
        set
        {
            string normalized = value ?? string.Empty;
            if (_descriptionSignature == normalized)
            {
                return;
            }

            _descriptionSignature = normalized;
            NotifyState();
        }
    }

    public string TitleCharacterCount =>
        $"{Title.Length}/{_service.MaximumTitleLength}";

    public string DescriptionCharacterCount =>
        $"{Description.Length}/{_service.MaximumDescriptionLength}";

    public string Status => _status;

    public string DraftState => HasUnsavedChanges
        ? "Unsaved"
        : _draftState;

    public string MetadataOriginText =>
        StudioGameContextPresentation.BuildMetadataOrigin(_asset);

    public string WhyThisTitleText =>
        StudioGameContextPresentation.BuildWhyThisTitle(_asset);

    public bool NeedsCurrentCutRefresh => _needsCurrentCutRefresh;

    public string CurrentCutStatus => _currentCutStatus;

    public bool HasContextReceipt =>
        _asset?.EditorialContext?.EditorialBrief is not null;

    public string ContextUsedSummary =>
        StudioGameContextPresentation.BuildContextUsedSummary(_asset);

    public string ContextAuthoritySummary =>
        StudioGameContextPresentation.BuildContextAuthoritySummary(_asset);

    public bool ContextNeedsReview =>
        StudioGameContextPresentation.IsGroundingReceiptStale(_asset) ||
        _asset?.EditorialMetadata?.GroundingAudit?.NeedsReview == true ||
        _asset?.EditorialContext?.EditorialBrief?.CandidateClaimCount > 0;

    public string ContextReviewSummary =>
        StudioGameContextPresentation.IsGroundingReceiptStale(_asset)
            ? "The clip, captions, or saved game info changed after this was written. Your wording is unchanged; refresh it when you want it to use the update."
            : _asset?.EditorialMetadata?.GroundingAudit?.NeedsReview == true
            ? "This title and description are broad. Check them or try another angle; the clip is still ready to use."
            : _asset?.EditorialContext?.EditorialBrief?.CandidateClaimCount > 0
                ? "Unconfirmed game details, speech hints, and screen text were left out."
                : "Only verified details were used.";

    public string CanonicalGameContextText =>
        _gameContextReceipt.CanonicalGameTitle;

    public string GameContextFreshnessText =>
        StudioGameContextPresentation.BuildFreshnessText(
            _gameContextReceipt);

    public bool HasGameContextSources =>
        _gameContextReceipt.Sources.Count > 0;

    public string GameContextSourcesText =>
        StudioGameContextPresentation.BuildSourceAttributionText(
            _gameContextReceipt);

    public bool HasSupportedGameContextClaims =>
        _gameContextReceipt.SupportedClaims.Count > 0;

    public string SupportedGameContextClaimsText =>
        _gameContextReceipt.SupportedClaims.Count == 0
            ? "No verified public game details were used."
            : string.Join(Environment.NewLine,
                _gameContextReceipt.SupportedClaims.Take(4).Select(claim =>
                    $"{claim.Label}: " +
                    $"{StudioGameContextPresentation.BoundDisplay(claim.Value, 180)} " +
                    $"({claim.SourceTitle})"));

    public bool HasAmbiguousGameContextSuggestions =>
        StudioGameContextPresentation
            .AmbiguousKnowledgeClaims(_asset).Count > 0;

    public string AmbiguousGameContextSuggestionsText =>
        StudioGameContextPresentation
            .AmbiguousKnowledgeClaims(_asset).Count == 0
            ? "No unconfirmed mission, location, or story detail is waiting."
            : string.Join(Environment.NewLine,
                StudioGameContextPresentation
                    .AmbiguousKnowledgeClaims(_asset)
                    .Take(4)
                    .Select(claim =>
                        $"Likely {StudioGameContextPresentation.ClaimLabel(claim.Kind)}: " +
                        StudioGameContextPresentation.BoundDisplay(
                            claim.Value,
                            180)));

    public string GameContextComponentsText =>
        _gameContextReceipt.Components.Count == 0
            ? "No saved game-info sections."
            : string.Join(" · ", _gameContextReceipt.Components.Select(
                static component =>
                    $"{component.Kind}: {component.Completeness}"));

    public bool CanRefreshPublicGameContext =>
        _gameContextReceipt.CanRefresh;

    public bool HasCachedPublicGameContext =>
        _gameContextReceipt.CanRemove;

    public bool IsGameContextUpdating => _isGameContextUpdating;

    public string RefreshCurrentCutText => UsesLocalAiForRerolls
        ? "Rewrite for this clip and captions"
        : "Update for this clip and captions";

    public bool HasUnsavedChanges =>
        !Title.Equals(_savedTitle, StringComparison.Ordinal) ||
        !Description.Equals(_savedDescription, StringComparison.Ordinal) ||
        !Tags.Equals(_savedTags, StringComparison.Ordinal);

    public bool HasUnsavedProfileChanges =>
        !AudienceAddress.Equals(
            _savedAudienceAddress,
            StringComparison.Ordinal) ||
        !NamingGuidance.Equals(
            _savedNamingGuidance,
            StringComparison.Ordinal) ||
        !DescriptionSignature.Equals(
            _savedDescriptionSignature,
            StringComparison.Ordinal);

    public string SaveButtonText => "Save changes";

    public string SaveGuidance => HasUnsavedChanges
        ? "Add to queue will save these changes too."
        : _asset?.HasApprovedEditorialMetadata == true
            ? "Reviewed. You can still make changes."
            : "Ready to use. Review is optional.";

    public bool IsGenerating => _isGenerating || _isGameContextUpdating;

    public bool IsAiAvailable => _service.IsAiAvailable;

    public bool UsesLocalAiForRerolls => _rerollPreference.UseLocalAi;

    public string RerollButtonText => UsesLocalAiForRerolls
        ? "Rewrite with local AI"
        : "Try another angle";

    public string RerollAutomationName => UsesLocalAiForRerolls
        ? "Rewrite title and description with local AI"
        : "Try another title and description angle";

    public string RerollProviderText => HasUnsavedChanges
        ? "Save your changes before trying another angle so they are not replaced."
        : UsesLocalAiForRerolls
            ? IsAiAvailable
                ? "Local AI will write a different version. Your current draft stays in place if it cannot finish."
                : "Local AI is not ready. Check Advanced AI in Settings before trying again."
            : "Replay Foundry will create a quick local rewrite.";

    public IReadOnlyList<StudioEditorialVariantChoice> VariantChoices
    {
        get;
    }

    public StudioEditorialVariantChoice SelectedVariantChoice
    {
        get => _selectedVariantChoice;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!VariantChoices.Contains(value))
            {
                throw new ArgumentException(
                    "The metadata angle must come from the displayed choices.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedVariantChoice, value))
            {
                return;
            }
            _selectedVariantChoice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedVariantDescription));
        }
    }

    public string SelectedVariantDescription =>
        SelectedVariantChoice.Description;

    public ICommand SaveCommand => _saveCommand;

    public ICommand MarkReviewedCommand => _markReviewedCommand;

    public ICommand RerollCommand => _rerollCommand;

    public ICommand RefreshCurrentCutCommand => _refreshCurrentCutCommand;

    public ICommand RefreshGameContextCommand => _refreshGameContextCommand;

    public ICommand RemoveCachedGameContextCommand =>
        _removeCachedGameContextCommand;

    public ICommand SaveProfileCommand => _saveProfileCommand;

    public void Bind(
        GenerationOutputProject? project,
        GenerationOutputAsset? asset)
    {
        _project = project;
        _asset = asset;
        LoadProfile();
        LoadDraft();
    }

    public void SetHostBusy(bool isBusy)
    {
        if (_isHostBusy == isBusy)
        {
            return;
        }

        _isHostBusy = isBusy;
        NotifyState();
    }

    internal StudioPendingEditorialDraft? CapturePendingDraft() =>
        HasUnsavedChanges
            ? new StudioPendingEditorialDraft(Title, Description, Tags)
            : null;

    internal StudioPendingEditorialProfileDraft? CapturePendingProfileDraft() =>
        HasUnsavedProfileChanges
            ? new StudioPendingEditorialProfileDraft(
                AudienceAddress,
                NamingGuidance,
                DescriptionSignature)
            : null;

    internal void RestorePendingDrafts(
        StudioPendingEditorialDraft? metadata,
        StudioPendingEditorialProfileDraft? profile)
    {
        if (_project?.IsFinalized != false)
        {
            return;
        }

        if (metadata is not null)
        {
            _title = metadata.Title;
            _description = metadata.Description;
            _tags = metadata.Tags;
        }
        if (profile is not null)
        {
            _audienceAddress = profile.AudienceAddress;
            _namingGuidance = profile.NamingGuidance;
            _descriptionSignature = profile.DescriptionSignature;
        }
        NotifyState();
    }

    internal bool CanPersistPendingDraft(
        StudioPendingEditorialDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return _service.CanEdit(_project, _asset) &&
               !_isHostBusy &&
               !IsGenerating &&
               !string.IsNullOrWhiteSpace(draft.Title) &&
               draft.Title.Trim().Length <= _service.MaximumTitleLength &&
               !string.IsNullOrWhiteSpace(draft.Description) &&
               draft.Description.Trim().Length <=
                   _service.MaximumDescriptionLength;
    }

    internal bool TryPersistPendingDraft(
        StudioPendingEditorialDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!CanPersistPendingDraft(draft) ||
            _project is null ||
            _asset is null)
        {
            return false;
        }

        try
        {
            string beforeTitle = _savedTitle;
            string beforeDescription = _savedDescription;
            string beforeTags = _savedTags;
            _service.Save(
                _project,
                _asset,
                draft.Title,
                draft.Description,
                draft.Tags);
            _savedTitle = draft.Title;
            _savedDescription = draft.Description;
            _savedTags = draft.Tags;
            _draftState = "Saved";
            _status =
                "Title and description saved.";
            _preferenceRecorder?.TryRecordCorrection(
                beforeTitle,
                beforeDescription,
                beforeTags,
                draft.Title,
                draft.Description,
                draft.Tags);
            NotifyState();
            return true;
        }
        catch (Exception exception)
        {
            _status = exception.Message;
            NotifyState();
            return false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _rerollPreference.Changed -= RerollPreference_Changed;
        CancellationTokenSource? cancellation;
        lock (_operationSync)
        {
            cancellation = _generationCancellation;
        }
        TryCancel(cancellation);
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? activeCancellation;
        Task completion;
        lock (_operationSync)
        {
            _isStopping = true;
            activeCancellation = _generationCancellation;
            completion = _generationCompletion?.Task ?? Task.CompletedTask;
        }
        TryCancel(activeCancellation);
        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Save()
    {
        if (!CanSave() ||
            _project is null ||
            _asset is null)
        {
            return;
        }

        try
        {
            string beforeTitle = _savedTitle;
            string beforeDescription = _savedDescription;
            string beforeTags = _savedTags;
            _service.Save(
                _project,
                _asset,
                Title,
                Description,
                Tags);
            _savedTitle = Title;
            _savedDescription = Description;
            _savedTags = Tags;
            _draftState = "Saved";
            _status =
                "Title and description saved.";
            _preferenceRecorder?.TryRecordCorrection(
                beforeTitle,
                beforeDescription,
                beforeTags,
                Title,
                Description,
                Tags);
        }
        catch (Exception exception)
        {
            _status = exception.Message;
        }

        NotifyState();
    }

    private void MarkReviewed()
    {
        if (!CanMarkReviewed() || _project is null || _asset is null)
        {
            return;
        }

        try
        {
            _service.MarkReviewed(_project, _asset);
            _draftState = "Reviewed";
            _status = "Marked as reviewed.";
        }
        catch (Exception exception)
        {
            _status = exception.Message;
        }

        NotifyState();
    }

    private async Task RerollAsync()
    {
        if (_project is null ||
            _asset is null ||
            _project.IsFinalized)
        {
            return;
        }

        var generationCancellation = new CancellationTokenSource();
        var generationCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_operationSync)
        {
            if (_isDisposed || _isStopping)
            {
                generationCancellation.Dispose();
                return;
            }
            _generationCancellation = generationCancellation;
            _generationCompletion = generationCompletion;
        }
        _isGenerating = true;
        bool requireAi = _rerollPreference.UseLocalAi;
        _status = requireAi
                ? "Asking local AI for another angle."
                : "Writing another local version.";
        NotifyState();
        try
        {
            StudioEditorialRerollResult result =
                await _service.RerollAsync(
                    _project,
                    _asset,
                    AudienceAddress,
                    NamingGuidance,
                    DescriptionSignature,
                    requireAi,
                    generationCancellation.Token,
                    SelectedVariantChoice.Value);
            generationCancellation.Token.ThrowIfCancellationRequested();
            _status = result.Status;
        }
        catch (OperationCanceledException)
        {
            if (!_isDisposed)
            {
                _status = "Rewrite cancelled.";
            }
        }
        catch (Exception exception)
        {
            if (!_isDisposed)
            {
                _status = exception.Message;
            }
        }
        finally
        {
            lock (_operationSync)
            {
                if (ReferenceEquals(
                        _generationCancellation,
                        generationCancellation))
                {
                    _generationCancellation = null;
                }
                if (ReferenceEquals(
                        _generationCompletion,
                        generationCompletion))
                {
                    _generationCompletion = null;
                }
            }
            generationCancellation.Dispose();
            _isGenerating = false;
            if (!_isDisposed)
            {
                NotifyState();
            }
            generationCompletion.TrySetResult(true);
        }
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed between capture and cancellation.
        }
    }

    private Task RefreshCurrentCutAsync() => RerollAsync();

    private async Task RefreshGameContextAsync()
    {
        if (_project is null || _asset is null)
        {
            return;
        }
        var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_operationSync)
        {
            if (_isDisposed || _isStopping)
            {
                cancellation.Dispose();
                return;
            }
            _generationCancellation = cancellation;
            _generationCompletion = completion;
        }
        _isGameContextUpdating = true;
        _status = "Refreshing saved public game info.";
        NotifyState();
        try
        {
            StudioGameContextOperationResult result =
                await _service.RefreshGameContextAsync(
                    _project,
                    _asset,
                    cancellation.Token);
            _gameContextReceipt = result.Receipt;
            _status = result.Status;
        }
        catch (OperationCanceledException)
        {
            if (!_isDisposed)
            {
                _status = "Game-info refresh cancelled.";
            }
        }
        catch (Exception exception)
        {
            if (!_isDisposed)
            {
                _status = exception.Message;
            }
        }
        finally
        {
            CompleteContextOperation(cancellation, completion);
        }
    }

    private void CompleteContextOperation(
        CancellationTokenSource cancellation,
        TaskCompletionSource<bool> completion)
    {
        lock (_operationSync)
        {
            if (ReferenceEquals(_generationCancellation, cancellation))
            {
                _generationCancellation = null;
            }
            if (ReferenceEquals(_generationCompletion, completion))
            {
                _generationCompletion = null;
            }
        }
        cancellation.Dispose();
        _isGameContextUpdating = false;
        if (!_isDisposed)
        {
            NotifyState();
        }
        completion.TrySetResult(true);
    }

    private void RemoveCachedGameContext()
    {
        if (_project is null || _asset is null)
        {
            return;
        }
        try
        {
            StudioGameContextOperationResult result =
                _service.RemoveCachedGameContext(_project, _asset);
            _gameContextReceipt = result.Receipt;
            _status = result.Status;
        }
        catch (Exception exception)
        {
            _status = exception.Message;
        }
        NotifyState();
    }

    private void SaveProfile()
    {
        try
        {
            _service.SaveProfile(
                AudienceAddress,
                NamingGuidance,
                DescriptionSignature);
            _savedAudienceAddress = AudienceAddress;
            _savedNamingGuidance = NamingGuidance;
            _savedDescriptionSignature = DescriptionSignature;
            _status =
                "Reusable wording saved for future suggestions.";
        }
        catch (Exception exception)
        {
            _status = exception.Message;
        }

        NotifyState();
    }

    private bool CanSave() =>
        _service.CanEdit(_project, _asset) &&
        !_isHostBusy &&
        !IsGenerating &&
        !string.IsNullOrWhiteSpace(Title) &&
        Title.Trim().Length <= _service.MaximumTitleLength &&
        !string.IsNullOrWhiteSpace(Description) &&
        Description.Trim().Length <= _service.MaximumDescriptionLength;

    private bool CanMarkReviewed() =>
        _service.CanEdit(_project, _asset) &&
        !_isHostBusy &&
        !IsGenerating &&
        !HasUnsavedChanges &&
        !NeedsCurrentCutRefresh &&
        !_draftState.Equals("Reviewed", StringComparison.Ordinal);

    private bool CanReroll() =>
        !_isStopping &&
        _service.CanEdit(_project, _asset) &&
        !_isHostBusy &&
        !IsGenerating &&
        !HasUnsavedChanges;

    private bool CanRefreshCurrentCut() =>
        NeedsCurrentCutRefresh &&
        CanReroll();

    private bool CanRefreshGameContext() =>
        !_isStopping &&
        !_isGameContextUpdating &&
        !_isHostBusy &&
        !IsGenerating &&
        _gameContextReceipt.CanRefresh &&
        _service.CanEdit(_project, _asset);

    private bool CanRemoveCachedGameContext() =>
        !_isStopping &&
        !_isGameContextUpdating &&
        !_isHostBusy &&
        !IsGenerating &&
        _gameContextReceipt.CanRemove &&
        _service.CanEdit(_project, _asset);

    private void RerollPreference_Changed(object? sender, EventArgs args) =>
        NotifyState();

    private void LoadDraft()
    {
        StudioEditorialDraftSnapshot snapshot =
            _service.LoadDraft(_asset);
        _title = snapshot.Title;
        _description = snapshot.Description;
        _tags = snapshot.Tags;
        _savedTitle = snapshot.Title;
        _savedDescription = snapshot.Description;
        _savedTags = snapshot.Tags;
        _status = snapshot.Status;
        _draftState = snapshot.DraftState;
        _needsCurrentCutRefresh = snapshot.NeedsCurrentCutRefresh;
        _currentCutStatus = snapshot.CurrentCutStatus;
        _gameContextReceipt = _service.InspectGameContext(_asset);
        NotifyState();
    }

    private void LoadProfile()
    {
        StudioEditorialProfileSnapshot snapshot =
            _service.LoadProfile();
        _audienceAddress = snapshot.AudienceAddress;
        _namingGuidance = snapshot.NamingGuidance;
        _descriptionSignature = snapshot.DescriptionSignature;
        _savedAudienceAddress = snapshot.AudienceAddress;
        _savedNamingGuidance = snapshot.NamingGuidance;
        _savedDescriptionSignature = snapshot.DescriptionSignature;
    }

    private void NotifyState()
    {
        foreach (string propertyName in new[]
        {
            nameof(Title),
            nameof(Description),
            nameof(Tags),
            nameof(AudienceAddress),
            nameof(NamingGuidance),
            nameof(DescriptionSignature),
            nameof(TitleCharacterCount),
            nameof(DescriptionCharacterCount),
            nameof(Status),
            nameof(DraftState),
            nameof(MetadataOriginText),
            nameof(WhyThisTitleText),
            nameof(NeedsCurrentCutRefresh),
            nameof(CurrentCutStatus),
            nameof(HasContextReceipt),
            nameof(ContextUsedSummary),
            nameof(ContextAuthoritySummary),
            nameof(ContextNeedsReview),
            nameof(ContextReviewSummary),
            nameof(CanonicalGameContextText),
            nameof(GameContextFreshnessText),
            nameof(HasGameContextSources),
            nameof(GameContextSourcesText),
            nameof(HasSupportedGameContextClaims),
            nameof(SupportedGameContextClaimsText),
            nameof(HasAmbiguousGameContextSuggestions),
            nameof(AmbiguousGameContextSuggestionsText),
            nameof(GameContextComponentsText),
            nameof(CanRefreshPublicGameContext),
            nameof(HasCachedPublicGameContext),
            nameof(IsGameContextUpdating),
            nameof(RefreshCurrentCutText),
            nameof(HasUnsavedChanges),
            nameof(HasUnsavedProfileChanges),
            nameof(SaveButtonText),
            nameof(SaveGuidance),
            nameof(IsGenerating),
            nameof(IsAiAvailable),
            nameof(UsesLocalAiForRerolls),
            nameof(RerollButtonText),
            nameof(RerollAutomationName),
            nameof(RerollProviderText),
            nameof(VariantChoices),
            nameof(SelectedVariantChoice),
            nameof(SelectedVariantDescription),
        })
        {
            OnPropertyChanged(propertyName);
        }

        _saveCommand.RaiseCanExecuteChanged();
        _markReviewedCommand.RaiseCanExecuteChanged();
        _rerollCommand.RaiseCanExecuteChanged();
        _refreshCurrentCutCommand.RaiseCanExecuteChanged();
        _refreshGameContextCommand.RaiseCanExecuteChanged();
        _removeCachedGameContextCommand.RaiseCanExecuteChanged();
        _saveProfileCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
