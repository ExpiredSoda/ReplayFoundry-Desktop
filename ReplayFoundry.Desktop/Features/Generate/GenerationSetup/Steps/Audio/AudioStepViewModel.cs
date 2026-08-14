using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

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
    private readonly SelectionOption<GenerationCaptionStylePreset>[]
        _captionStyles;
    private bool _isCaptioningEnabled;
    private SelectionOption<GenerationCaptionStylePreset>
        _selectedCaptionStyle;

    public AudioStepViewModel(
        GenerationSetupDraft draft,
        IGenerationAudioRoleMemory? audioRoleMemory = null,
        IAudioStreamAuditionService? auditionService = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        _draft = draft;

        _options =
        [
            new SelectionOption<AudioSelectionMode>(
                AudioSelectionMode.Auto,
                "Keep all source audio",
                "Every inspected stream remains audible once in the finished clip. Captions use only the explicitly confirmed speech stream below."),
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
            new(GenerationCaptionStylePreset.Clean, "Clean", "A readable two-line subtitle with a subtle outline."),
            new(GenerationCaptionStylePreset.WordFocus, "Word focus", "Keeps context visible while the spoken word lifts and glows."),
            new(GenerationCaptionStylePreset.KaraokeSweep, "Karaoke focus", "Moves a gold, pulsing focus word across each phrase."),
            new(GenerationCaptionStylePreset.Pop, "Pop", "Bounces each spoken word with energetic short-form timing."),
            new(GenerationCaptionStylePreset.HighContrast, "High contrast", "Uses an opaque panel and strong edge on busy footage."),
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
                        auditionService))
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
                : "Captions require an explicit stream and a confirmed speech source for every source that contains audio.";

    public string ReferenceSourceName =>
        _draft.Request.ReferenceSource.FileName;

    public bool IsBatchSetup =>
        _draft.Request.IsBatch;

    public string BatchReferenceMessage =>
        $"'{ReferenceSourceName}' remains the batch reference. Every source " +
        "is inspected independently, and Automatic uses all of its detected " +
        "audio streams without assigning roles from track titles.";

    public IReadOnlyList<CaptionAudioSelectionViewModel>
        CaptionSources => _captionSources;

    public IReadOnlyList<SelectionOption<GenerationCaptionStylePreset>>
        CaptionStyles => _captionStyles;

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
            _selectedCaptionStyle = value;
            UpdateCaptionDraft();
            OnPropertyChanged();
        }
    }

    public bool HasAnyAudio =>
        _captionSources.Any(static source => source.HasAudio);

    public string CaptionExplanation =>
        "The selected stream controls only the words shown on screen. " +
        "Rendered clips still keep and mix every inspected source audio stream.";

    public string SourceAudioPolicyText =>
        "All source audio stays audible by default. Choose only which recording track supplies the on-screen words.";

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
                selections));
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
