using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public sealed class PreviewFrameImageConverter :
    IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is not VideoPreviewFrame frame)
        {
            return null;
        }

        using var stream =
            new MemoryStream(
                frame.PngData.ToArray(),
                writable: false);

        var image =
            new BitmapImage();

        image.BeginInit();
        image.CacheOption =
            BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();

        if (image.CanFreeze)
        {
            image.Freeze();
        }

        return image;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException(
            "Preview frame image conversion is one-way.");
    }
}
