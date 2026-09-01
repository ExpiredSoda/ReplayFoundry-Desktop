using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ReplayFoundry.Desktop.Features.Studio.Browser
{
    public partial class StudioBrowserView : UserControl
    {
        public StudioBrowserView() => InitializeComponent();

        private void BrowserItems_SelectionChanged(
            object sender,
            SelectionChangedEventArgs eventArgs)
        {
            if (sender is not ListBox listBox ||
                listBox.SelectedValue is not string assetId ||
                listBox.Tag is not ICommand selectCommand)
            {
                return;
            }

            if (selectCommand.CanExecute(assetId))
            {
                selectCommand.Execute(assetId);
            }

            // A pending invalid edit can reject the requested switch. Refresh
            // the one-way projection so the visual selection stays truthful.
            listBox.GetBindingExpression(Selector.SelectedValueProperty)?
                .UpdateTarget();
        }
    }
}
namespace ReplayFoundry.Desktop.Features.Studio.Inspector
{
    public partial class StudioInspectorView : UserControl
    {
        public StudioInspectorView() => InitializeComponent();
    }
}
