using System.Text.RegularExpressions;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public enum StudioCaptionSafeArea { None, Shorts, Reels, TikTok }
public enum StudioCaptionAlignment { Center, Left, Right }
public enum StudioCaptionCasing { Original, Uppercase, Lowercase }
public enum StudioCaptionBackground { StyleDefault, None, Panel }

/// <summary>A portable caption look. Colors use RGB hex; font families are resolved by the local renderer.</summary>
public sealed record StudioCaptionTypography
{
    public StudioCaptionTypography(string fontFamily = "Segoe UI", string textColor = "#FFFFFF",
        string accentColor = "#FFC75E", string outlineColor = "#101010", bool bold = true,
        double outlineWidth = 3, double shadowDepth = 2, StudioCaptionSafeArea safeArea = StudioCaptionSafeArea.None,
        bool rightToLeft = false, StudioCaptionBackground background = StudioCaptionBackground.StyleDefault,
        string backgroundColor = "#000000", double backgroundOpacityPercent = 92,
        StudioCaptionAlignment alignment = StudioCaptionAlignment.Center, StudioCaptionCasing casing = StudioCaptionCasing.Original,
        double animationIntensityPercent = 100, double lineSpacingPercent = 100,
        StudioCaptionSafeAreaInsets? safeAreaInsets = null)
    {
        if (string.IsNullOrWhiteSpace(fontFamily) || fontFamily.Length > 100 ||
            fontFamily.IndexOfAny([',', '\r', '\n', '{', '}']) >= 0)
            throw new ArgumentException("Choose a valid installed font family.", nameof(fontFamily));
        if (!double.IsFinite(outlineWidth) || outlineWidth is < 0 or > 12 ||
            !double.IsFinite(shadowDepth) || shadowDepth is < 0 or > 12 || !Enum.IsDefined(safeArea))
            throw new ArgumentException("Outline and shadow must be between 0 and 12 pixels.");
        if (!Enum.IsDefined(background) || !Enum.IsDefined(alignment) || !Enum.IsDefined(casing) ||
            !double.IsFinite(backgroundOpacityPercent) || backgroundOpacityPercent is < 0 or > 100 ||
            !double.IsFinite(animationIntensityPercent) || animationIntensityPercent is < 0 or > 100 ||
            !double.IsFinite(lineSpacingPercent) || lineSpacingPercent is < 80 or > 200)
            throw new ArgumentException("Opacity and motion must be 0–100%; line spacing must be 80–200%.");
        FontFamily = fontFamily.Trim(); TextColor = Color(textColor); AccentColor = Color(accentColor);
        OutlineColor = Color(outlineColor); Bold = bold; OutlineWidth = outlineWidth;
        ShadowDepth = shadowDepth; SafeArea = safeArea; RightToLeft = rightToLeft;
        Background = background; BackgroundColor = Color(backgroundColor); BackgroundOpacityPercent = backgroundOpacityPercent;
        Alignment = alignment; Casing = casing; AnimationIntensityPercent = animationIntensityPercent; LineSpacingPercent = lineSpacingPercent;
        SafeAreaInsets = safeAreaInsets;
    }
    public string FontFamily { get; }
    public string TextColor { get; }
    public string AccentColor { get; }
    public string OutlineColor { get; }
    public bool Bold { get; }
    public double OutlineWidth { get; }
    public double ShadowDepth { get; }
    public StudioCaptionSafeArea SafeArea { get; }
    public bool RightToLeft { get; }
    public StudioCaptionBackground Background { get; }
    public string BackgroundColor { get; }
    public double BackgroundOpacityPercent { get; }
    public StudioCaptionAlignment Alignment { get; }
    public StudioCaptionCasing Casing { get; }
    public double AnimationIntensityPercent { get; }
    public double LineSpacingPercent { get; }
    public StudioCaptionSafeAreaInsets? SafeAreaInsets { get; }
    public StudioCaptionSafeAreaInsets GetSafeAreaInsets() => SafeAreaInsets ?? StudioCaptionSafeAreaInsets.ForPlatform(SafeArea);
    public string DisplayText(string text) => Casing switch
    {
        StudioCaptionCasing.Uppercase => text.ToUpperInvariant(),
        StudioCaptionCasing.Lowercase => text.ToLowerInvariant(),
        _ => text,
    };
    public double MotionScale(double scale) => 1 + (scale - 1) * AnimationIntensityPercent / 100;
    public bool HasBackground(Generate.GenerationSetup.GenerationCaptionStylePreset style) =>
        Background == StudioCaptionBackground.Panel ||
        Background == StudioCaptionBackground.StyleDefault && style == Generate.GenerationSetup.GenerationCaptionStylePreset.HighContrast;
    public bool HasCustomBackground(Generate.GenerationSetup.GenerationCaptionStylePreset style) =>
        Background == StudioCaptionBackground.Panel || HasBackground(style) && (BackgroundColor != "#000000" || BackgroundOpacityPercent != 92);
    public static StudioCaptionTypography Default { get; } = new();
    public double ConstrainVerticalPosition(double position)
    {
        if (SafeAreaInsets is not { } insets) return SafeArea == StudioCaptionSafeArea.None
            ? position : Math.Clamp(position, 18, SafeArea == StudioCaptionSafeArea.TikTok ? 72 : 76);
        double margin = Math.Min(6, (100 - insets.TopPercent - insets.BottomPercent) / 4);
        return Math.Clamp(position, Math.Max(10, insets.TopPercent + margin), Math.Min(90, 100 - insets.BottomPercent - margin));
    }
    public double ConstrainMaximumWidth(double width) => SafeAreaInsets is { } insets
        ? Math.Min(width, Math.Max(StudioClipAppearance.MinimumCaptionMaximumWidthPercent, 100 - 2 * Math.Max(insets.LeftPercent, insets.RightPercent)))
        : SafeArea == StudioCaptionSafeArea.None ? width : Math.Min(width, 82);
    public static string AssColor(string rgb) => "&H00" + rgb[5..7] + rgb[3..5] + rgb[1..3];
    private static string Color(string value) => Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$")
        ? value!.ToUpperInvariant() : throw new ArgumentException("Colors must use #RRGGBB.");
}
