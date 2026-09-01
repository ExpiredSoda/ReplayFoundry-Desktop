using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Features.Generate.ModeSelection;

public partial class GenerationModeSelector : UserControl
{
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(
            nameof(IsCompact),
            typeof(bool),
            typeof(GenerationModeSelector),
            new FrameworkPropertyMetadata(false));

    public GenerationModeSelector()
    {
        InitializeComponent();
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }
}
