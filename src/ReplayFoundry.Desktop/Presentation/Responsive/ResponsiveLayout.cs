namespace ReplayFoundry.Desktop.Presentation.Responsive;

public readonly record struct ResponsiveLayoutBands(
    bool IsCompact,
    bool IsStandard,
    bool IsWide);

public static class ResponsiveLayout
{
    public const double StandardMinimumWidth = 1120;
    public const double WideMinimumWidth = 1600;

    public static ResponsiveLayoutBands ForWidth(double width)
    {
        bool compact = width > 0 && width < StandardMinimumWidth;
        bool wide = width >= WideMinimumWidth;
        return new ResponsiveLayoutBands(
            compact,
            !compact && !wide,
            wide);
    }
}
