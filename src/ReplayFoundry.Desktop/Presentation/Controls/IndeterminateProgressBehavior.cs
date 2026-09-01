using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public static class IndeterminateProgressBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(IndeterminateProgressBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty SubscriptionProperty =
        DependencyProperty.RegisterAttached(
            "Subscription",
            typeof(Subscription),
            typeof(IndeterminateProgressBehavior));

    public static bool GetIsEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(
        DependencyObject element,
        bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(
        DependencyObject element,
        DependencyPropertyChangedEventArgs args)
    {
        if (element is not ProgressBar progress)
        {
            throw new InvalidOperationException(
                "The shared indeterminate-progress behavior can only be used with a ProgressBar.");
        }

        if (progress.GetValue(SubscriptionProperty) is Subscription existing)
        {
            existing.Dispose();
            progress.ClearValue(SubscriptionProperty);
        }
        if (args.NewValue is true)
        {
            progress.SetValue(
                SubscriptionProperty,
                new Subscription(progress));
        }
    }

    private sealed class Subscription : IDisposable
    {
        private static readonly DependencyPropertyDescriptor
            IsIndeterminateDescriptor =
            DependencyPropertyDescriptor.FromProperty(
                ProgressBar.IsIndeterminateProperty,
                typeof(ProgressBar)) ??
            throw new InvalidOperationException(
                "ProgressBar.IsIndeterminate must expose a dependency-property descriptor.");
        private static readonly DependencyPropertyDescriptor
            TemplateDescriptor =
            DependencyPropertyDescriptor.FromProperty(
                Control.TemplateProperty,
                typeof(ProgressBar)) ??
            throw new InvalidOperationException(
                "ProgressBar.Template must expose a dependency-property descriptor.");

        private readonly ProgressBar _progress;
        private FrameworkElement? _signal;
        private bool _progressEventsAttached;
        private bool _systemEventsAttached;
        private bool _disposed;

        internal Subscription(ProgressBar progress)
        {
            _progress = progress;
            _progress.Loaded += OnLoaded;
            _progress.Unloaded += OnUnloaded;
            _progress.IsVisibleChanged += OnIsVisibleChanged;
            if (_progress.IsLoaded)
            {
                AttachProgressEvents();
                AttachSystemEvents();
                Update();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _progress.Loaded -= OnLoaded;
            _progress.Unloaded -= OnUnloaded;
            _progress.IsVisibleChanged -= OnIsVisibleChanged;
            DetachProgressEvents();
            DetachSystemEvents();
            StopSignal();
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            AttachProgressEvents();
            AttachSystemEvents();
            Update();
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            DetachProgressEvents();
            DetachSystemEvents();
            StopSignal();
        }

        private void OnIsIndeterminateChanged(object? sender, EventArgs args) =>
            Update();

        private void OnTemplateChanged(object? sender, EventArgs args) =>
            Update();

        private void OnIsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs args) =>
            Update();

        private void OnSystemParametersChanged(
            object? sender,
            PropertyChangedEventArgs args)
        {
            if (string.Equals(
                    args.PropertyName,
                    nameof(SystemParameters.ClientAreaAnimation),
                    StringComparison.Ordinal))
            {
                Update();
            }
        }

        private void AttachProgressEvents()
        {
            if (_progressEventsAttached)
            {
                return;
            }
            IsIndeterminateDescriptor.AddValueChanged(
                _progress,
                OnIsIndeterminateChanged);
            TemplateDescriptor.AddValueChanged(
                _progress,
                OnTemplateChanged);
            _progressEventsAttached = true;
        }

        private void DetachProgressEvents()
        {
            if (!_progressEventsAttached)
            {
                return;
            }
            IsIndeterminateDescriptor.RemoveValueChanged(
                _progress,
                OnIsIndeterminateChanged);
            TemplateDescriptor.RemoveValueChanged(
                _progress,
                OnTemplateChanged);
            _progressEventsAttached = false;
        }

        private void AttachSystemEvents()
        {
            if (_systemEventsAttached)
            {
                return;
            }
            SystemParameters.StaticPropertyChanged +=
                OnSystemParametersChanged;
            _systemEventsAttached = true;
        }

        private void DetachSystemEvents()
        {
            if (!_systemEventsAttached)
            {
                return;
            }
            SystemParameters.StaticPropertyChanged -=
                OnSystemParametersChanged;
            _systemEventsAttached = false;
        }

        private void Update()
        {
            if (_disposed || !_progress.IsLoaded)
            {
                return;
            }
            _progress.ApplyTemplate();
            FrameworkElement? signal = _progress.Template?.FindName(
                "IndeterminateSignal",
                _progress) as FrameworkElement;
            if (!ReferenceEquals(_signal, signal))
            {
                StopSignal();
                _signal = signal;
            }
            if (_signal is null)
            {
                return;
            }

            StopAnimation(_signal);
            if (!_progress.IsIndeterminate ||
                !_progress.IsVisible ||
                !SystemParameters.ClientAreaAnimation)
            {
                return;
            }

            TimeSpan duration = TimeSpan.FromSeconds(1.1);
            if (_progress.TryFindResource("Motion.Ambient") is Duration resource &&
                resource.HasTimeSpan &&
                resource.TimeSpan > TimeSpan.Zero)
            {
                duration = resource.TimeSpan;
            }
            var animation = new PointAnimation(
                new Point(-0.5, 0.5),
                new Point(1.5, 0.5),
                new Duration(duration))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            _signal.BeginAnimation(
                FrameworkElement.RenderTransformOriginProperty,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void StopSignal()
        {
            if (_signal is null)
            {
                return;
            }
            StopAnimation(_signal);
            _signal = null;
        }

        private static void StopAnimation(FrameworkElement signal)
        {
            signal.BeginAnimation(
                FrameworkElement.RenderTransformOriginProperty,
                null);
            signal.SetCurrentValue(
                FrameworkElement.RenderTransformOriginProperty,
                new Point(0.5, 0.5));
        }
    }
}
