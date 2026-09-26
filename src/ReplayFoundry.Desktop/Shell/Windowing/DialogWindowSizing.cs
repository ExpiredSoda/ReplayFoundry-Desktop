using System;
using System.Windows;

namespace ReplayFoundry.Desktop.Shell.Windowing;

public static class DialogWindowSizing
{
    private const double EdgeAllowance = 32;

    public static void FitToOwnerWorkArea(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        double availableWidth = window.Owner?.ActualWidth > 0
            ? window.Owner.ActualWidth
            : SystemParameters.WorkArea.Width;
        double availableHeight = window.Owner?.ActualHeight > 0
            ? window.Owner.ActualHeight
            : SystemParameters.WorkArea.Height;

        double maximumWidth = Math.Max(640, availableWidth - EdgeAllowance);
        double maximumHeight = Math.Max(480, availableHeight - EdgeAllowance);

        window.MinWidth = Math.Min(window.MinWidth, maximumWidth);
        window.MinHeight = Math.Min(window.MinHeight, maximumHeight);
        window.MaxWidth = maximumWidth;
        window.MaxHeight = maximumHeight;
        window.Width = Math.Min(window.Width, maximumWidth);
        window.Height = Math.Min(window.Height, maximumHeight);
    }
}
