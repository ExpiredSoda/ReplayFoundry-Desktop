using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Features.Studio.HiddenMoments;

public partial class StudioHiddenMomentsView : UserControl
{
    public StudioHiddenMomentsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Focus();
    }
}
