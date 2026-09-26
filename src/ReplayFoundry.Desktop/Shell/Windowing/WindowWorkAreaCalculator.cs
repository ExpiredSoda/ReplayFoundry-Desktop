using System;

namespace ReplayFoundry.Desktop.Shell.Windowing;

public readonly record struct MonitorWorkArea(
    int MonitorLeft,
    int MonitorTop,
    int MonitorWidth,
    int MonitorHeight,
    int WorkLeft,
    int WorkTop,
    int WorkWidth,
    int WorkHeight,
    uint Dpi)
{
    public int WorkRight => WorkLeft + WorkWidth;

    public int WorkBottom => WorkTop + WorkHeight;
}

public readonly record struct WindowMaxBounds(
    int X,
    int Y,
    int Width,
    int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

public readonly record struct WindowRestoreBounds(
    int X,
    int Y,
    int Width,
    int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

public enum AutoHideTaskbarEdge
{
    Left,
    Top,
    Right,
    Bottom,
}

public static class WindowWorkAreaCalculator
{
    public static WindowMaxBounds ForMonitor(
        MonitorWorkArea monitor,
        AutoHideTaskbarEdge? autoHideTaskbarEdge = null)
    {
        if (monitor.MonitorWidth <= 0 || monitor.MonitorHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monitor), "Monitor bounds must be positive.");
        }

        if (monitor.WorkWidth <= 0 || monitor.WorkHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monitor), "The work area must be positive.");
        }

        var bounds = new WindowMaxBounds(
            monitor.WorkLeft - monitor.MonitorLeft,
            monitor.WorkTop - monitor.MonitorTop,
            monitor.WorkWidth,
            monitor.WorkHeight);
        if (autoHideTaskbarEdge is null)
        {
            return bounds;
        }

        // A borderless maximized window that occupies the final physical
        // pixel can intercept the activation edge of an auto-hidden taskbar.
        // Reserve exactly one device pixel on the edge reported by the Shell.
        return autoHideTaskbarEdge.Value switch
        {
            AutoHideTaskbarEdge.Left => bounds with
            {
                X = bounds.X + 1,
                Width = Math.Max(1, bounds.Width - 1),
            },
            AutoHideTaskbarEdge.Top => bounds with
            {
                Y = bounds.Y + 1,
                Height = Math.Max(1, bounds.Height - 1),
            },
            AutoHideTaskbarEdge.Right => bounds with
            {
                Width = Math.Max(1, bounds.Width - 1),
            },
            AutoHideTaskbarEdge.Bottom => bounds with
            {
                Height = Math.Max(1, bounds.Height - 1),
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(autoHideTaskbarEdge)),
        };
    }

    public static int DipToPixels(double dips, uint dpi)
    {
        if (dips < 0 || double.IsNaN(dips) || double.IsInfinity(dips))
        {
            throw new ArgumentOutOfRangeException(nameof(dips));
        }

        uint effectiveDpi = dpi == 0 ? 96u : dpi;
        return Math.Max(1, (int)Math.Round(dips * effectiveDpi / 96d));
    }

    public static WindowRestoreBounds CenterRestoreBounds(
        MonitorWorkArea monitor,
        double requestedWidth,
        double requestedHeight)
    {
        if (monitor.WorkWidth <= 0 || monitor.WorkHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monitor),
                "The work area must be positive.");
        }

        if (requestedWidth <= 0 || double.IsNaN(requestedWidth) ||
            double.IsInfinity(requestedWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedWidth));
        }

        if (requestedHeight <= 0 || double.IsNaN(requestedHeight) ||
            double.IsInfinity(requestedHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedHeight));
        }

        int width = Math.Min(
            monitor.WorkWidth,
            DipToPixels(requestedWidth, monitor.Dpi));
        int height = Math.Min(
            monitor.WorkHeight,
            DipToPixels(requestedHeight, monitor.Dpi));
        return new WindowRestoreBounds(
            monitor.WorkLeft + (monitor.WorkWidth - width) / 2,
            monitor.WorkTop + (monitor.WorkHeight - height) / 2,
            width,
            height);
    }
}
