using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

/// <summary>A preview-only guide. The export never includes these overlays.</summary>
public sealed class StudioCaptionSafeAreaGuide : FrameworkElement
{
    public static readonly DependencyProperty CaptionTypographyProperty = DependencyProperty.Register(
        nameof(CaptionTypography), typeof(StudioCaptionTypography), typeof(StudioCaptionSafeAreaGuide),
        new FrameworkPropertyMetadata(StudioCaptionTypography.Default, FrameworkPropertyMetadataOptions.AffectsRender));
    public StudioCaptionTypography CaptionTypography
    {
        get => (StudioCaptionTypography)GetValue(CaptionTypographyProperty);
        set => SetValue(CaptionTypographyProperty, value);
    }
    protected override void OnRender(DrawingContext drawing)
    {
        if (CaptionTypography.SafeArea == StudioCaptionSafeArea.None && CaptionTypography.SafeAreaInsets is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        double width = ActualWidth, height = ActualHeight;
        var insets = CaptionTypography.GetSafeAreaInsets();
        var safe = new Rect(width * insets.LeftPercent / 100, height * insets.TopPercent / 100,
            width * (100 - insets.LeftPercent - insets.RightPercent) / 100,
            height * (100 - insets.TopPercent - insets.BottomPercent) / 100);
        var shade = new SolidColorBrush(Color.FromArgb(45, 240, 153, 48)); shade.Freeze();
        drawing.DrawGeometry(shade, null, Geometry.Combine(new RectangleGeometry(new Rect(0, 0, width, height)),
            new RectangleGeometry(safe), GeometryCombineMode.Exclude, null));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 199, 94)), Math.Max(1, width / 600)) { DashStyle = DashStyles.Dash };
        pen.Freeze(); drawing.DrawRectangle(null, pen, safe);
        if (CaptionTypography.SafeArea != StudioCaptionSafeArea.None)
        {
            string heading = CaptionTypography.SafeArea switch
            {
                StudioCaptionSafeArea.Shorts => "Shorts  ·  Search",
                StudioCaptionSafeArea.Reels => "Reels  ·  Camera",
                _ => "Following  ·  For You",
            };
            Control(heading, new Rect(width * .12, height * .075, width * .76, height * .038));
            string[] controls = ["Like", "Reply", "Share"];
            for (int index = 0; index < controls.Length; index++)
                Control(controls[index], new Rect(width * .87, height * (.48 + .085 * index), width * .115, height * .06));
            double bottom = CaptionTypography.SafeArea == StudioCaptionSafeArea.TikTok ? .81 : .85;
            Control("@creator  ·  Caption / description\nAudio and navigation", new Rect(width * .04, height * bottom, width * .8, height * .105));
        }
        string name = CaptionTypography.SafeArea == StudioCaptionSafeArea.None ? "Custom" : CaptionTypography.SafeArea.ToString();
        var label = new FormattedText($"{name} · approximate UI preview\nDashed blue: caption bounds · gold: review area", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(14, height * .013), Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = width * .8 };
        drawing.DrawText(label, new Point(width * .1, height * .025));
        void Control(string text, Rect bounds)
        {
            var background = new SolidColorBrush(Color.FromArgb(105, 20, 20, 24)); background.Freeze();
            drawing.DrawRoundedRectangle(background, null, bounds, width * .012, width * .012);
            var caption = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), Math.Max(11, height * .011), Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                { MaxTextWidth = Math.Max(1, bounds.Width - width * .01), MaxTextHeight = bounds.Height, Trimming = TextTrimming.CharacterEllipsis };
            drawing.DrawText(caption, new Point(bounds.Left + width * .005, bounds.Top + height * .005));
        }
    }
}
