using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReplayFoundry.Desktop.Shell.Navigation;

namespace ReplayFoundry.Desktop.Shell.Dock;

public partial class FloatingDock : UserControl
{
    public static readonly DependencyProperty NavigateCommandProperty =
        DependencyProperty.Register(
            nameof(NavigateCommand),
            typeof(ICommand),
            typeof(FloatingDock),
            new PropertyMetadata(default(ICommand)));

    public static readonly DependencyProperty SelectedDestinationProperty =
        DependencyProperty.Register(
            nameof(SelectedDestination),
            typeof(ShellDestination),
            typeof(FloatingDock),
            new FrameworkPropertyMetadata(
                ShellDestination.Generate,
                OnSelectedDestinationChanged),
            IsValidShellDestination);

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsActive",
            typeof(bool),
            typeof(FloatingDock),
            new FrameworkPropertyMetadata(false));

    private IReadOnlyDictionary<ShellDestination, Button>? _buttons;

    public FloatingDock()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshSelectionVisuals();
    }

    public ICommand? NavigateCommand
    {
        get => (ICommand?)GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    public ShellDestination SelectedDestination
    {
        get => (ShellDestination)GetValue(SelectedDestinationProperty);
        set => SetValue(SelectedDestinationProperty, value);
    }

    public static bool GetIsActive(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsActiveProperty);
    }

    public static void SetIsActive(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsActiveProperty, value);
    }

    private static bool IsValidShellDestination(object value) =>
        value is ShellDestination destination &&
        Enum.IsDefined(typeof(ShellDestination), destination);

    private static void OnSelectedDestinationChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is FloatingDock dock)
        {
            dock.RefreshSelectionVisuals();
        }
    }

    private void RefreshSelectionVisuals()
    {
        _buttons ??= new Dictionary<ShellDestination, Button>
        {
            [ShellDestination.Generate] = GenerateButton,
            [ShellDestination.Studio] = StudioButton,
            [ShellDestination.Library] = LibraryButton,
            [ShellDestination.Publish] = PublishButton,
            [ShellDestination.Settings] = SettingsButton,
        };

        foreach ((ShellDestination destination, Button button) in _buttons)
        {
            SetIsActive(button, destination == SelectedDestination);
        }
    }
}
