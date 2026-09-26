using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ReplayFoundry.Desktop.Shell.Windowing;

public sealed class MainWindowNativeBehavior : IDisposable
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmDpiChanged = 0x02E0;
    private const uint MonitorDefaultToNearest = 2;
    private const uint AbmGetAutoHideBarEx = 0x0000000B;

    private readonly Window _window;
    private HwndSource? _source;
    private WindowVisibilityRecovery? _visibilityRecovery;
    private bool _isDisposed;

    public MainWindowNativeBehavior(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public uint CurrentDpi { get; private set; } = 96;

    public void Attach()
    {
        if (_isDisposed || _source is not null)
        {
            return;
        }

        if (PresentationSource.FromVisual(_window) is not HwndSource source)
        {
            throw new InvalidOperationException("The native window handle is not available.");
        }

        _source = source;
        CurrentDpi = GetDpiForWindow(source.Handle);
        _source.AddHook(WndProc);
        _visibilityRecovery = new WindowVisibilityRecovery(
            _window,
            source.Handle);
        _visibilityRecovery.Attach();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _visibilityRecovery?.Dispose();
        _visibilityRecovery = null;
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            ApplyWorkAreaBounds(hwnd, lParam);
            handled = true;
        }
        else if (message == WmDpiChanged)
        {
            CurrentDpi = GetDpiForWindow(hwnd);
            _window.InvalidateMeasure();
        }

        return IntPtr.Zero;
    }

    private void ApplyWorkAreaBounds(IntPtr hwnd, IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
        {
            return;
        }

        IntPtr monitorHandle = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitorHandle == IntPtr.Zero || !GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return;
        }

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        MonitorWorkArea area = CreateMonitorWorkArea(
            monitorInfo,
            CurrentDpi);
        WindowMaxBounds bounds = WindowWorkAreaCalculator.ForMonitor(
            area,
            FindAutoHideTaskbarEdge(monitorInfo.Monitor));

        info.MaxPosition = new PointInt(bounds.X, bounds.Y);
        info.MaxSize = new PointInt(bounds.Width, bounds.Height);
        info.MinTrackSize = new PointInt(
            WindowWorkAreaCalculator.DipToPixels(_window.MinWidth, CurrentDpi),
            WindowWorkAreaCalculator.DipToPixels(_window.MinHeight, CurrentDpi));
        Marshal.StructureToPtr(info, lParam, false);
    }

    private static MonitorWorkArea CreateMonitorWorkArea(
        MonitorInfo monitorInfo,
        uint dpi) =>
        new(
            monitorInfo.Monitor.Left,
            monitorInfo.Monitor.Top,
            monitorInfo.Monitor.Right - monitorInfo.Monitor.Left,
            monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top,
            monitorInfo.Work.Left,
            monitorInfo.Work.Top,
            monitorInfo.Work.Right - monitorInfo.Work.Left,
            monitorInfo.Work.Bottom - monitorInfo.Work.Top,
            dpi);

    private static AutoHideTaskbarEdge? FindAutoHideTaskbarEdge(
        RectangleInt monitorBounds)
    {
        foreach ((AutoHideTaskbarEdge edge, uint nativeEdge) in new[]
                 {
                     (AutoHideTaskbarEdge.Left, 0u),
                     (AutoHideTaskbarEdge.Top, 1u),
                     (AutoHideTaskbarEdge.Right, 2u),
                     (AutoHideTaskbarEdge.Bottom, 3u),
                 })
        {
            var data = new AppBarData
            {
                CbSize = (uint)Marshal.SizeOf<AppBarData>(),
                Edge = nativeEdge,
                Rect = monitorBounds,
            };
            if (SHAppBarMessage(AbmGetAutoHideBarEx, ref data) != UIntPtr.Zero)
            {
                return edge;
            }
        }
        return null;
    }

    private static uint GetDpiForWindow(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindowNative(hwnd);
        return dpi == 0 ? 96u : dpi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint GetDpiForWindowNative(IntPtr hwnd);

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern UIntPtr SHAppBarMessage(
        uint message,
        ref AppBarData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointInt
    {
        public int X;
        public int Y;

        public PointInt(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

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
        public int CbSize;
        public RectangleInt Monitor;
        public RectangleInt Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint CbSize;
        public IntPtr WindowHandle;
        public uint CallbackMessage;
        public uint Edge;
        public RectangleInt Rect;
        public IntPtr Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointInt Reserved;
        public PointInt MaxSize;
        public PointInt MaxPosition;
        public PointInt MinTrackSize;
        public PointInt MaxTrackSize;
    }
}
