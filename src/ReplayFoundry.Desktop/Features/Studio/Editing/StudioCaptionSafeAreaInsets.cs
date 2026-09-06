namespace ReplayFoundry.Desktop.Features.Studio.Editing;

/// <summary>Preview/export review margins, expressed as percentages of the output canvas.</summary>
public sealed record StudioCaptionSafeAreaInsets
{
    public StudioCaptionSafeAreaInsets(double leftPercent = 9, double topPercent = 12,
        double rightPercent = 9, double bottomPercent = 18)
    {
        foreach (double value in new[] { leftPercent, topPercent, rightPercent, bottomPercent })
            if (!double.IsFinite(value) || value is < 0 or > 45)
                throw new ArgumentOutOfRangeException(nameof(leftPercent), "Safe-area margins must be between 0 and 45 percent.");
        LeftPercent = leftPercent; TopPercent = topPercent; RightPercent = rightPercent; BottomPercent = bottomPercent;
    }
    public double LeftPercent { get; }
    public double TopPercent { get; }
    public double RightPercent { get; }
    public double BottomPercent { get; }
    public static StudioCaptionSafeAreaInsets ForPlatform(StudioCaptionSafeArea platform) => platform switch
    {
        StudioCaptionSafeArea.None => new(0, 0, 0, 0),
        StudioCaptionSafeArea.TikTok => new(bottomPercent: 22),
        _ => new(),
    };
}
