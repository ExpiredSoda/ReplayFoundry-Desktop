using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public sealed class IconPath : Shape
{
    private Geometry _geometry = Geometry.Empty;

    public static readonly DependencyProperty IconKeyProperty =
        DependencyProperty.Register(nameof(IconKey), typeof(string), typeof(IconPath), new PropertyMetadata(null, OnIconKeyChanged));

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    private static void OnIconKeyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not IconPath iconPath)
        {
            return;
        }

        iconPath._geometry = args.NewValue is string key && Application.Current?.TryFindResource(key) is Geometry geometry
            ? geometry
            : Geometry.Empty;
        iconPath.InvalidateVisual();
    }

    protected override Geometry DefiningGeometry => _geometry;
}
