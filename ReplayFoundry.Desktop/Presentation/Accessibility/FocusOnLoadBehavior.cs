using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ReplayFoundry.Desktop.Presentation.Accessibility;

public static class FocusOnLoadBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(FocusOnLoadBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Control control || args.NewValue is not true)
        {
            return;
        }

        control.Loaded += OnControlLoaded;
    }

    private static void OnControlLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Control control)
        {
            return;
        }

        control.Loaded -= OnControlLoaded;
        control.Focus();
        Keyboard.Focus(control);
    }
}
