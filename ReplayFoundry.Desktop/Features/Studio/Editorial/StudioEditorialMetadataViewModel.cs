using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

internal sealed record StudioPendingEditorialDraft(
    string Title,
    string Description,
    string Tags);

internal sealed record StudioPendingEditorialProfileDraft(
    string AudienceAddress,
    string NamingGuidance,
    string DescriptionSignature);

public sealed class StudioEditorialMetadataViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly StudioEditorialMetadataService _service;
    private readonly IEditorialRerollPreference _rerollPreference;
    private readonly IStudioEditorialMetadataCorrectionRecorder?
        _preferenceRecorder;
    private readonly DelegateCommand _saveCommand;
    private readonly AsyncDelegateCommand _rerollCommand;
    private readonly AsyncDelegateCommand _refreshCurrentCutCommand;
    private readonly DelegateCommand _saveProfileCommand;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private CancellationTokenSource? _generationCancellation;
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
        "Select a generated clip to inspect its metadata coverage.";
    private bool _isGenerating;
    private bool _isHostBusy;
    private bool _isDisposed;

    public StudioEditorialMetadataViewModel(
        IGenerationOutputEditor? outputEditor,
        IClipEditorialMetadataGenerationService? generator,
        IClipEditorialProfileEditor? profileEditor,
        IEditorialRerollPreference? rerollPreference = null,
        IStudioEditorialMetadataCorrectionRecorder? preferenceRecorder = null)
    {
        _service = new StudioEditorialMetadataService(
            outputEditor,
            generator,
            profileEditor);
        _rerollPreference = rerollPreference ??
            new EditorialRerollPreferenceState(
                new InMemoryEditorialRerollPreferenceStore());
        _preferenceRecorder = preferenceRecorder;
        _saveCommand = new DelegateCommand(Save, CanSave);
        _rerollCommand = new AsyncDelegateCommand(
            RerollAsync,
            CanReroll);
        _refreshCurrentCutCommand = new AsyncDelegateCommand(
            RefreshCurrentCutAsync,
            CanRefreshCurrentCut);
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

    public string DraftState => _draftState;

    public bool NeedsCurrentCutRefresh => _needsCurrentCutRefresh;

    public string CurrentCutStatus => _currentCutStatus;

    public string RefreshCurrentCutText => UsesLocalAiForRerolls
        ? "Rewrite this cut with local AI"
        : "Update text for this cut";

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

    public string SaveButtonText => "Save metadata";

    public string SaveGuidance => HasUnsavedChanges
        ? "Save these metadata changes before rerolling or adding this clip to the render queue."
        : "The render queue uses this saved copy. You can add an AI draft to the queue and keep editing it later.";

    public bool IsGenerating => _isGenerating;

    public bool IsAiAvailable => _service.IsAiAvailable;

    public bool UsesLocalAiForRerolls => _rerollPreference.UseLocalAi;

    public string RerollButtonText => UsesLocalAiForRerolls
        ? "Rewrite metadata with local AI"
        : "Create another metadata draft";

    public string RerollAutomationName => UsesLocalAiForRerolls
        ? "Rewrite clip metadata with required local AI"
        : "Create another deterministic metadata draft";

    public string RerollProviderText => HasUnsavedChanges
        ? "Save your title, description, and tag edits before creating another draft. This keeps a reroll from replacing work you have not saved."
        : UsesLocalAiForRerolls
            ? IsAiAvailable
                ? "Settings selects qualified local AI for this reroll. If generation fails, no deterministic wording is substituted."
                : "Local AI is selected in Settings, but it is not ready yet. Try again after repairing the AI model in Settings."
            : "The fast local title writer is selected for this rewrite. The larger AI model will not run.";

    public ICommand SaveCommand => _saveCommand;

    public ICommand RerollCommand => _rerollCommand;

    public ICommand RefreshCurrentCutCommand => _refreshCurrentCutCommand;

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
            _status =
                "Metadata saved before opening another Studio project.";
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
        _generationCancellation?.Cancel();
        _generationCancellation?.Dispose();
        _generationCancellation = null;
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
            _status =
                "Metadata saved. The render queue will use this title, description, and tags.";
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

    private async Task RerollAsync()
    {
        if (_project is null ||
            _asset is null ||
            _project.IsFinalized)
        {
            return;
        }

        _generationCancellation?.Dispose();
        _generationCancellation = new CancellationTokenSource();
        _isGenerating = true;
        bool requireAi = _rerollPreference.UseLocalAi;
        _status = requireAi
                ? "Asking the qualified local model to rewrite this metadata."
                : "Creating another deterministic metadata draft.";
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
                    _generationCancellation.Token);
            _status = result.Status;
        }
        catch (OperationCanceledException)
        {
            _status = "Metadata generation was cancelled.";
        }
        catch (Exception exception)
        {
            _status = exception.Message;
        }
        finally
        {
            _generationCancellation?.Dispose();
            _generationCancellation = null;
            _isGenerating = false;
            NotifyState();
        }
    }

    private Task RefreshCurrentCutAsync() => RerollAsync();

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
                "Reusable metadata wording is saved for future suggestions in this app session.";
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

    private bool CanReroll() =>
        _service.CanEdit(_project, _asset) &&
        !_isHostBusy &&
        !IsGenerating &&
        !HasUnsavedChanges;

    private bool CanRefreshCurrentCut() =>
        NeedsCurrentCutRefresh &&
        CanReroll();

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
            nameof(NeedsCurrentCutRefresh),
            nameof(CurrentCutStatus),
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
        })
        {
            OnPropertyChanged(propertyName);
        }

        _saveCommand.RaiseCanExecuteChanged();
        _rerollCommand.RaiseCanExecuteChanged();
        _refreshCurrentCutCommand.RaiseCanExecuteChanged();
        _saveProfileCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
