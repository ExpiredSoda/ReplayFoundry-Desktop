using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace ReplayFoundry.Desktop.Presentation.Accessibility;

internal sealed class HighContrastThemeController : IDisposable
{
    private static readonly string[] BackgroundKeys =
    [
        "Brush.WindowFallback", "Brush.WindowOverlay", "Brush.OverlaySurface",
        "Brush.DockSurface", "Brush.SurfaceCanvas", "Brush.SurfacePanel",
        "Brush.SurfaceElevated", "Brush.SurfaceInset", "Brush.KineticCanvasPane",
        "Brush.TitleBarActive", "Brush.TitleBarInactive", "Brush.ProgressSurface",
        "Brush.ProgressPanel", "Brush.ProgressTrack", "Brush.GenerateSetupSurface",
        "Brush.GenerateSetupPanel", "Brush.GenerateSetupField",
        "Brush.GenerateSetupButton", "Brush.GenerateModeSurface",
        "Brush.CompositionSurface", "Brush.CompositionCanvas",
        "Brush.CompositionErrorSurface", "Brush.GenerateAudioWarningSurface",
    ];

    private static readonly string[] TextKeys =
    [
        "Brush.BrandInk", "Brush.BrandPaper", "Brush.BrandCyan",
        "Brush.BrandBlue", "Brush.BrandBlueDeep", "Brush.BrandYellow",
        "Brush.BrandGreen", "Brush.TextPrimary", "Brush.TextSecondary",
        "Brush.TextMuted", "Brush.StatusSuccess", "Brush.StatusWarning",
        "Brush.StatusError", "Brush.StatusInfo", "Brush.ValidationErrorText",
        "Brush.ValidationWarningText", "Brush.CompositionWarning",
        "Brush.CompositionSuccess", "Brush.CompositionError",
        "Brush.GenerateAudioWarning", "Brush.ReferenceBadge",
    ];

    private static readonly string[] BorderKeys =
    [
        "Brush.WindowGridLine", "Brush.WindowGridMajor", "Brush.DockBorder",
        "Brush.BorderSubtle", "Brush.BorderStrong", "Brush.TitleBarBorder",
        "Brush.ScrollTrack", "Brush.ScrollThumb", "Brush.ScrollThumbHover",
        "Brush.ScrollThumbPressed", "Brush.CompositionFocus",
    ];

    private static readonly string[] HighlightKeys =
    [
        "Brush.PrimaryAction", "Brush.DockButtonHover", "Brush.DockFocus",
        "Brush.DockActiveBorder", "Brush.BorderFocus", "Brush.InteractiveHover",
        "Brush.InteractiveSelected", "Brush.InteractivePressed",
        "Brush.KineticGlow", "Brush.KineticGlowSoft", "Brush.CaptionHover",
        "Brush.CaptionPressed", "Brush.ProgressAccent", "Brush.Indeterminate",
        "Brush.GenerateSetupButtonHover", "Brush.GenerateSetupPrimaryButton",
        "Brush.GenerateModeSurfaceHover", "Brush.GenerateModeSurfaceSelected",
        "Brush.CompositionFocusSoft", "Brush.GenerateDropHighlight",
        "Brush.GenerateSetupHighlight", "Brush.GenerateSetupSelected",
        "Brush.GenerateSetupPressed",
    ];

    private readonly ResourceDictionary _resources;
    private readonly Dictionary<string, object> _originals = [];
    private bool _isApplied;

    public HighContrastThemeController(ResourceDictionary resources)
    {
        _resources = resources;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Apply(SystemParameters.HighContrast);
    }

    public void Dispose()
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        Apply(false);
    }

    internal void Apply(bool enabled)
    {
        if (_isApplied == enabled)
        {
            return;
        }

        if (enabled)
        {
            ApplySystemPalette();
        }
        else
        {
            RestoreBrandPalette();
        }

        _resources["Accessibility.IsHighContrast"] = enabled;
        _isApplied = enabled;
    }

    private void ApplySystemPalette()
    {
        Override(BackgroundKeys, SystemColors.WindowBrush);
        Override(TextKeys, SystemColors.WindowTextBrush);
        Override(BorderKeys, SystemColors.WindowTextBrush);
        Override(HighlightKeys, SystemColors.HighlightBrush);
        Override(["Brush.TextDisabled", "Brush.ScrollThumbDisabled"], SystemColors.GrayTextBrush);
        Override(["Brush.TextOnAccent", "Brush.PlatformYouTubeGlyph"], SystemColors.HighlightTextBrush);
        Override(["Brush.WindowGrid", "Brush.ModalScrim"], Brushes.Transparent);
    }

    private void Override(IEnumerable<string> keys, Brush brush)
    {
        foreach (string key in keys)
        {
            if (_resources[key] is not object original)
            {
                continue;
            }

            _originals.TryAdd(key, original);
            _resources[key] = brush;
        }
    }

    private void RestoreBrandPalette()
    {
        foreach ((string key, object value) in _originals)
        {
            _resources[key] = value;
        }

        _originals.Clear();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            Apply(SystemParameters.HighContrast);
        }
    }
}
