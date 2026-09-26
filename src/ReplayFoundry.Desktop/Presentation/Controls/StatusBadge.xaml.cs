using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));

    public StatusBadge()
    {
        InitializeComponent();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
}
