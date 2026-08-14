using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Publish.Editorial;

public sealed class PublishEditorialMetadataViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly IPublishEditorialMetadataService? _service;
    private readonly Action<PublishEditorialRerollResult> _applyDraft;
    private readonly Func<PublishEditorialMetadataSnapshot> _currentMetadata;
    private readonly Action _persistDraft;
    private readonly Func<bool> _isHostBusy;
    private readonly IEditorialRerollPreference _rerollPreference;
    private readonly AsyncDelegateCommand _rerollCommand;
    private LibraryMediaAsset? _asset;
    private CancellationTokenSource? _cancellation;
    private string _audienceAddress = "Chat";
    private string _namingGuidance = string.Empty;
    private string _descriptionSignature = string.Empty;
    private string _status =
        "Edit the prepared wording directly, or make a different version with the same saved video context.";
    private int? _lastCompletedAttempt;
    private IReadOnlyList<string> _priorAcceptedTitles = [];
    private bool _isGenerating;
    private bool _canRerollSelected;
    private bool _isDisposed;

    internal PublishEditorialMetadataViewModel(
        IPublishEditorialMetadataService? service,
        Action<PublishEditorialRerollResult> applyDraft,
        Func<PublishEditorialMetadataSnapshot> currentMetadata,
        Action persistDraft,
        Func<bool> isHostBusy,
        IEditorialRerollPreference? rerollPreference = null)
    {
        _service = service;
        _applyDraft = applyDraft ??
            throw new ArgumentNullException(nameof(applyDraft));
        _currentMetadata = currentMetadata ??
            throw new ArgumentNullException(nameof(currentMetadata));
        _persistDraft = persistDraft ??
            throw new ArgumentNullException(nameof(persistDraft));
        _isHostBusy = isHostBusy ??
            throw new ArgumentNullException(nameof(isHostBusy));
        _rerollPreference = rerollPreference ??
            new EditorialRerollPreferenceState(
                new InMemoryEditorialRerollPreferenceStore());
        _rerollCommand = new AsyncDelegateCommand(
            RerollAsync,
            CanReroll);
        _rerollPreference.Changed += RerollPreference_Changed;

        if (_service is not null)
        {
            PublishEditorialProfileSnapshot profile = _service.LoadProfile();
            _audienceAddress = profile.AudienceAddress;
            _namingGuidance = profile.NamingGuidance;
            _descriptionSignature = profile.DescriptionSignature;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AudienceAddress
    {
        get => _audienceAddress;
        set
        {
            string normalized = value ?? string.Empty;
            if (_audienceAddress == normalized) return;
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
            if (_namingGuidance == normalized) return;
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
            if (_descriptionSignature == normalized) return;
            _descriptionSignature = normalized;
            NotifyState();
        }
    }

    public bool IsGenerating => _isGenerating;

    public bool IsAiAvailable => _service?.IsAiAvailable == true;

    public bool UsesLocalAiForRerolls => _rerollPreference.UseLocalAi;

    public bool CanRerollSelected =>
        _canRerollSelected;

    public string Status => _status;

    internal int? LastCompletedAttempt => _lastCompletedAttempt;

    internal IReadOnlyList<string> PriorAcceptedTitles =>
        _priorAcceptedTitles;

    internal void RetainReplacedTitle(string? title)
    {
        _priorAcceptedTitles = PublishEditorialTitleHistory.Merge(
            _priorAcceptedTitles,
            title);
    }

    public string AvailabilityText => CanRerollSelected
        ? UsesLocalAiForRerolls
            ? IsAiAvailable
                ? "Ready to make a genuinely different title and description from the same video context."
                : "The writing model needs attention before it can make another version."
            : "Ready to make a quick alternate version on this PC."
        : "This video does not have the saved source context needed for a grounded reroll. You can still edit and save every field.";

    public string RerollButtonText => "Make a different version";

    public string RerollAutomationName => UsesLocalAiForRerolls
        ? "Create another YouTube metadata draft with required local AI"
        : "Create another deterministic YouTube metadata draft";

    public ICommand RerollCommand => _rerollCommand;

    internal void BindAsset(
        LibraryMediaAsset? asset,
        int? lastCompletedAttempt = null,
        IReadOnlyList<string>? priorAcceptedTitles = null)
    {
        if (lastCompletedAttempt is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastCompletedAttempt));
        }
        _cancellation?.Cancel();
        _asset = asset;
        _canRerollSelected = asset is not null &&
            _service?.CanReroll(asset) == true;
        _lastCompletedAttempt = lastCompletedAttempt;
        _priorAcceptedTitles = PublishEditorialTitleHistory.Merge(
            priorAcceptedTitles);
        _status = CanRerollSelected
            ? "Keep this wording or make a different version with one click."
            : "This video does not have the saved source context needed for a grounded reroll. You can still edit and save every field.";
        NotifyState();
    }

    internal void LoadDraftContext(
        string audienceAddress,
        string namingGuidance,
        string descriptionSignature)
    {
        _audienceAddress = audienceAddress;
        _namingGuidance = namingGuidance;
        _descriptionSignature = descriptionSignature;
        NotifyState();
    }

    internal void ResetContextToProfile()
    {
        PublishEditorialProfileSnapshot profile =
            _service?.LoadProfile() ??
            new PublishEditorialProfileSnapshot(
                "Chat",
                string.Empty,
                string.Empty);
        LoadDraftContext(
            profile.AudienceAddress,
            profile.NamingGuidance,
            profile.DescriptionSignature);
    }

    internal void RefreshHostState()
    {
        _rerollCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _rerollPreference.Changed -= RerollPreference_Changed;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task RerollAsync()
    {
        if (_asset is null || _service is null || !CanRerollSelected)
        {
            return;
        }

        LibraryMediaAsset selected = _asset;
        PublishEditorialRerollSnapshot requestSnapshot =
            CaptureRerollSnapshot(selected.Id);
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _isGenerating = true;
        bool requireAi = _rerollPreference.UseLocalAi;
        _status = requireAi
            ? "Creating a different title and description…"
            : "Creating a different version…";
        NotifyState();
        bool completed = false;
        try
        {
            PublishEditorialRerollResult result = await _service.RerollAsync(
                selected,
                requestSnapshot.AudienceAddress,
                requestSnapshot.NamingGuidance,
                requestSnapshot.DescriptionSignature,
                _lastCompletedAttempt,
                requestSnapshot.Metadata.Title,
                _priorAcceptedTitles,
                requireAi,
                _cancellation.Token);
            if (_asset is null || !_asset.Id.Equals(
                    selected.Id,
                    StringComparison.Ordinal))
            {
                _status =
                    "The selected Library video changed, so Replay Foundry discarded the completed metadata draft.";
                return;
            }
            if (!requestSnapshot.Equals(CaptureRerollSnapshot(selected.Id)))
            {
                _status =
                    "You changed this video's title, description, tags, or wording context while the new draft " +
                    "was being created. Replay Foundry kept your edits and discarded the older result. " +
                    "Choose create another draft again when you are ready.";
                return;
            }
            _applyDraft(result);
            _lastCompletedAttempt = result.Attempt;
            _priorAcceptedTitles = result.PriorAcceptedTitles;
            _status = result.Status;
            completed = true;
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
            _cancellation?.Dispose();
            _cancellation = null;
            _isGenerating = false;
            NotifyState();
        }

        if (completed)
        {
            _persistDraft();
        }
    }

    private bool CanReroll() =>
        CanRerollSelected &&
        !IsGenerating &&
        !_isHostBusy() &&
        !string.IsNullOrWhiteSpace(AudienceAddress) &&
        AudienceAddress.Trim().Length <= 40 &&
        NamingGuidance.Trim().Length <= 300 &&
        DescriptionSignature.Trim().Length <= 1_500;

    private PublishEditorialRerollSnapshot CaptureRerollSnapshot(
        string assetId) =>
        new(
            assetId,
            _currentMetadata(),
            AudienceAddress,
            NamingGuidance,
            DescriptionSignature);

    private void RerollPreference_Changed(object? sender, EventArgs args) =>
        NotifyState();

    private void NotifyState()
    {
        foreach (string name in new[]
        {
            nameof(AudienceAddress),
            nameof(NamingGuidance),
            nameof(DescriptionSignature),
            nameof(IsGenerating),
            nameof(IsAiAvailable),
            nameof(UsesLocalAiForRerolls),
            nameof(CanRerollSelected),
            nameof(Status),
            nameof(AvailabilityText),
            nameof(RerollButtonText),
            nameof(RerollAutomationName),
        })
        {
            OnPropertyChanged(name);
        }
        _rerollCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

internal sealed record PublishEditorialMetadataSnapshot(
    string Title,
    string Description,
    string Tags);

internal sealed record PublishEditorialRerollSnapshot(
    string AssetId,
    PublishEditorialMetadataSnapshot Metadata,
    string AudienceAddress,
    string NamingGuidance,
    string DescriptionSignature);
