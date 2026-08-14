using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Settings;

public sealed class CreatorVoiceSettingsViewModel : ObservableObject
{
    private readonly ICreatorVoiceSettingsEditor _profileEditor;
    private readonly DelegateCommand _saveCommand;
    private string _audienceAddress = "Chat";
    private string _namingGuidance = string.Empty;
    private string _descriptionSignature = string.Empty;
    private string _defaultTags = string.Empty;
    private string _status =
        "These defaults last until Replay Foundry closes.";

    public CreatorVoiceSettingsViewModel(
        ICreatorVoiceSettingsEditor profileEditor)
    {
        _profileEditor = profileEditor ??
            throw new ArgumentNullException(nameof(profileEditor));
        _saveCommand = new DelegateCommand(
            Save,
            static () => true);
        Reload();
    }

    public string AudienceAddress
    {
        get => _audienceAddress;
        set => SetDraft(ref _audienceAddress, value);
    }

    public string NamingGuidance
    {
        get => _namingGuidance;
        set => SetDraft(ref _namingGuidance, value);
    }

    public string DescriptionSignature
    {
        get => _descriptionSignature;
        set => SetDraft(ref _descriptionSignature, value);
    }

    public string DefaultTags
    {
        get => _defaultTags;
        set => SetDraft(ref _defaultTags, value);
    }

    public string Status => _status;

    public bool IsAvailable => true;

    public ICommand SaveCommand => _saveCommand;

    public void Reload()
    {
        CreatorVoiceSettings profile =
            _profileEditor.CurrentCreatorVoice;
        _audienceAddress = profile.AudienceAddress;
        _namingGuidance = profile.NamingGuidance;
        _descriptionSignature =
            profile.DescriptionSignature;
        _defaultTags = string.Join(", ", profile.DefaultTags);
        _status = "These defaults last until Replay Foundry closes.";
        NotifyAll();
    }

    private void Save()
    {
        try
        {
            CreatorVoiceSettings profile =
                _profileEditor.UpdateCreatorVoice(
                AudienceAddress,
                NamingGuidance,
                DescriptionSignature,
                ClipEditorialProfileTags.Parse(DefaultTags));
            _audienceAddress = profile.AudienceAddress;
            _namingGuidance = profile.NamingGuidance;
            _descriptionSignature =
                profile.DescriptionSignature;
            _defaultTags = string.Join(", ", profile.DefaultTags);
            _status =
                "Creator voice saved for new drafts and rerolls in this app session.";
        }
        catch (ArgumentException exception)
        {
            _status = "Creator voice could not be saved: " + exception.Message;
        }

        NotifyAll();
    }

    private void SetDraft(
        ref string field,
        string? value,
        [CallerMemberName] string? propertyName = null)
    {
        string normalized = value ?? string.Empty;
        if (!SetProperty(ref field, normalized, propertyName))
        {
            return;
        }

        _status = "Unsaved creator voice changes.";
        OnPropertyChanged(nameof(Status));
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(AudienceAddress));
        OnPropertyChanged(nameof(NamingGuidance));
        OnPropertyChanged(nameof(DescriptionSignature));
        OnPropertyChanged(nameof(DefaultTags));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsAvailable));
        _saveCommand.RaiseCanExecuteChanged();
    }
}
