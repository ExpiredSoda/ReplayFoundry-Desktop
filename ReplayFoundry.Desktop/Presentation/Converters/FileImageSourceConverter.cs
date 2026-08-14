using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ReplayFoundry.Desktop.Presentation.Converters;

public sealed class FileImageSourceConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is not string path ||
            string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            !File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            if (image.CanFreeze)
            {
                image.Freeze();
            }

            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException(
            "File image conversion is one-way.");
}
