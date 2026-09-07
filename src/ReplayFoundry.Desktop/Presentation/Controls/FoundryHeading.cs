using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Presentation.Controls;

/// <summary>A small film-frame signature shared by workspace section headings.</summary>
public sealed class FoundryHeading : TextBlock
{
    public FoundryHeading()
    {
        Padding = new Thickness(24, 0, 0, 0);
        SetResourceReference(BackgroundProperty, "Brush.FoundryHeading");
    }
}
