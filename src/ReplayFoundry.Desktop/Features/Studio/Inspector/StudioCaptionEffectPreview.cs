using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Studio.Inspector;

/// <summary>Runs the real caption frame calculator and renderer against a short, explicitly authored sample.</summary>
public sealed class StudioCaptionEffectPreview : UserControl
{
    public static readonly DependencyProperty PresetProperty = DependencyProperty.Register(nameof(Preset),
        typeof(GenerationCaptionStylePreset), typeof(StudioCaptionEffectPreview), new PropertyMetadata(GenerationCaptionStylePreset.Clean, OnLookChanged));
    public static readonly DependencyProperty TypographyProperty = DependencyProperty.Register(nameof(Typography),
        typeof(StudioCaptionTypography), typeof(StudioCaptionEffectPreview), new PropertyMetadata(StudioCaptionTypography.Default, OnLookChanged));
    public static readonly DependencyProperty IsPreviewActiveProperty = DependencyProperty.Register(nameof(IsPreviewActive),
        typeof(bool), typeof(StudioCaptionEffectPreview), new PropertyMetadata(false, OnActivityChanged));
    private static readonly GenerationCandidateCaptionTrack Sample = CreateSample();
    private readonly StudioLiveCaptionFrameCalculator _calculator = new();
    private readonly StudioCaptionPreviewText _caption = new() { Width = 300, Height = 72, CaptionFontSize = 36 };
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private bool _subscribed;
    public GenerationCaptionStylePreset Preset
    {
        get => (GenerationCaptionStylePreset)GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }
    public StudioCaptionTypography Typography
    {
        get => (StudioCaptionTypography)GetValue(TypographyProperty);
        set => SetValue(TypographyProperty, value);
    }
    public bool IsPreviewActive { get => (bool)GetValue(IsPreviewActiveProperty); set => SetValue(IsPreviewActiveProperty, value); }
    internal bool IsMotionRunning => _timer.IsEnabled;
    internal StudioCaptionPreviewText SampleText => _caption;

    public StudioCaptionEffectPreview()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Content = new Viewbox { Child = _caption, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000d / 30), DispatcherPriority.Background,
            (_, _) => RenderSampleAt(_clock.Elapsed.TotalSeconds % 3), Dispatcher);
        _timer.Stop();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateMotion();
        RenderSampleAt(1.45);
    }
    private static void OnLookChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((StudioCaptionEffectPreview)sender).RenderSampleAt(1.45);
    private static void OnActivityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((StudioCaptionEffectPreview)sender).UpdateMotion();
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) SystemParameters.StaticPropertyChanged += OnMotionPreferenceChanged;
        _subscribed = true;
        UpdateMotion();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed) SystemParameters.StaticPropertyChanged -= OnMotionPreferenceChanged;
        _subscribed = false;
        StopMotion();
    }
    private void OnMotionPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Dispatcher.InvokeAsync(UpdateMotion);
    }
    private void UpdateMotion()
    {
        if (!IsLoaded || !IsVisible || !IsPreviewActive || !SystemParameters.ClientAreaAnimation) { StopMotion(); return; }
        if (_timer.IsEnabled) return;
        _clock.Restart(); RenderSampleAt(0); _timer.Start();
    }
    private void StopMotion() { _timer.Stop(); _clock.Reset(); RenderSampleAt(1.45); }
    internal void RenderSampleAt(double seconds)
    {
        LiveCaptionFrameState frame = _calculator.Calculate(Sample, StudioCaptionWordLimitPreset.Streamlined,
            Preset, seconds, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        _caption.CaptionStyle = Preset;
        _caption.CaptionTypography = Typography;
        _caption.CaptionText = frame.Text ?? string.Empty;
        _caption.AccentStartIndex = frame.AccentStart;
        _caption.AccentLength = frame.AccentLength;
        _caption.SweepLength = frame.SweepLength;
        _caption.AccentProgress = frame.AccentProgress;
        _caption.CaptionScale = frame.Scale;
        _caption.EmphasisSpans = frame.EmphasisSpans;
    }
    private static GenerationCandidateCaptionTrack CreateSample()
    {
        var words = new[] { Word("That", .15, .7), Word("was", .7, 1.25), Word("close!", 1.25, 2.3) };
        var segment = new AudioTranscriptionSegment("effect-sample", "effect-sample", "That was close!",
            TimeSpan.FromSeconds(.15), TimeSpan.FromSeconds(2.3), TimeSpan.FromSeconds(.15), TimeSpan.FromSeconds(2.3), words);
        return GenerationCandidateCaptionTrack.RestoreStudioHandoff("effect-sample", "effect-sample",
            // Identity for an in-memory sample only. No file is opened or written.
            new GenerationCaptionSourceSelection(Path.Combine(AppContext.BaseDirectory, "caption-effect-sample.mp4"), 0, CaptionAudioContentRole.OtherKnownSpeech),
            GenerationCaptionStylePreset.Clean, TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), [segment], true,
            GenerationCaptionSuppressionReason.None);
    }
    private static AudioTranscriptionWord Word(string text, double start, double end) => new(text,
        TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));
}
