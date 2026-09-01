using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Features.Generate;

public partial class GenerateView : UserControl
{
    private const double CompactLayoutHeightThreshold = 545;

    private static readonly DependencyPropertyKey IsCompactLayoutPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsCompactLayout),
            typeof(bool),
            typeof(GenerateView),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsCompactLayoutProperty =
        IsCompactLayoutPropertyKey.DependencyProperty;

    public GenerateView()
    {
        InitializeComponent();
    }

    public bool IsCompactLayout =>
        (bool)GetValue(IsCompactLayoutProperty);

    private void GenerateView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        UpdateCompactLayout();
    }

    private void GenerateView_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        UpdateCompactLayout();
    }

    private void UpdateCompactLayout()
    {
        bool useCompactLayout =
            ActualHeight > 0 &&
            ActualHeight < CompactLayoutHeightThreshold;

        SetValue(
            IsCompactLayoutPropertyKey,
            useCompactLayout);
    }
}
