using System.IO;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public enum StudioCropTrackingTarget { Gameplay, Hud }
public enum StudioCropTrackingState { Seed, Tracked, Lost, Ambiguous, Uninformative, SceneChanged }

public sealed class StudioCropTrackingKnot
{
    public StudioCropTrackingKnot(TimeSpan sourcePosition, double x, double y)
    {
        if (sourcePosition < TimeSpan.Zero || !double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or >= 1 || y is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(sourcePosition));
        SourcePosition = sourcePosition; X = x; Y = y;
    }
    public TimeSpan SourcePosition { get; }
    public double X { get; }
    public double Y { get; }
}

/// <summary>A contiguous accepted run. No interpolation is permitted between separate runs.</summary>
public sealed class StudioCropTrackingRun
{
    public StudioCropTrackingRun(IReadOnlyList<StudioCropTrackingKnot> knots)
    {
        ArgumentNullException.ThrowIfNull(knots);
        if (knots.Count is < 2 or > StudioSourceCropTrack.MaximumKnots || knots.Any(static knot => knot is null) ||
            knots.Zip(knots.Skip(1)).Any(static pair => pair.First.SourcePosition >= pair.Second.SourcePosition))
            throw new ArgumentException("A tracking run needs 2–16 ordered source-time knots.", nameof(knots));
        Knots = Array.AsReadOnly(knots.ToArray());
    }
    public IReadOnlyList<StudioCropTrackingKnot> Knots { get; }
}

