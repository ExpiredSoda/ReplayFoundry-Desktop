using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

public sealed class StudioCaptionPreviewText : Control
{
    public static readonly DependencyProperty CaptionTypographyProperty = DependencyProperty.Register(
        nameof(CaptionTypography), typeof(StudioCaptionTypography), typeof(StudioCaptionPreviewText),
        new FrameworkPropertyMetadata(StudioCaptionTypography.Default, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public StudioCaptionTypography CaptionTypography
    {
        get => (StudioCaptionTypography)GetValue(CaptionTypographyProperty);
        set => SetValue(CaptionTypographyProperty, value);
    }
    public string ResolvedCaptionFontFamily => StudioCaptionFontResolver.Resolve(CaptionTypography.FontFamily).Family;
    public static readonly DependencyProperty EmphasisSpansProperty = DependencyProperty.Register(
        nameof(EmphasisSpans), typeof(IReadOnlyList<StudioCaptionWordSpan>), typeof(StudioCaptionPreviewText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<StudioCaptionWordSpan>? EmphasisSpans
    {
        get => (IReadOnlyList<StudioCaptionWordSpan>?)GetValue(EmphasisSpansProperty);
        set => SetValue(EmphasisSpansProperty, value);
    }
    private Brush ConfiguredTextBrush => ColorBrush(CaptionTypography.TextColor);
    private Brush ConfiguredAccentBrush => ColorBrush(CaptionTypography.AccentColor);
    private Brush ConfiguredOutlineBrush => ColorBrush(CaptionTypography.OutlineColor);
    private static Brush ColorBrush(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    private static readonly Brush WhiteBrush = CreateBrush(0xFF, 0xFF, 0xFF);
    private static readonly Brush FocusBaseBrush = CreateBrush(0xD7, 0xD9, 0xDE);
    private static readonly Brush FutureWordBrush = CreateBrush(0x98, 0x9E, 0xA5);
    private static readonly Brush AccentBrush = CreateBrush(0xFF, 0xC7, 0x5E);
    private static readonly Brush TransparentBrush = CreateBrush(0, 0, 0, 0);
    private Brush AccentGlowBrush
    {
        get
        {
            Color color = (Color)ColorConverter.ConvertFromString(CaptionTypography.AccentColor);
            return CreateBrush(color.R, color.G, color.B, 0x72);
        }
    }
    private static readonly Brush ShadowBrush = CreateBrush(0x00, 0x00, 0x00, 0x87);
    private static readonly Brush HighContrastPanelBrush =
        CreateBrush(0x00, 0x00, 0x00, 0xEC);

    public static readonly DependencyProperty CaptionTextProperty =
        DependencyProperty.Register(
            nameof(CaptionText),
            typeof(string),
            typeof(StudioCaptionPreviewText),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CaptionStyleProperty =
        DependencyProperty.Register(
            nameof(CaptionStyle),
            typeof(GenerationCaptionStylePreset),
            typeof(StudioCaptionPreviewText),
            new FrameworkPropertyMetadata(
                GenerationCaptionStylePreset.Clean,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CaptionFontSizeProperty =
        DependencyProperty.Register(
            nameof(CaptionFontSize),
            typeof(double),
            typeof(StudioCaptionPreviewText),
            new FrameworkPropertyMetadata(
                48d,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentStartIndexProperty =
        DependencyProperty.Register(
            nameof(AccentStartIndex),
            typeof(int),
            typeof(StudioCaptionPreviewText),
            new FrameworkPropertyMetadata(
                -1,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentLengthProperty =
        DependencyProperty.Register(
            nameof(AccentLength),
            typeof(int),
            typeof(StudioCaptionPreviewText),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SweepLengthProperty =
        DependencyProperty.Register(
            nameof(SweepLength),
            typeof(int),
            typeof(StudioCaptionPreviewText),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProgressProperty =
        DependencyProperty.Register(
            nameof(AccentProgress),
            typeof(double),
            typeof(StudioCaptionPreviewText),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CaptionScaleProperty =
        DependencyProperty.Register(
            nameof(CaptionScale),
            typeof(double),
            typeof(StudioCaptionPreviewText),
            new FrameworkPropertyMetadata(
                1d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public string CaptionText
    {
        get => (string)GetValue(CaptionTextProperty);
        set => SetValue(CaptionTextProperty, value);
    }

    public GenerationCaptionStylePreset CaptionStyle
    {
        get => (GenerationCaptionStylePreset)GetValue(CaptionStyleProperty);
        set => SetValue(CaptionStyleProperty, value);
    }

    public double CaptionFontSize
    {
        get => (double)GetValue(CaptionFontSizeProperty);
        set => SetValue(CaptionFontSizeProperty, value);
    }

    public int AccentStartIndex
    {
        get => (int)GetValue(AccentStartIndexProperty);
        set => SetValue(AccentStartIndexProperty, value);
    }

    public int AccentLength
    {
        get => (int)GetValue(AccentLengthProperty);
        set => SetValue(AccentLengthProperty, value);
    }

    public int SweepLength
    {
        get => (int)GetValue(SweepLengthProperty);
        set => SetValue(SweepLengthProperty, value);
    }

    public double AccentProgress
    {
        get => (double)GetValue(AccentProgressProperty);
        set => SetValue(AccentProgressProperty, value);
    }

    public double CaptionScale
    {
        get => (double)GetValue(CaptionScaleProperty);
        set => SetValue(CaptionScaleProperty, value);
    }

    private string DisplayText => CaptionTypography.DisplayText(CaptionText);
    private int DisplayIndex(int index) => StudioCaptionDisplayText.MapIndex(CaptionText, index, CaptionTypography);
    private (string Text, StudioCaptionTypography Typography, double Font, double Width, double Dpi)? _lineKey;
    private StudioCaptionLineLayoutResult? _lineLayout;
    private StudioCaptionLineLayoutResult ExplicitLines(double width)
    {
        var key = (DisplayText, CaptionTypography, CaptionFontSize, width, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (_lineKey != key)
        {
            _lineLayout = StudioCaptionLineLayout.Create(key.Item1, key.Item2, key.Item3, key.Item4, key.Item5);
            _lineKey = key;
        }
        return _lineLayout!;
    }
    private double PanelPadding => CaptionTypography.HasBackground(CaptionStyle)
        ? CaptionTypography.HasCustomBackground(CaptionStyle) ? 16 : 20 : 0;
    private bool UsesExplicitLines => CaptionTypography.SafeAreaInsets is not null || CaptionTypography.LineSpacingPercent != 100 ||
        CaptionTypography.HasCustomBackground(CaptionStyle);

    protected override Size MeasureOverride(Size constraint)
    {
        if (string.IsNullOrWhiteSpace(CaptionText)) return new Size(0, 0);
        double width = double.IsFinite(constraint.Width) ? Math.Max(1, constraint.Width) : Math.Max(1, Width);
        double height = !UsesExplicitLines
            ? CreateFormattedText(width).Height : ExplicitLines(width).Height;
        return new Size(width, height + PanelPadding);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (string.IsNullOrWhiteSpace(CaptionText) || ActualWidth <= 0 || ActualHeight <= 0) return;
        double outline = CaptionTypography.OutlineWidth == 3 ? GetOutlinePixels(CaptionStyle) : CaptionTypography.OutlineWidth;
        double shadow = CaptionTypography.ShadowDepth == 2 ? GetShadowPixels(CaptionStyle) : CaptionTypography.ShadowDepth;
        double scale = double.IsFinite(CaptionScale) ? CaptionTypography.MotionScale(Math.Clamp(CaptionScale, 0.5, 1.5)) : 1;
        if (Math.Abs(scale - 1) > 0.0001)
            drawingContext.PushTransform(new ScaleTransform(scale, scale, ActualWidth / 2, ActualHeight / 2));
        if (!UsesExplicitLines)
            DrawLine(drawingContext, DisplayText, 0, PanelPadding / 2, outline, shadow);
        else
            foreach (var line in ExplicitLines(ActualWidth).Lines)
                if (!string.IsNullOrWhiteSpace(line.Text))
                    DrawLine(drawingContext, line.Text, line.StartIndex, line.Top + PanelPadding / 2, outline, shadow);
        if (Math.Abs(scale - 1) > 0.0001) drawingContext.Pop();
        if (CaptionTypography.SafeArea != StudioCaptionSafeArea.None || CaptionTypography.SafeAreaInsets is not null)
        {
            // Preview-only at-rest bounds make narrow margins and tall phrases reviewable.
            double width = UsesExplicitLines ? ExplicitLines(ActualWidth).Lines.Select(static line => line.Width).DefaultIfEmpty(0).Max()
                : CreateFormattedText(ActualWidth).WidthIncludingTrailingWhitespace;
            double left = CaptionTypography.Alignment switch
            {
                StudioCaptionAlignment.Left => 0,
                StudioCaptionAlignment.Right => ActualWidth - width,
                _ => (ActualWidth - width) / 2,
            };
            var bounds = new Rect(left, 0, width, ActualHeight);
            bounds.Inflate(outline + shadow, outline + shadow);
            var pen = new Pen(Brushes.LightSkyBlue, 1) { DashStyle = DashStyles.Dash };
            pen.Freeze(); drawingContext.DrawRectangle(null, pen, bounds);
        }
    }

    private void DrawLine(DrawingContext drawingContext, string content, int displayOffset, double top, double outline, double shadow)
    {
        FormattedText text = CreateFormattedText(ActualWidth, content: content, displayOffset: displayOffset);
        var origin = new Point(0, top);
        Geometry glyphs = text.BuildGeometry(origin);
        if (CaptionTypography.HasBackground(CaptionStyle))
        {
            bool custom = CaptionTypography.HasCustomBackground(CaptionStyle);
            double panelLeft = CaptionTypography.Alignment switch
            {
                StudioCaptionAlignment.Left => 0,
                StudioCaptionAlignment.Right => ActualWidth - text.WidthIncludingTrailingWhitespace,
                _ => (ActualWidth - text.WidthIncludingTrailingWhitespace) / 2,
            };
            Rect panel = custom ? new Rect(panelLeft, top, text.WidthIncludingTrailingWhitespace, text.Height) : glyphs.Bounds;
            panel.Inflate(custom ? 8 : outline + 12, custom ? 8 : outline + 8);
            Color color = (Color)ColorConverter.ConvertFromString(CaptionTypography.BackgroundColor);
            var brush = custom ? CreateBrush(color.R, color.G, color.B, (byte)Math.Round(255 * CaptionTypography.BackgroundOpacityPercent / 100)) : HighContrastPanelBrush;
            drawingContext.DrawRoundedRectangle(brush, null, panel, custom ? 0 : 10, custom ? 0 : 10);
        }
        if (shadow > 0)
        {
            drawingContext.PushTransform(new TranslateTransform(shadow, shadow));
            drawingContext.DrawGeometry(ShadowBrush, null, glyphs);
            drawingContext.Pop();
        }
        var outlinePen = new Pen(CaptionStyle == GenerationCaptionStylePreset.HighContrast &&
            CaptionTypography.OutlineColor == "#101010" && !CaptionTypography.HasCustomBackground(CaptionStyle)
            ? Brushes.Black : ConfiguredOutlineBrush, outline * 2) { LineJoin = PenLineJoin.Round };
        outlinePen.Freeze();
        drawingContext.DrawGeometry(null, outlinePen, glyphs);
        drawingContext.DrawText(text, origin);
        if ((CaptionStyle == GenerationCaptionStylePreset.KaraokeSweep ||
             CaptionStyle == GenerationCaptionStylePreset.WordFocus && CaptionTypography.AnimationIntensityPercent > 0) && AccentStartIndex >= 0 && AccentLength > 0)
            DrawActiveWordPulse(drawingContext, origin, outline, content, displayOffset);
    }
    private FormattedText CreateFormattedText(
        double maximumTextWidth,
        bool applyAccent = true,
        string? content = null,
        int displayOffset = 0)
    {
        content ??= DisplayText;
        Brush baseBrush = CaptionStyle switch
        {
            GenerationCaptionStylePreset.WordFocus => FocusBaseBrush,
            GenerationCaptionStylePreset.KaraokeSweep => FutureWordBrush,
            GenerationCaptionStylePreset.Pop => AccentBrush,
            _ => WhiteBrush,
        };
        if (CaptionStyle == GenerationCaptionStylePreset.Pop) baseBrush = ConfiguredAccentBrush;
        else if (CaptionTypography.TextColor != "#FFFFFF") baseBrush = ConfiguredTextBrush;
        var text = new FormattedText(
            content,
            CultureInfo.CurrentUICulture,
            CaptionTypography.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(ResolvedCaptionFontFamily),
                FontStyles.Normal,
                CaptionTypography.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal),
            Math.Max(1, CaptionFontSize),
            baseBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = maximumTextWidth,
            TextAlignment = CaptionTypography.Alignment switch
            {
                StudioCaptionAlignment.Left => TextAlignment.Left,
                StudioCaptionAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Center,
            },
            Trimming = TextTrimming.None,
        };
        int rawStart = AccentStartIndex < 0 ? -1 : DisplayIndex(AccentStartIndex) - displayOffset;
        int rawEnd = AccentStartIndex < 0 ? -1 : DisplayIndex(AccentStartIndex + AccentLength) - displayOffset;
        int start = applyAccent && rawStart >= 0 ? Math.Clamp(rawStart, 0, content.Length) : -1;
        int length = applyAccent && rawEnd > 0 ? Math.Max(0, Math.Min(content.Length, rawEnd) - Math.Max(0, rawStart)) : 0;
        if (applyAccent && rawStart < 0 && rawEnd > 0) start = 0;
        if (applyAccent &&
            CaptionStyle == GenerationCaptionStylePreset.KaraokeSweep &&
            rawStart >= 0)
        {
            if (start > 0)
            {
                text.SetForegroundBrush(ConfiguredTextBrush, 0, start);
            }
        }
        else if (applyAccent && start >= 0 && length > 0)
        {
            text.SetForegroundBrush(ConfiguredAccentBrush, start, length);
        }
        foreach (var span in EmphasisSpans ?? [])
        {
            int left = Math.Max(0, DisplayIndex(span.StartIndex) - displayOffset);
            int right = Math.Min(content.Length, DisplayIndex(span.StartIndex + span.Length) - displayOffset);
            if (right > left) text.SetTextDecorations(TextDecorations.Underline, left, right - left);
        }
        return text;
    }

    private void DrawActiveWordPulse(
        DrawingContext drawingContext,
        Point origin,
        double outline,
        string content,
        int displayOffset)
    {
        int start = Math.Clamp(DisplayIndex(AccentStartIndex) - displayOffset, 0, content.Length);
        int end = Math.Clamp(DisplayIndex(AccentStartIndex + AccentLength) - displayOffset, 0, content.Length);
        int length = end - start;
        if (length == 0)
        {
            return;
        }

        FormattedText active = CreateFormattedText(
            ActualWidth,
            applyAccent: false,
            content: content,
            displayOffset: displayOffset);
        active.SetForegroundBrush(
            TransparentBrush,
            0,
            content.Length);
        active.SetForegroundBrush(ConfiguredAccentBrush, start, length);
        Geometry highlight = active.BuildHighlightGeometry(
            origin,
            start,
            length);
        Rect bounds = highlight.Bounds;
        double progress = double.IsFinite(AccentProgress)
            ? Math.Clamp(AccentProgress, 0, 1)
            : 0;
        double peak = CaptionStyle == GenerationCaptionStylePreset.KaraokeSweep
            ? 1.12
            : 1.07;
        double settled = CaptionStyle == GenerationCaptionStylePreset.KaraokeSweep
            ? 1.05
            : 1.025;
        double pulse = progress <= 0.35
            ? 1 + (peak - 1) * progress / 0.35
            : peak + (settled - peak) * (progress - 0.35) / 0.65;
        pulse = CaptionTypography.MotionScale(pulse);
        drawingContext.PushTransform(new ScaleTransform(
            pulse,
            pulse,
            bounds.Left + bounds.Width / 2,
            bounds.Top + bounds.Height / 2));
        drawingContext.PushClip(highlight);
        if (CaptionStyle == GenerationCaptionStylePreset.KaraokeSweep)
        {
            drawingContext.PushClip(new RectangleGeometry(new Rect(
                bounds.Left,
                bounds.Top,
                bounds.Width * progress,
                bounds.Height)));
        }
        Geometry activeGlyphs = active.BuildGeometry(origin);
        var glowPen = new Pen(AccentGlowBrush, outline * 2 + 5)
        {
            LineJoin = PenLineJoin.Round,
        };
        glowPen.Freeze();
        drawingContext.DrawGeometry(null, glowPen, activeGlyphs);
        var outlinePen = new Pen(ConfiguredOutlineBrush, outline * 2)
        {
            LineJoin = PenLineJoin.Round,
        };
        outlinePen.Freeze();
        drawingContext.DrawGeometry(null, outlinePen, activeGlyphs);
        drawingContext.DrawText(active, origin);
        if (CaptionStyle == GenerationCaptionStylePreset.KaraokeSweep)
        {
            drawingContext.Pop();
        }
        drawingContext.Pop();
        drawingContext.Pop();
    }

    private static double GetOutlinePixels(
        GenerationCaptionStylePreset style) => style switch
        {
            GenerationCaptionStylePreset.Clean => 3,
            GenerationCaptionStylePreset.WordFocus => 5,
            GenerationCaptionStylePreset.KaraokeSweep => 5,
            GenerationCaptionStylePreset.Pop => 6,
            GenerationCaptionStylePreset.HighContrast => 2,
            _ => 4,
        };

    private static double GetShadowPixels(
        GenerationCaptionStylePreset style) =>
        style == GenerationCaptionStylePreset.Clean ? 2 :
        style == GenerationCaptionStylePreset.Pop ? 2 :
        style == GenerationCaptionStylePreset.HighContrast ? 0 : 1;

    private static Brush CreateBrush(
        byte red,
        byte green,
        byte blue,
        byte alpha = 0xFF)
    {
        var brush = new SolidColorBrush(
            Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }
}
