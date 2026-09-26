using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public partial class FoundryLoadingMark : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(FoundryLoadingMark), new PropertyMetadata(false, OnActivityChanged));
    private bool _subscribed;
    internal bool IsMotionRunning { get; private set; }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public FoundryLoadingMark()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateMotion();
    }
    private static void OnActivityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((FoundryLoadingMark)sender).UpdateMotion();
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
        _subscribed = true;
        UpdateMotion();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed) SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
        _subscribed = false;
        StopMotion();
    }
    private void OnSystemPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Dispatcher.InvokeAsync(UpdateMotion);
    }
    private void UpdateMotion()
    {
        if (!IsLoaded || !IsVisible || !IsActive || !SystemParameters.ClientAreaAnimation)
        {
            StopMotion();
            return;
        }
        if (IsMotionRunning) return;
        ((Storyboard)FindResource("AssemblyMotion")).Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        IsMotionRunning = true;
    }
    private void StopMotion()
    {
        if (!IsMotionRunning) return;
        ((Storyboard)FindResource("AssemblyMotion")).Remove(this);
        IsMotionRunning = false;
    }
}
