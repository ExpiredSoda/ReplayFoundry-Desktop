using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

public sealed class StudioCaptionPositionMarginConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (values.Length < 2 ||
            values[0] is not double height ||
            values[1] is not double percent ||
            !double.IsFinite(height) ||
            !double.IsFinite(percent))
        {
            return new Thickness(24);
        }
        double captionHeight = values.Length > 2 &&
                               values[2] is double actualHeight &&
                               double.IsFinite(actualHeight) &&
                               actualHeight > 0
            ? actualHeight
            : 68;
        // ASS uses an exact center anchor (`an5` + `pos`). Preserve that
        // geometry here as well, including truthful frame-edge clipping.
        double top = height * percent / 100d - captionHeight / 2d;
        return new Thickness(0, top, 0, 0);
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
