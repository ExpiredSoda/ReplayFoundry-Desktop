using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace ReplayFoundry.Desktop.Shell.Windowing;

internal sealed class WindowVisibilityRecovery : IDisposable
{
    private const uint MonitorDefaultToNull = 0;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;

    private readonly Window _window;
    private readonly IntPtr _handle;
    private WindowState _lastNonMinimizedState;
    private bool _isAttached;
    private bool _isDisposed;
    private bool _isRecovering;
    private bool _isScheduled;

    public WindowVisibilityRecovery(Window window, IntPtr handle)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        if (handle == IntPtr.Zero)
        {
            throw new ArgumentException(
                "Visibility recovery requires a native window handle.",
                nameof(handle));
        }

        _handle = handle;
        _lastNonMinimizedState = window.WindowState == WindowState.Minimized
            ? WindowState.Normal
            : window.WindowState;
    }

    public void Attach()
    {
        if (_isDisposed || _isAttached)
        {
            return;
        }

        _isAttached = true;
        _window.Activated += Window_Activated;
        _window.ContentRendered += Window_ContentRendered;
        _window.StateChanged += Window_StateChanged;
        Schedule();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_isAttached)
        {
            _window.Activated -= Window_Activated;
            _window.ContentRendered -= Window_ContentRendered;
            _window.StateChanged -= Window_StateChanged;
            _isAttached = false;
        }
    }

    private void Window_Activated(object? sender, EventArgs args) =>
        Schedule();

    private void Window_ContentRendered(object? sender, EventArgs args)
    {
        _window.ContentRendered -= Window_ContentRendered;
        Schedule();
    }

    private void Window_StateChanged(object? sender, EventArgs args)
    {
        if (_window.WindowState != WindowState.Minimized)
        {
            Schedule();
        }
    }

    private void Schedule()
    {
        if (_isDisposed || _isScheduled)
        {
            return;
        }

        _isScheduled = true;
        _window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                _isScheduled = false;
                RecoverIfNeeded();
            }));
    }

    private void RecoverIfNeeded()
    {
        if (_isDisposed || _isRecovering ||
            _window.WindowState == WindowState.Minimized)
        {
            return;
        }

        if (IsOnAnyMonitor())
        {
            _lastNonMinimizedState = _window.WindowState;
            return;
        }

        if (!TryGetNearestMonitor(out MonitorInfo monitorInfo))
        {
            return;
        }

        _isRecovering = true;
        try
        {
            uint dpi = GetDpiForWindow(_handle);
            WindowRestoreBounds bounds =
                WindowWorkAreaCalculator.CenterRestoreBounds(
                    CreateWorkArea(monitorInfo, dpi),
                    FinitePositive(_window.Width)
                        ? _window.Width
                        : WindowStartupPolicy.DefaultWidth,
                    FinitePositive(_window.Height)
                        ? _window.Height
                        : WindowStartupPolicy.DefaultHeight);
            WindowState restoreState = _lastNonMinimizedState ==
                WindowState.Minimized
                    ? WindowState.Normal
                    : _lastNonMinimizedState;

            _window.WindowState = WindowState.Normal;
            if (!SetWindowPos(
                    _handle,
                    IntPtr.Zero,
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height,
                    SwpNoActivate | SwpNoZOrder | SwpShowWindow))
            {
                return;
            }

            if (restoreState == WindowState.Maximized)
            {
                _window.WindowState = WindowState.Maximized;
            }

            _lastNonMinimizedState = restoreState;
        }
        finally
        {
            _isRecovering = false;
        }
    }

    private bool IsOnAnyMonitor()
    {
        if (!GetWindowRect(_handle, out RectangleInt bounds) ||
            bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
        {
            return false;
        }

        return MonitorFromRect(
                ref bounds,
                MonitorDefaultToNull) != IntPtr.Zero;
    }

    private bool TryGetNearestMonitor(out MonitorInfo monitorInfo)
    {
        IntPtr monitor = MonitorFromWindow(
            _handle,
            MonitorDefaultToNearest);
        monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };
        return monitor != IntPtr.Zero &&
            GetMonitorInfo(monitor, ref monitorInfo);
    }

    private static MonitorWorkArea CreateWorkArea(
        MonitorInfo monitor,
        uint dpi) =>
        new(
            monitor.Monitor.Left,
            monitor.Monitor.Top,
            monitor.Monitor.Right - monitor.Monitor.Left,
            monitor.Monitor.Bottom - monitor.Monitor.Top,
            monitor.Work.Left,
            monitor.Work.Top,
            monitor.Work.Right - monitor.Work.Left,
            monitor.Work.Bottom - monitor.Work.Top,
            dpi);

    private static bool FinitePositive(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static uint GetDpiForWindow(IntPtr handle)
    {
        uint dpi = GetDpiForWindowNative(handle);
        return dpi == 0 ? 96u : dpi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromRect(
        ref RectangleInt rectangle,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(
        IntPtr hwnd,
        out RectangleInt rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint GetDpiForWindowNative(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectangleInt
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public RectangleInt Monitor;
        public RectangleInt Work;
        public uint Flags;
    }
}
