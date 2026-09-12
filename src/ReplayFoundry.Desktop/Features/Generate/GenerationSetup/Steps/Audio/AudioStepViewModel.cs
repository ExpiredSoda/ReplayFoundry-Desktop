using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Audio;

public sealed class AudioStepViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly GenerationSetupDraft _draft;

    private readonly SelectionOption<AudioSelectionMode>[]
        _options;

    private SelectionOption<AudioSelectionMode>
        _selectedOption;
    private readonly CaptionAudioSelectionViewModel[]
        _captionSources;
    private bool _isCaptioningEnabled;
    private CaptionLookChoice _selectedCaptionLook;

    public AudioStepViewModel(
        GenerationSetupDraft draft,
        IGenerationAudioRoleMemory? audioRoleMemory = null,
        IAudioStreamAuditionService? auditionService = null,
        IReadOnlyList<StudioNamedCaptionLook>? captionLooks = null,
        AudioTranscriptionModelLanguageCapabilities? languageCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        _draft = draft;
        IReadOnlyList<StudioNamedCaptionLook> savedLooks;
        try { savedLooks = captionLooks ?? new JsonStudioCaptionLookStore().Load(); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            savedLooks = [];
            CaptionLookHint = "Saved looks could not be read. Built-in looks remain available.";
        }
        var looks = CaptionLookChoice.Create(savedLooks).ToList();
        _selectedCaptionLook = draft.CaptionSettings.SavedLook is { } saved
            ? looks.FirstOrDefault(option => option.Look == saved && option.Name == draft.CaptionSettings.SavedLookName)
                ?? new CaptionLookChoice(draft.CaptionSettings.SavedLookName ?? "Saved project look", saved.CaptionStyle,
                    "The complete look saved with this project.", new StudioNamedCaptionLook(draft.CaptionSettings.SavedLookName ?? "Saved project look", saved))
            : looks.First(option => option.Look is null && option.Style == draft.CaptionSettings.Style);
        if (!looks.Contains(_selectedCaptionLook)) looks.Add(_selectedCaptionLook);
        CaptionLooks = looks.AsReadOnly();

        _options =
        [
            new SelectionOption<AudioSelectionMode>(
                AudioSelectionMode.Auto,
                "Keep all original audio",
                "Every audio track stays audible in the finished clip. Captions use only the track you choose below."),
        ];

        if (draft.AudioSelectionMode != AudioSelectionMode.Auto)
        {
            draft.UpdateAudioSelectionMode(AudioSelectionMode.Auto);
        }

        _selectedOption =
            _options.Single(
                option =>
                    option.Value ==
                    draft.AudioSelectionMode);

        _isCaptioningEnabled = draft.CaptionSettings.IsEnabled;
        _captionSources = draft.Request.PreparedSources
            .Select(
                source =>
                    new CaptionAudioSelectionViewModel(
                        source,
                        draft.CaptionSettings.FindForSource(
                            source.Media.FullPath),
                        audioRoleMemory?.Find(source),
                        auditionService,
                        languageCapabilities))
            .ToArray();
        foreach (CaptionAudioSelectionViewModel source in _captionSources)
        {
            source.Changed += CaptionSource_Changed;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void StopAuditions()
    {
        foreach (CaptionAudioSelectionViewModel source in _captionSources)
            source.StopAudition();
    }

    public IReadOnlyList<SelectionOption<AudioSelectionMode>>
        Options =>
        _options;

    public SelectionOption<AudioSelectionMode> SelectedOption
    {
        get => _selectedOption;

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!_options.Contains(value))
            {
                throw new ArgumentException(
                    "The selected audio option is not available in this step.",
                    nameof(value));
            }

            if (ReferenceEquals(
                    _selectedOption,
                    value))
            {
                return;
            }

            _selectedOption = value;

            _draft.UpdateAudioSelectionMode(
                value.Value);

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(SelectedDescription));

            OnPropertyChanged(
                nameof(IsValid));

            OnPropertyChanged(
                nameof(ValidationMessage));
        }
    }

    public string SelectedDescription =>
        SelectedOption.Description;

    public bool IsValid =>
        SelectedOption.IsAvailable &&
        (!IsCaptioningEnabled ||
         HasAnyAudio &&
         _captionSources.All(static source => source.IsValid));

    public string? ValidationMessage =>
        IsValid
            ? null
            : !SelectedOption.IsAvailable
                ? SelectedOption.UnavailableReason
                : _captionSources.FirstOrDefault(static source => source.LanguageValidationMessage is not null)?.LanguageValidationMessage ??
                    "For each video with audio, choose the track and type of speech to caption.";

    public string ReferenceSourceName =>
        _draft.Request.ReferenceSource.FileName;

    public bool IsBatchSetup =>
        _draft.Request.IsBatch;

    public string BatchReferenceMessage =>
        $"'{ReferenceSourceName}' sets the first preview and defaults. " +
        "Replay Foundry checks every video separately and keeps all of its audio tracks.";

    public IReadOnlyList<CaptionAudioSelectionViewModel>
        CaptionSources => _captionSources;

    public IReadOnlyList<CaptionLookChoice> CaptionLooks { get; }
    public string CaptionLookHint { get; private set; } =
        "Built-in effects and your saved looks are together here. Hover to preview the animation.";
    public CaptionLookChoice SelectedCaptionLook
    {
        get => _selectedCaptionLook;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedCaptionLook)) return;
            if (!CaptionLooks.Contains(value)) throw new ArgumentException("Choose an available caption look.");
            _selectedCaptionLook = value;
            UpdateCaptionDraft(); OnPropertyChanged();
        }
    }

    public bool IsCaptioningEnabled
    {
        get => _isCaptioningEnabled;
        set
        {
            if (_isCaptioningEnabled == value) return;
            _isCaptioningEnabled = value;
            UpdateCaptionDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(ValidationMessage));
            if (value)
            {
                _ = PrepareAuditionsAsync();
            }
        }
    }

    public bool HasAnyAudio =>
        _captionSources.Any(static source => source.HasAudio);

    public string CaptionExplanation =>
        "Listen to a track, then choose whose words should appear on screen.";

    public async Task PrepareAuditionsAsync()
    {
        foreach (CaptionAudioSelectionViewModel source in _captionSources)
        {
            await source.PrepareAuditionsAsync();
        }
    }

    private void CaptionSource_Changed(object? sender, EventArgs e)
    {
        UpdateCaptionDraft();
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void UpdateCaptionDraft()
    {
        if (!IsCaptioningEnabled)
        {
            _draft.UpdateCaptionSettings(
                GenerationCaptionSettings.Disabled);
            return;
        }

        GenerationCaptionSourceSelection[] selections =
            _captionSources
                .Select(static source => source.CreateSelection())
                .OfType<GenerationCaptionSourceSelection>()
                .ToArray();
        if (selections.Length == 0)
        {
            return;
        }

        _draft.UpdateCaptionSettings(
            new GenerationCaptionSettings(
                isEnabled: true,
                SelectedCaptionLook.Style,
                selections,
                SelectedCaptionLook.Look,
                SelectedCaptionLook.Look is null ? null : SelectedCaptionLook.Name));
    }

    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }

    public void Dispose()
    {
        foreach (CaptionAudioSelectionViewModel source in _captionSources)
        {
            source.Changed -= CaptionSource_Changed;
            source.Dispose();
        }
    }
}
