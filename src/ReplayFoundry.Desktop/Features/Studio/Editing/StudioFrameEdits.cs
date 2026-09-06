namespace ReplayFoundry.Desktop.Features.Studio.Editing;

/// <summary>A framing waypoint anchored to the recording, so trimming does not move its timing.</summary>
public sealed class StudioFrameKeyframe
{
    public StudioFrameKeyframe(TimeSpan sourcePosition, double zoom = 1, double panXPercent = 50, double panYPercent = 50)
    {
        if (sourcePosition < TimeSpan.Zero || !double.IsFinite(zoom) || zoom is < 1 or > 4 ||
            !double.IsFinite(panXPercent) || panXPercent is < 0 or > 100 ||
            !double.IsFinite(panYPercent) || panYPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(sourcePosition));
        SourcePosition = sourcePosition; Zoom = zoom; PanXPercent = panXPercent; PanYPercent = panYPercent;
    }
    public TimeSpan SourcePosition { get; }
    public double Zoom { get; }
    public double PanXPercent { get; }
    public double PanYPercent { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayText => $"{SourcePosition:hh\\:mm\\:ss\\.ff} · {Zoom:0.00}× · {PanXPercent:0}% / {PanYPercent:0}%";
}

/// <summary>A text card inside the clip, with timing anchored to its source recording.</summary>
public sealed class StudioTimedTextOverlay
{
    public StudioTimedTextOverlay(string text, TimeSpan sourceStart, TimeSpan sourceEnd,
        double centerXPercent = 50, double centerYPercent = 20, double fontSizePercent = 5)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500 ||
            text.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
            throw new ArgumentException("Text overlays require 1–500 printable characters.", nameof(text));
        if (sourceStart < TimeSpan.Zero || sourceEnd <= sourceStart ||
            !double.IsFinite(centerXPercent) || centerXPercent is < 5 or > 95 ||
            !double.IsFinite(centerYPercent) || centerYPercent is < 5 or > 95 ||
            !double.IsFinite(fontSizePercent) || fontSizePercent is < 2 or > 12)
            throw new ArgumentOutOfRangeException(nameof(sourceStart));
        Text = text; SourceStart = sourceStart; SourceEnd = sourceEnd;
        CenterXPercent = centerXPercent; CenterYPercent = centerYPercent; FontSizePercent = fontSizePercent;
    }
    public string Text { get; }
    public TimeSpan SourceStart { get; }
    public TimeSpan SourceEnd { get; }
    public double CenterXPercent { get; }
    public double CenterYPercent { get; }
    public double FontSizePercent { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayText => $"{SourceStart:hh\\:mm\\:ss}–{SourceEnd:hh\\:mm\\:ss} · {Text.ReplaceLineEndings(" ")}";
}
