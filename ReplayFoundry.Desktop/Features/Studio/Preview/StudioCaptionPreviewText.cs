using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

public sealed class StudioCaptionPreviewText : Control
{
    private static readonly Brush WhiteBrush = CreateBrush(0xFF, 0xFF, 0xFF);
    private static readonly Brush FocusBaseBrush = CreateBrush(0xB8, 0xB8, 0xB8);
    private static readonly Brush FutureWordBrush = CreateBrush(0x8A, 0x92, 0x98);
    private static readonly Brush AccentBrush = CreateBrush(0xFF, 0xC7, 0x5E);
    private static readonly Brush TransparentBrush = CreateBrush(0, 0, 0, 0);
    private static readonly Brush AccentGlowBrush = CreateBrush(0xFF, 0xC7, 0x5E, 0x72);
    private static readonly Brush OutlineBrush = CreateBrush(0x10, 0x10, 0x10);
    private static readonly Brush ShadowBrush = CreateBrush(0x00, 0x00, 0x00, 0x87);
    private static readonly Brush HighContrastPanelBrush =
        CreateBrush(0x00, 0x00, 0x00, 0xD8);

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

    protected override Size MeasureOverride(Size constraint)
    {
        if (string.IsNullOrWhiteSpace(CaptionText))
        {
            return new Size(0, 0);
        }

        double width = double.IsFinite(constraint.Width)
            ? Math.Max(1, constraint.Width)
            : Math.Max(1, Width);
        FormattedText text = CreateFormattedText(width);
        return new Size(width, text.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (string.IsNullOrWhiteSpace(CaptionText) ||
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return;
        }

        double outline = GetOutlinePixels(CaptionStyle);
        double shadow = GetShadowPixels(CaptionStyle);
        FormattedText text = CreateFormattedText(ActualWidth);
        var origin = new Point(0, 0);
        Geometry glyphs = text.BuildGeometry(origin);

        double scale = double.IsFinite(CaptionScale)
            ? Math.Clamp(CaptionScale, 0.5, 1.5)
            : 1;
        if (Math.Abs(scale - 1) > 0.0001)
        {
            drawingContext.PushTransform(new ScaleTransform(
                scale,
                scale,
                ActualWidth / 2,
                ActualHeight / 2));
        }

        if (CaptionStyle == GenerationCaptionStylePreset.HighContrast)
        {
            Rect panel = glyphs.Bounds;
            panel.Inflate(outline + 12, outline + 8);
            drawingContext.DrawRoundedRectangle(
                HighContrastPanelBrush,
                null,
                panel,
                10,
                10);
        }

        if (shadow > 0)
        {
            drawingContext.PushTransform(
                new TranslateTransform(shadow, shadow));
            drawingContext.DrawGeometry(ShadowBrush, null, glyphs);
            drawingContext.Pop();
        }

        var outlinePen = new Pen(
            CaptionStyle == GenerationCaptionStylePreset.HighContrast
                ? Brushes.Black
                : OutlineBrush,
            outline * 2)
        {
            LineJoin = PenLineJoin.Round,
        };
        outlinePen.Freeze();
        drawingContext.DrawGeometry(null, outlinePen, glyphs);
        drawingContext.DrawText(text, origin);

        if ((CaptionStyle is
                 GenerationCaptionStylePreset.WordFocus or
                 GenerationCaptionStylePreset.KaraokeSweep) &&
            AccentStartIndex >= 0 &&
            AccentLength > 0)
        {
            DrawActiveWordPulse(
                drawingContext,
                origin,
                outline);
        }

        if (Math.Abs(scale - 1) > 0.0001)
        {
            drawingContext.Pop();
        }
    }

    private FormattedText CreateFormattedText(
        double maximumTextWidth,
        bool applyAccent = true)
    {
        Brush baseBrush = CaptionStyle switch
        {
            GenerationCaptionStylePreset.WordFocus => FocusBaseBrush,
            GenerationCaptionStylePreset.KaraokeSweep => FutureWordBrush,
            GenerationCaptionStylePreset.Pop => AccentBrush,
            _ => WhiteBrush,
        };
        var text = new FormattedText(
            CaptionText,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily("Segoe UI"),
                FontStyles.Normal,
                FontWeights.Bold,
                FontStretches.Normal),
            Math.Max(1, CaptionFontSize),
            baseBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = maximumTextWidth,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.None,
        };
        int start = applyAccent
            ? Math.Clamp(
                AccentStartIndex,
                -1,
                CaptionText.Length)
            : -1;
        int length = applyAccent
            ? Math.Clamp(
                AccentLength,
                0,
                start < 0 ? 0 : CaptionText.Length - start)
            : 0;
        if (applyAccent &&
            CaptionStyle == GenerationCaptionStylePreset.KaraokeSweep &&
            start >= 0)
        {
            if (start > 0)
            {
                text.SetForegroundBrush(WhiteBrush, 0, start);
            }
            if (length > 0)
            {
                text.SetForegroundBrush(AccentBrush, start, length);
            }
        }
        else if (applyAccent && start >= 0 && length > 0)
        {
            text.SetForegroundBrush(AccentBrush, start, length);
        }
        return text;
    }

    private void DrawActiveWordPulse(
        DrawingContext drawingContext,
        Point origin,
        double outline)
    {
        int start = Math.Clamp(AccentStartIndex, 0, CaptionText.Length);
        int length = Math.Clamp(
            AccentLength,
            0,
            CaptionText.Length - start);
        if (length == 0)
        {
            return;
        }

        FormattedText active = CreateFormattedText(
            ActualWidth,
            applyAccent: false);
        active.SetForegroundBrush(
            TransparentBrush,
            0,
            CaptionText.Length);
        active.SetForegroundBrush(AccentBrush, start, length);
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
        drawingContext.PushTransform(new ScaleTransform(
            pulse,
            pulse,
            bounds.Left + bounds.Width / 2,
            bounds.Top + bounds.Height / 2));
        drawingContext.PushClip(highlight);
        Geometry activeGlyphs = active.BuildGeometry(origin);
        var glowPen = new Pen(AccentGlowBrush, outline * 2 + 5)
        {
            LineJoin = PenLineJoin.Round,
        };
        glowPen.Freeze();
        drawingContext.DrawGeometry(null, glowPen, activeGlyphs);
        var outlinePen = new Pen(OutlineBrush, outline * 2)
        {
            LineJoin = PenLineJoin.Round,
        };
        outlinePen.Freeze();
        drawingContext.DrawGeometry(null, outlinePen, activeGlyphs);
        drawingContext.DrawText(active, origin);
        drawingContext.Pop();
        drawingContext.Pop();
    }

    private static double GetOutlinePixels(
        GenerationCaptionStylePreset style) => style switch
        {
            GenerationCaptionStylePreset.Clean => 4,
            GenerationCaptionStylePreset.WordFocus => 5,
            GenerationCaptionStylePreset.KaraokeSweep => 5,
            GenerationCaptionStylePreset.Pop => 6,
            GenerationCaptionStylePreset.HighContrast => 2,
            _ => 4,
        };

    private static double GetShadowPixels(
        GenerationCaptionStylePreset style) =>
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
