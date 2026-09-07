using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Presentation.Converters;

public sealed class StudioOptionLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CompositionRegionRole.Gameplay => "Gameplay",
        CompositionRegionRole.Presenter => "Your camera",
        CompositionRegionRole.ChatOrText => "Chat or on-screen text",
        CompositionRegionRole.Overlay => "Logo or other overlay",
        CompositionRegionRole.Unknown => "Other area",
        StudioOutputCanvas.Source => "Same as recording",
        StudioOutputCanvas.Portrait => "Vertical · 9:16",
        StudioOutputCanvas.Square => "Square · 1:1",
        StudioOutputCanvas.Landscape => "Wide · 16:9",
        StudioCompositionLayout.Fit => "Show the whole picture",
        StudioCompositionLayout.Fill => "Fill the frame · crop the edges",
        StudioCompositionLayout.FacecamTop => "Camera above · fill gameplay area",
        StudioCompositionLayout.FacecamTopFit => "Camera above · keep all gameplay",
        StudioColorOutput.AutomaticSdr => "Automatic · standard screen colors",
        StudioColorOutput.PreserveSdr => "Keep original standard colors",
        StudioExportQuality.Compact => "Smaller file",
        StudioExportQuality.Standard => "Balanced",
        StudioExportQuality.High => "Best quality · larger file",
        StudioCaptionSafeArea.None => "No platform guide",
        StudioCaptionSafeArea.Shorts => "YouTube Shorts",
        StudioCaptionSafeArea.Reels => "Instagram Reels",
        StudioCaptionCasing.Original => "As written",
        StudioCaptionCasing.Uppercase => "ALL CAPITALS",
        StudioCaptionCasing.Lowercase => "all lowercase",
        _ => value?.ToString() ?? string.Empty,
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
