using System;

namespace ReplayFoundry.Desktop.Shell.Windowing;

public enum ResponsiveWidthBand
{
    Compact,
    Standard,
    Wide,
}

public enum ResponsiveHeightBand
{
    Short,
    Standard,
    Tall,
}

public readonly record struct ResponsiveReadabilityState(
    ResponsiveWidthBand Width,
    ResponsiveHeightBand Height,
    double TextScale,
    uint Dpi)
{
    public bool NeedsProgressiveDisclosure => Width == ResponsiveWidthBand.Compact || Height == ResponsiveHeightBand.Short || TextScale > 1.5;

    public bool NeedsWrappedLabels => Width == ResponsiveWidthBand.Compact || TextScale > 1.25;

    public static ResponsiveReadabilityState Calculate(double width, double height, double textScale, uint dpi)
    {
        if (width < 0 || height < 0 || textScale < 1 || double.IsNaN(width) || double.IsNaN(height) || double.IsNaN(textScale))
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Responsive dimensions and text scale must be valid.");
        }

        ResponsiveWidthBand widthBand = width < 1120 ? ResponsiveWidthBand.Compact : width >= 1600 ? ResponsiveWidthBand.Wide : ResponsiveWidthBand.Standard;
        ResponsiveHeightBand heightBand = height < 700 ? ResponsiveHeightBand.Short : height >= 1000 ? ResponsiveHeightBand.Tall : ResponsiveHeightBand.Standard;
        return new ResponsiveReadabilityState(widthBand, heightBand, Math.Clamp(textScale, 1, 2.25), dpi == 0 ? 96u : dpi);
    }
}
