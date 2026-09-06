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

public sealed record GenerationCaptionLookOption(string Name, StudioCaptionLook? Look)
{
    public override string ToString() => Name;
}

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
    private readonly SelectionOption<GenerationCaptionStylePreset>[]
        _captionStyles;
    private bool _isCaptioningEnabled;
    private SelectionOption<GenerationCaptionStylePreset>
        _selectedCaptionStyle;
    private GenerationCaptionLookOption _selectedCaptionLook;

    public AudioStepViewModel(
        GenerationSetupDraft draft,
        IGenerationAudioRoleMemory? audioRoleMemory = null,
        IAudioStreamAuditionService? auditionService = null,
        IReadOnlyList<StudioNamedCaptionLook>? captionLooks = null,
        AudioTranscriptionModelLanguageCapabilities? languageCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        _draft = draft;
        var looks = new List<GenerationCaptionLookOption> { new("Use effect defaults", null) };
        try { looks.AddRange((captionLooks ?? new JsonStudioCaptionLookStore().Load()).Select(look => new GenerationCaptionLookOption(look.Name, look.Look))); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { CaptionLookHint = "Saved looks could not be read. Effect defaults remain available."; }
        _selectedCaptionLook = looks.FirstOrDefault(option => option.Look == draft.CaptionSettings.SavedLook &&
            option.Name.Equals(draft.CaptionSettings.SavedLookName, StringComparison.Ordinal)) ??
            looks.FirstOrDefault(option => option.Look == draft.CaptionSettings.SavedLook) ??
            new GenerationCaptionLookOption(draft.CaptionSettings.SavedLookName ?? "Saved project look", draft.CaptionSettings.SavedLook);
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

        _captionStyles =
        [
            new(GenerationCaptionStylePreset.Clean, "Clean", "Shows short white phrases with a crisp edge and soft shadow."),
            new(GenerationCaptionStylePreset.WordFocus, "Word focus", "Keeps phrase context visible while the spoken word lifts in gold."),
            new(GenerationCaptionStylePreset.KaraokeSweep, "Karaoke sweep", "Sweeps gold through each spoken word while past words resolve white."),
            new(GenerationCaptionStylePreset.Pop, "Pop", "Shows one spoken word at a time with a quick elastic bounce."),
            new(GenerationCaptionStylePreset.HighContrast, "High contrast", "Places white phrases on an opaque dark panel for busy footage."),
        ];
        _isCaptioningEnabled = draft.CaptionSettings.IsEnabled;
        _selectedCaptionStyle = _captionStyles.Single(
            option =>
                option.Value == draft.CaptionSettings.Style);
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

    public IReadOnlyList<SelectionOption<GenerationCaptionStylePreset>>
        CaptionStyles => _captionStyles;
    public IReadOnlyList<GenerationCaptionLookOption> CaptionLooks { get; }
    public string CaptionLookHint { get; private set; } =
        "Reuse fonts, colors, phrase size, placement and spacing saved in Studio. " +
        "The selected look is captured for this generation run.";
    public bool IsCaptionStyleSelectable => IsCaptioningEnabled && SelectedCaptionLook.Look is null;
    public GenerationCaptionLookOption SelectedCaptionLook
    {
        get => _selectedCaptionLook;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedCaptionLook)) return;
            if (!CaptionLooks.Contains(value)) throw new ArgumentException("Choose an available caption look.");
            _selectedCaptionLook = value;
            if (value.Look is { } look) _selectedCaptionStyle = _captionStyles.Single(option => option.Value == look.CaptionStyle);
            UpdateCaptionDraft(); OnPropertyChanged(); OnPropertyChanged(nameof(SelectedCaptionStyle)); OnPropertyChanged(nameof(IsCaptionStyleSelectable));
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
            OnPropertyChanged(nameof(IsCaptionStyleSelectable));
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(ValidationMessage));
            if (value)
            {
                _ = PrepareAuditionsAsync();
            }
        }
    }

    public SelectionOption<GenerationCaptionStylePreset>
        SelectedCaptionStyle
    {
        get => _selectedCaptionStyle;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_captionStyles.Contains(value))
            {
                throw new ArgumentException(
                    "The caption style is not available.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedCaptionStyle, value)) return;
            if (SelectedCaptionLook.Look is not null) return;
            _selectedCaptionStyle = value;
            UpdateCaptionDraft();
            OnPropertyChanged();
        }
    }

    public bool HasAnyAudio =>
        _captionSources.Any(static source => source.HasAudio);

    public string CaptionExplanation =>
        "The track you choose controls only the words shown on screen. " +
        "The finished clip still keeps and mixes every audio track.";

    public string SourceAudioPolicyText =>
        "All original audio stays audible. Choose only which track supplies the on-screen words.";

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
                SelectedCaptionStyle.Value,
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
