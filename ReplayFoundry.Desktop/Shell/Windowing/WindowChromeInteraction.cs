using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ReplayFoundry.Desktop.Shell.Guidance;

namespace ReplayFoundry.Desktop.Shell.Windowing;

public static class WindowChromeInteraction
{
    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached(
            "Attach",
            typeof(bool),
            typeof(WindowChromeInteraction),
            new PropertyMetadata(false, OnAttachChanged));

    public static bool GetAttach(DependencyObject element) => (bool)element.GetValue(AttachProperty);

    public static void SetAttach(DependencyObject element, bool value) => element.SetValue(AttachProperty, value);

    private static void OnAttachChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Window window || args.NewValue is not true)
        {
            return;
        }

        window.Loaded += OnWindowLoaded;
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Loaded -= OnWindowLoaded;
        if (FindNamedDescendant<Button>(window, "CaptionMaximizeButton") is not Button maximizeButton ||
            FindNamedDescendant<Button>(window, "CaptionMinimizeButton") is not Button minimizeButton ||
            FindNamedDescendant<Button>(window, "CaptionCloseButton") is not Button closeButton ||
            FindNamedDescendant<TextBlock>(window, "CaptionMaximizeGlyph") is not TextBlock maximizeGlyph ||
            FindNamedDescendant<FrameworkElement>(window, "TitleBar") is not FrameworkElement titleBar)
        {
            throw new InvalidOperationException("Window chrome controls are incomplete.");
        }

        var behavior = new MainWindowNativeBehavior(window);
        var state = new CaptionState(window, maximizeButton, maximizeGlyph);
        behavior.Attach();
        window.StateChanged += (_, _) => state.Refresh();
        window.Closed += (_, _) =>
        {
            behavior.Dispose();
            state.Dispose();
        };

        minimizeButton.Click += (_, _) => SystemCommands.MinimizeWindow(window);
        maximizeButton.Click += (_, _) => ToggleMaximizeRestore(window);
        closeButton.Click += (_, _) => SystemCommands.CloseWindow(window);
        titleBar.MouseLeftButtonDown += (_, e) => HandleTitleBarLeftClick(window, e);
        titleBar.MouseRightButtonUp += (_, e) => HandleTitleBarRightClick(window, e);
        state.Refresh();
    }

    private static T? FindNamedDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        if (root is T candidate && string.Equals(candidate.Name, name, StringComparison.Ordinal))
        {
            return candidate;
        }

        int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            T? match = FindNamedDescendant<T>(
                System.Windows.Media.VisualTreeHelper.GetChild(root, index),
                name);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static void HandleTitleBarLeftClick(Window window, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && FindButton(source) is not null)
        {
            return;
        }

        if (args.ClickCount == 2)
        {
            ToggleMaximizeRestore(window);
            args.Handled = true;
            return;
        }

        if (args.ButtonState != MouseButtonState.Pressed || window.WindowState == WindowState.Minimized)
        {
            return;
        }

        try
        {
            window.DragMove();
            args.Handled = true;
        }
        catch (InvalidOperationException)
        {
            args.Handled = true;
        }
    }

    private static void HandleTitleBarRightClick(Window window, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && FindButton(source) is not null)
        {
            return;
        }

        SystemCommands.ShowSystemMenu(window, window.PointToScreen(args.GetPosition(window)));
        args.Handled = true;
    }

    private static Button? FindButton(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is Button button)
            {
                return button;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static void ToggleMaximizeRestore(Window window)
    {
        if (window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(window);
        }
        else
        {
            SystemCommands.MaximizeWindow(window);
        }
    }

    private sealed class CaptionState : IDisposable
    {
        private readonly Window _window;
        private readonly Button _maximizeButton;
        private readonly TextBlock _maximizeGlyph;
        private bool _isDisposed;

        public CaptionState(Window window, Button maximizeButton, TextBlock maximizeGlyph)
        {
            _window = window;
            _maximizeButton = maximizeButton;
            _maximizeGlyph = maximizeGlyph;
        }

        public void Refresh()
        {
            if (_isDisposed)
            {
                return;
            }

            bool isMaximized = _window.WindowState == WindowState.Maximized;
            _maximizeGlyph.Text = (string)_window.FindResource(
                isMaximized ? "Icon.Glyph.ChromeRestore" : "Icon.Glyph.ChromeMaximize");
            AutomationProperties.SetName(_maximizeButton, isMaximized ? "Restore window" : "Maximize window");
        }

        public void Dispose() => _isDisposed = true;
    }
}
