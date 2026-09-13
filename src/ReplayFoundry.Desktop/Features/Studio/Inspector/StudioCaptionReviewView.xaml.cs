using System.Windows.Controls;
using System.Windows;

namespace ReplayFoundry.Desktop.Features.Studio.Inspector;

public partial class StudioCaptionReviewView : UserControl
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact),
        typeof(bool), typeof(StudioCaptionReviewView), new PropertyMetadata(false));
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    public StudioCaptionReviewView() => InitializeComponent();
}
