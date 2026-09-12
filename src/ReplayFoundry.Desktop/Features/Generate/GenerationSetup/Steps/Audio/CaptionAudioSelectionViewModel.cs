using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Audio;

public sealed record CaptionAudioStreamOption(
    AudioStreamInfo Stream,
    string DisplayName,
    string Detail)
{
    public override string ToString() => DisplayName;
}


public sealed class CaptionAudioSelectionViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly IAudioStreamAuditionService? _auditionService;
    private readonly AsyncDelegateCommand _auditionCommand;
    private readonly DelegateCommand _stopAuditionCommand;
    private readonly DelegateCommand<double> _seekAuditionCommand;
    private CancellationTokenSource? _auditionCancellation;
    private CancellationTokenSource? _preparationCancellation;
    private readonly Dictionary<int, AudioStreamAuditionPreview>
        _preparedAuditions = [];
    private readonly CaptionAudioStreamOption[] _streams;
    private readonly SelectionOption<CaptionAudioContentRole>[] _roles;
    private readonly SelectionOption<GenerationCaptionLanguagePolicy>[]
        _languages;
    private CaptionAudioStreamOption? _selectedStream;
    private SelectionOption<CaptionAudioContentRole>? _selectedRole;
    private SelectionOption<GenerationCaptionLanguagePolicy>
        _selectedLanguage;
    private double _auditionProgress;
    private TimeSpan _auditionPosition;
    private TimeSpan _auditionDuration;
    private bool _isAuditionPlaying;

    public CaptionAudioSelectionViewModel(
        PreparedGenerationSource source,
        GenerationCaptionSourceSelection? initialSelection,
        RememberedGenerationAudioRole? rememberedRole = null,
        IAudioStreamAuditionService? auditionService = null,
        AudioTranscriptionModelLanguageCapabilities? languageCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (initialSelection is not null &&
            !initialSelection.SourceFullPath.Equals(
                source.Media.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The initial caption selection belongs to another source.",
                nameof(initialSelection));
        }

        Source = source;
        _auditionService = auditionService;
        if (_auditionService is not null)
        {
            _auditionService.PlaybackChanged += AuditionService_PlaybackChanged;
        }
        _streams = source.Media.AudioStreams
            .Select(
                (stream, index) =>
                    new CaptionAudioStreamOption(
                        stream,
                        $"{source.Source.FileName} · audio {index + 1}",
                        BuildDetail(stream, index, source.Media.AudioStreams.Count)))
            .ToArray();
        _roles =
        [
            new(
                CaptionAudioContentRole.CreatorCommentary,
                "My commentary",
                "Show speech from your commentary track."),
            new(
                CaptionAudioContentRole.GameDialogue,
                "Game dialogue",
                "Show spoken dialogue from the game-audio track."),
            new(
                CaptionAudioContentRole.MixedSpeech,
                "Mixed speech",
                "This track contains both your commentary and game dialogue."),
            new(
                CaptionAudioContentRole.OtherKnownSpeech,
                "Other known speech",
                "This track contains other speech you want to caption."),
        ];
        GenerationCaptionLanguagePolicy selectedLanguage = initialSelection?.LanguagePolicy ??
            rememberedRole?.LanguagePolicy ?? GenerationCaptionLanguageCatalog.DefaultFor(languageCapabilities);
        _languages = GenerationCaptionLanguageCatalog.GetChoices(languageCapabilities, selectedLanguage).ToArray();
        LanguageCapabilitySummary = languageCapabilities?.Description;
        int? selectedStreamIndex = initialSelection?.AbsoluteAudioStreamIndex ??
            rememberedRole?.AbsoluteAudioStreamIndex;
        _selectedStream = selectedStreamIndex is null
            ? _streams.Length == 1 ? _streams[0] : null
            : _streams.SingleOrDefault(
                option =>
                    option.Stream.Index ==
                    selectedStreamIndex.Value);
        CaptionAudioContentRole? selectedRole = initialSelection?.ContentRole ??
            rememberedRole?.ContentRole;
        _selectedRole = selectedRole.HasValue
            ? _roles.Single(role => role.Value == selectedRole.Value)
            : null;
        _selectedLanguage = _languages.Single(
            language =>
                language.Value ==
                selectedLanguage);
        IsRememberedSelection = initialSelection is null && rememberedRole is not null;
        _auditionCommand = new AsyncDelegateCommand(
            AuditionAsync,
            () => _auditionService is not null &&
                  SelectedStream is not null &&
                  HasAudio);
        _stopAuditionCommand = new DelegateCommand(
            StopAudition,
            () => IsAuditionPlaying);
        _seekAuditionCommand = new DelegateCommand<double>(SeekAudition,
            _ => _auditionService?.CanSeek == true && SelectedStream is not null && HasWaveform && !IsPreparingAudition);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;

    public PreparedGenerationSource Source { get; }
    public string SourceName => Source.Source.FileName;
    public IReadOnlyList<CaptionAudioStreamOption> Streams => _streams;
    public IReadOnlyList<SelectionOption<CaptionAudioContentRole>> Roles =>
        _roles;
    public IReadOnlyList<SelectionOption<GenerationCaptionLanguagePolicy>>
        Languages => _languages;
    public string? LanguageCapabilitySummary { get; }
    public string? LanguageValidationMessage => SelectedLanguage.UnavailableReason;
    public bool HasAudio => _streams.Length > 0;
    public bool IsRememberedSelection { get; }
    public IReadOnlyList<double> WaveformPeaks =>
        SelectedStream is not null &&
        _preparedAuditions.TryGetValue(
            SelectedStream.Stream.Index,
            out AudioStreamAuditionPreview? preview)
            ? preview.WaveformPeaks
            : [];
    public bool HasWaveform => WaveformPeaks.Count > 0;
    public double AuditionProgress => _auditionProgress;
    public bool IsAuditionPlaying => _isAuditionPlaying;
    public string AuditionProgressText => _auditionDuration <= TimeSpan.Zero
        ? "Ready to preview"
        : $"{FormatPlaybackTime(_auditionPosition)} / " +
          FormatPlaybackTime(_auditionDuration);
    public bool IsPreparingAudition { get; private set; }
    public string AuditionSampleText =>
        SelectedStream is not null &&
        _preparedAuditions.TryGetValue(
            SelectedStream.Stream.Index,
            out AudioStreamAuditionPreview? preview)
            ? $"Prepared {FormatDuration(preview.Duration)} sample from " +
              FormatTimestamp(preview.Start)
            : "A representative 30-second sample will be prepared automatically.";
    public string AuditionStatus { get; private set; } =
        "Choose the recording track whose words should appear, then listen before confirming its role.";
    public System.Windows.Input.ICommand AuditionCommand => _auditionCommand;
    public System.Windows.Input.ICommand StopAuditionCommand => _stopAuditionCommand;
    public System.Windows.Input.ICommand SeekAuditionCommand => _seekAuditionCommand;

    private void SeekAudition(double progress)
    {
        if (!double.IsFinite(progress) || SelectedStream is null) return;
        if (_auditionService?.Seek(Source, SelectedStream.Stream.Index, Math.Clamp(progress, 0, 1)) == true)
        {
            AuditionStatus = IsAuditionPlaying ? "Playing from this point." : "Ready here. Press Play to listen.";
            OnPropertyChanged(nameof(AuditionStatus));
        }
    }
    public string AvailabilityText => HasAudio
        ? $"{_streams.Length} audio " +
          (_streams.Length == 1 ? "track" : "tracks")
        : "No audio tracks found";

    public CaptionAudioStreamOption? SelectedStream
    {
        get => _selectedStream;
        set
        {
            if (value is not null && !_streams.Contains(value))
            {
                throw new ArgumentException(
                    "The caption stream must belong to this source.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedStream, value)) return;
            StopAudition();
            _selectedStream = value;
            AuditionStatus = value is null ? "Choose a track to listen to." : "Press Play to listen, or click the waveform to choose a starting point.";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(ConfirmationStatus));
            NotifyAuditionProperties();
            _auditionCommand.RaiseCanExecuteChanged();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public SelectionOption<CaptionAudioContentRole>? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (value is not null && !_roles.Contains(value))
            {
                throw new ArgumentException(
                    "The caption content role is not available.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedRole, value)) return;
            _selectedRole = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(ConfirmationStatus));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public SelectionOption<GenerationCaptionLanguagePolicy> SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_languages.Contains(value))
            {
                throw new ArgumentException(
                    "The caption language is not available.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedLanguage, value)) return;
            _selectedLanguage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(LanguageValidationMessage));
            OnPropertyChanged(nameof(ConfirmationStatus));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsValid =>
        !HasAudio || SelectedStream is not null && SelectedRole is not null && SelectedLanguage.IsAvailable;
    public string ConfirmationStatus => !HasAudio
        ? "No audio to caption."
        : IsValid
            ? IsRememberedSelection
                ? "Using your previously confirmed recording layout. Review it if this capture setup changed."
                : "Caption track and speech type confirmed."
            : LanguageValidationMessage ??
                "Choose a track and say whether it contains your commentary, game dialogue, or mixed speech.";

    public GenerationCaptionSourceSelection? CreateSelection() =>
        SelectedStream is null || SelectedRole is null
            ? null
            : new GenerationCaptionSourceSelection(
                Source.Media.FullPath,
                SelectedStream.Stream.Index,
                SelectedRole.Value,
                SelectedLanguage.Value);

    public void Dispose()
    {
        _preparationCancellation?.Cancel();
        _preparationCancellation?.Dispose();
        _preparationCancellation = null;
        StopAudition();
        if (_auditionService is not null)
        {
            _auditionService.PlaybackChanged -= AuditionService_PlaybackChanged;
        }
        _auditionService?.Release(Source);
    }

    public async Task PrepareAuditionsAsync()
    {
        if (_auditionService is null || !HasAudio)
        {
            return;
        }
        _preparationCancellation?.Cancel();
        _preparationCancellation?.Dispose();
        _preparationCancellation = new CancellationTokenSource();
        CancellationToken token = _preparationCancellation.Token;
        IsPreparingAudition = true;
        AuditionStatus = "Preparing representative audio samples…";
        NotifyAuditionProperties();
        try
        {
            foreach (CaptionAudioStreamOption option in _streams)
            {
                token.ThrowIfCancellationRequested();
                AudioStreamAuditionPreview preview =
                    await _auditionService.PrepareAsync(
                        Source,
                        option.Stream.Index,
                        token);
                _preparedAuditions[option.Stream.Index] = preview;
            }
            AuditionStatus = SelectedStream is null
                ? "Samples are ready. Choose which recording track should supply the on-screen words."
                : "Sample ready. Listen and confirm what kind of speech this track contains.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            AuditionStatus = "Audio sample preparation stopped.";
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidOperationException or
                InvalidDataException or
                AudioSegmentExtractionException)
        {
            AuditionStatus =
                "Replay Foundry could not prepare an audio sample: " +
                exception.Message;
        }
        finally
        {
            IsPreparingAudition = false;
            NotifyAuditionProperties();
        }
    }

    private async Task AuditionAsync()
    {
        if (_auditionService is null || SelectedStream is null)
        {
            return;
        }
        _auditionCancellation?.Cancel();
        _auditionCancellation?.Dispose();
        _auditionCancellation = new CancellationTokenSource();
        CancellationToken auditionToken = _auditionCancellation.Token;
        CaptionAudioStreamOption selected = SelectedStream;
        if (!_preparedAuditions.ContainsKey(selected.Stream.Index))
        {
            await PrepareAuditionsAsync();
        }
        if (auditionToken.IsCancellationRequested || !ReferenceEquals(selected, SelectedStream) ||
            !_preparedAuditions.ContainsKey(selected.Stream.Index))
        {
            return;
        }
        AuditionStatus = $"Starting {SelectedStream.DisplayName}…";
        OnPropertyChanged(nameof(AuditionStatus));
        _stopAuditionCommand.RaiseCanExecuteChanged();
        try
        {
            await _auditionService.PlayAsync(
                Source,
                selected.Stream.Index,
                auditionToken);
            if (IsAuditionPlaying)
            {
                AuditionStatus =
                    $"Playing {SelectedStream.DisplayName}. Choose what kind of speech it contains.";
            }
        }
        catch (OperationCanceledException)
        {
            AuditionStatus = "Audio sample stopped.";
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidOperationException or
                AudioSegmentExtractionException)
        {
            AuditionStatus = "The selected track could not be played: " + exception.Message;
        }
        finally
        {
            OnPropertyChanged(nameof(AuditionStatus));
        }
    }

    public void StopAudition()
    {
        _auditionCancellation?.Cancel();
        _auditionCancellation?.Dispose();
        _auditionCancellation = null;
        _auditionService?.Stop();
        SetPlaybackState(0, TimeSpan.Zero, TimeSpan.Zero, isPlaying: false);
        _stopAuditionCommand.RaiseCanExecuteChanged();
    }

    private void AuditionService_PlaybackChanged(
        object? sender,
        AudioStreamAuditionPlaybackChangedEventArgs eventArgs)
    {
        if (SelectedStream is null ||
            !Source.Media.FullPath.Equals(
                eventArgs.SourceFullPath,
                StringComparison.OrdinalIgnoreCase) ||
            SelectedStream.Stream.Index != eventArgs.AbsoluteAudioStreamIndex)
        {
            return;
        }

        SetPlaybackState(
            eventArgs.Progress,
            eventArgs.Position,
            eventArgs.Duration,
            eventArgs.IsPlaying);
        if (!eventArgs.IsPlaying)
        {
            AuditionStatus = eventArgs.Progress >= 0.999
                ? $"Finished {SelectedStream.DisplayName}."
                : eventArgs.Position > TimeSpan.Zero ? "Ready here. Press Play to listen." : "Sample stopped. Press Play to listen again.";
            OnPropertyChanged(nameof(AuditionStatus));
        }
    }

    private void SetPlaybackState(
        double progress,
        TimeSpan position,
        TimeSpan duration,
        bool isPlaying)
    {
        _auditionProgress = Math.Clamp(progress, 0, 1);
        _auditionPosition = position;
        _auditionDuration = duration;
        _isAuditionPlaying = isPlaying;
        OnPropertyChanged(nameof(AuditionProgress));
        OnPropertyChanged(nameof(IsAuditionPlaying));
        OnPropertyChanged(nameof(AuditionProgressText));
        _stopAuditionCommand.RaiseCanExecuteChanged();
    }

    private static string BuildDetail(
        AudioStreamInfo stream,
        int zeroBasedPosition,
        int streamCount)
    {
        string position = $"Track {zeroBasedPosition + 1} of {streamCount}";
        return string.IsNullOrWhiteSpace(stream.Title)
            ? position + " · no saved name"
            : position + $" · saved name: “{stream.Title}”";
    }

    private void NotifyAuditionProperties()
    {
        _seekAuditionCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(WaveformPeaks));
        OnPropertyChanged(nameof(HasWaveform));
        OnPropertyChanged(nameof(IsPreparingAudition));
        OnPropertyChanged(nameof(AuditionSampleText));
        OnPropertyChanged(nameof(AuditionStatus));
    }

    private static string FormatTimestamp(TimeSpan value) =>
        MediaTimeFormatter.Format(value);

    private static string FormatDuration(TimeSpan value) =>
        value.TotalSeconds >= 29.5
            ? "30-second"
            : $"{Math.Max(1, Math.Round(value.TotalSeconds)):0}-second";

    private static string FormatPlaybackTime(TimeSpan value) =>
        $"{Math.Floor(value.TotalMinutes):00}:{value.Seconds:00}";

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
