using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Presentation.Controls;

/// <summary>Static frame-and-splice artwork using the same colors as the Foundry mark.</summary>
public partial class FoundryFrameMark : UserControl
{
    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(FoundryFrameMark), new PropertyMetadata("Icon.Project"));

    public FoundryFrameMark() => InitializeComponent();

    public string IconKey
    {
        get => (string)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }
}
