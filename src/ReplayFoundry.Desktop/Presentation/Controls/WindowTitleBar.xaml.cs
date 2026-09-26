using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public partial class WindowTitleBar : UserControl
{
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(
            nameof(Subtitle),
            typeof(string),
            typeof(WindowTitleBar),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(
            nameof(Status),
            typeof(string),
            typeof(WindowTitleBar),
            new PropertyMetadata(string.Empty));

    public WindowTitleBar()
    {
        InitializeComponent();
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }
}
