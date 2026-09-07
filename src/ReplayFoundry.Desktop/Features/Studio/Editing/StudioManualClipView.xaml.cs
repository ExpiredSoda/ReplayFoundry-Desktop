using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public partial class StudioManualClipView : UserControl
{
    public StudioManualClipView()
    {
        InitializeComponent();
        Timeline.RangeEditStarted += (_, _) => (DataContext as StudioManualClipViewModel)?.BeginRangeGesture();
        Timeline.RangeEditCompleted += (_, _) => (DataContext as StudioManualClipViewModel)?.EndRangeGesture(cancel: false);
        Timeline.RangeEditCanceled += (_, _) => (DataContext as StudioManualClipViewModel)?.EndRangeGesture(cancel: true);
    }

    private void OnEditorVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Dispatcher.InvokeAsync(() => Timeline.Focus());
        else Timeline.CancelGesture();
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not StudioManualClipViewModel model ||
            Keyboard.FocusedElement is TextBoxBase or ComboBox or ComboBoxItem or ListBoxItem or Slider) return;
        if (e.Key == Key.Escape) { Timeline.CancelGesture(); e.Handled = true; return; }
        if (e.Key == Key.Space && Keyboard.FocusedElement is ButtonBase) return;
        e.Handled = StudioManualKeyboard.Handle(model, e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
    }
}
