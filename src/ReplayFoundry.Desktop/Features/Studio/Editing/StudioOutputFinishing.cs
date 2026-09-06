using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioAudioMastering
{
    public StudioAudioMastering(bool normalizeLoudness = false, double integratedLufs = -16,
        bool limitTruePeak = false, double truePeakDecibels = -1)
    {
        if (!double.IsFinite(integratedLufs) || integratedLufs is < -30 or > -5 ||
            !double.IsFinite(truePeakDecibels) || truePeakDecibels is < -9 or > 0)
            throw new ArgumentException("Loudness must be −30 to −5 LUFS and the true-peak ceiling −9 to 0 dBTP.");
        NormalizeLoudness = normalizeLoudness; IntegratedLufs = integratedLufs;
        LimitTruePeak = limitTruePeak; TruePeakDecibels = truePeakDecibels;
    }
    public bool NormalizeLoudness { get; }
    public double IntegratedLufs { get; }
    public bool LimitTruePeak { get; }
    public double TruePeakDecibels { get; }
}

public sealed record StudioCanvasDecoration
{
    public StudioCanvasDecoration(string backgroundColor = "#000000", NormalizedRectangle? hudSourceRegion = null,
        NormalizedRectangle? hudCanvasRegion = null)
    {
        if (!Regex.IsMatch(backgroundColor ?? "", "^#[0-9A-Fa-f]{6}$") ||
            (hudSourceRegion is null) != (hudCanvasRegion is null))
            throw new ArgumentException("Choose an RGB background color and both source and canvas regions for a HUD layer.");
        BackgroundColor = backgroundColor!.ToUpperInvariant(); HudSourceRegion = hudSourceRegion; HudCanvasRegion = hudCanvasRegion;
    }
    public string BackgroundColor { get; }
    public NormalizedRectangle? HudSourceRegion { get; }
    public NormalizedRectangle? HudCanvasRegion { get; }
}