/// <summary>Reviewed fixed-size source crop motion, independent of facecam and canvas motion.</summary>
public sealed class StudioSourceCropTrack
{
    public const int MaximumKnots = 16;
    public StudioSourceCropTrack(StudioCropTrackingTarget target, string sourceFullPath, long sourceLength,
        DateTime sourceLastWriteUtc, int displayWidth, int displayHeight, TimeSpan analyzedStart, TimeSpan analyzedEnd,
        NormalizedRectangle seedRegion, NormalizedRectangle bounds, NormalizedRectangle manualRegion,
        double viewportWidth, double viewportHeight, IReadOnlyList<StudioCropTrackingRun> runs,
        int sampledFrames, int acceptedFrames, StudioCropTrackingState finalState, string algorithmVersion = "zncc-translation-1",
        StudioOutputCanvas canvas = StudioOutputCanvas.Source, StudioCompositionLayout layout = StudioCompositionLayout.Fit,
        double facecamHeightPercent = 25, bool hasFacecam = false)
    {
        ArgumentNullException.ThrowIfNull(seedRegion); ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(manualRegion); ArgumentNullException.ThrowIfNull(runs);
        if (!Enum.IsDefined(target) || !Enum.IsDefined(finalState) || !Path.IsPathFullyQualified(sourceFullPath) ||
            sourceLength <= 0 || sourceLastWriteUtc.Kind != DateTimeKind.Utc || displayWidth <= 0 || displayHeight <= 0 ||
            analyzedStart < TimeSpan.Zero || analyzedEnd <= analyzedStart || analyzedEnd - analyzedStart > TimeSpan.FromSeconds(60) ||
            !bounds.Contains(seedRegion) || !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            viewportWidth <= 0 || viewportHeight <= 0 || viewportWidth > bounds.Width || viewportHeight > bounds.Height ||
            sampledFrames is < 2 or > 480 || acceptedFrames < 2 || acceptedFrames > sampledFrames ||
            algorithmVersion != "zncc-translation-1" || runs.Count == 0 || runs.Any(static run => run is null) ||
            runs.Sum(static run => run.Knots.Count) > MaximumKnots || !Enum.IsDefined(canvas) || !Enum.IsDefined(layout) ||
            !double.IsFinite(facecamHeightPercent) || facecamHeightPercent is < 10 or > 50 ||
            target == StudioCropTrackingTarget.Gameplay && !Same(bounds, manualRegion) ||
            target == StudioCropTrackingTarget.Hud && (!Same(bounds, NormalizedRectangle.FullFrame) ||
                viewportWidth != manualRegion.Width || viewportHeight != manualRegion.Height))
            throw new ArgumentException("The source crop track exceeds its supported source, duration or motion limits.");
        TimeSpan previous = TimeSpan.MinValue;
        foreach (StudioCropTrackingRun run in runs)
        {
            if (run.Knots[0].SourcePosition <= previous) throw new ArgumentException("Tracking runs must be ordered and separated.", nameof(runs));
            foreach (StudioCropTrackingKnot knot in run.Knots)
                if (knot.SourcePosition < analyzedStart || knot.SourcePosition >= analyzedEnd ||
                    knot.X < bounds.X - 1e-9 || knot.Y < bounds.Y - 1e-9 ||
                    knot.X + viewportWidth > bounds.Right + 1e-9 || knot.Y + viewportHeight > bounds.Bottom + 1e-9)
                    throw new ArgumentException("Tracking knots must remain in their analyzed window and gameplay bounds.", nameof(runs));
            previous = run.Knots[^1].SourcePosition;
        }
        Target = target; SourceFullPath = Path.GetFullPath(sourceFullPath); SourceLength = sourceLength;
        SourceLastWriteUtc = sourceLastWriteUtc; DisplayWidth = displayWidth; DisplayHeight = displayHeight;
        AnalyzedStart = analyzedStart; AnalyzedEnd = analyzedEnd; SeedRegion = seedRegion; Bounds = bounds;
        ManualRegion = manualRegion; ViewportWidth = viewportWidth; ViewportHeight = viewportHeight;
        Runs = Array.AsReadOnly(runs.ToArray()); SampledFrames = sampledFrames; AcceptedFrames = acceptedFrames;
        FinalState = finalState; AlgorithmVersion = algorithmVersion;
        Canvas = canvas; Layout = layout; FacecamHeightPercent = facecamHeightPercent; HasFacecam = hasFacecam;
    }
    public StudioCropTrackingTarget Target { get; }
    public string SourceFullPath { get; }
    public long SourceLength { get; }
    public DateTime SourceLastWriteUtc { get; }
    public int DisplayWidth { get; }
    public int DisplayHeight { get; }
    public TimeSpan AnalyzedStart { get; }
    public TimeSpan AnalyzedEnd { get; }
    public NormalizedRectangle SeedRegion { get; }
    public NormalizedRectangle Bounds { get; }
    public NormalizedRectangle ManualRegion { get; }
    public double ViewportWidth { get; }
    public double ViewportHeight { get; }
    public IReadOnlyList<StudioCropTrackingRun> Runs { get; }
    public int SampledFrames { get; }
    public int AcceptedFrames { get; }
    public StudioCropTrackingState FinalState { get; }
    public string AlgorithmVersion { get; }
    public StudioOutputCanvas Canvas { get; }
    public StudioCompositionLayout Layout { get; }
    public double FacecamHeightPercent { get; }
    public bool HasFacecam { get; }
    public bool MatchesSettings(StudioRenderSettings settings) => Target == StudioCropTrackingTarget.Gameplay
        ? Same(ManualRegion, settings.GameplayRegion) && Canvas == settings.Canvas && Layout == settings.Layout &&
            FacecamHeightPercent == settings.FacecamHeightPercent && HasFacecam == (settings.FacecamRegion is not null)
        : Same(ManualRegion, settings.Decoration.HudSourceRegion);
    public void RequireSource(MediaProbeResult media, bool checkFile)
    {
        var display = EffectiveDisplayGeometryCalculator.Calculate(media.PrimaryVideoStream);
        if (!string.Equals(SourceFullPath, media.FullPath, StringComparison.OrdinalIgnoreCase) ||
            display.Width != DisplayWidth || display.Height != DisplayHeight || AnalyzedEnd > media.Duration)
            throw new InvalidOperationException("Source tracking belongs to a different recording or display geometry. Clear it and analyze again.");
        if (!checkFile) return;
        var file = new FileInfo(SourceFullPath);
        if (!file.Exists || file.Length != SourceLength || file.LastWriteTimeUtc != SourceLastWriteUtc)
            throw new InvalidOperationException("The recording changed after tracking. Clear the saved track and analyze the current source again.");
    }
    internal static bool Same(NormalizedRectangle a, NormalizedRectangle? b) => b is not null &&
        a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;
}
