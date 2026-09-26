using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentEvidenceReference
{
    public MomentEvidenceReference(
        MomentEvidenceReferenceKind kind,
        TimeSpan start,
        TimeSpan end,
        string sourceDescription,
        string? visualTargetKey = null,
        int? compositionIntervalIndex = null,
        string? regionId = null,
        CompositionRegionRole? regionRole = null,
        int? audioStreamIndex = null,
        double? rawValue = null,
        double? normalizedValue = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The evidence-reference kind is not defined.");
        }

        if (start < TimeSpan.Zero ||
            end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "An evidence reference must use a non-negative ordered interval.");
        }

        if (string.IsNullOrWhiteSpace(sourceDescription))
        {
            throw new ArgumentException(
                "An evidence reference requires a concise source description.",
                nameof(sourceDescription));
        }

        if (compositionIntervalIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(compositionIntervalIndex));
        }

        if (regionRole is not null &&
            !Enum.IsDefined(regionRole.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(regionRole));
        }

        if (audioStreamIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioStreamIndex));
        }

        if (rawValue is double raw &&
            !double.IsFinite(raw))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawValue),
                rawValue,
                "Raw evidence values must be finite when supplied.");
        }

        if (normalizedValue is double normalized &&
            (!double.IsFinite(normalized) ||
             normalized is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalizedValue),
                normalizedValue,
                "Normalized evidence values must be finite and between zero and one.");
        }

        bool visualKind =
            kind is
                MomentEvidenceReferenceKind.GameplayActivitySample or
                MomentEvidenceReferenceKind.GameplayActivityBurst or
                MomentEvidenceReferenceKind.PresenterActivitySample or
                MomentEvidenceReferenceKind.SceneBoundary or
                MomentEvidenceReferenceKind.SceneCluster or
                MomentEvidenceReferenceKind.BlackInterval or
                MomentEvidenceReferenceKind.FreezeInterval or
                MomentEvidenceReferenceKind.LumaChange or
                MomentEvidenceReferenceKind.SaturationChange;

        bool audioKind =
            kind is
                MomentEvidenceReferenceKind.AudioSignalWindow or
                MomentEvidenceReferenceKind.AudioNoveltyEvent or
                MomentEvidenceReferenceKind.SilenceInterval;

        if (visualKind &&
            string.IsNullOrWhiteSpace(visualTargetKey))
        {
            throw new ArgumentException(
                "Visual evidence references require a target key.",
                nameof(visualTargetKey));
        }

        if (audioKind &&
            audioStreamIndex is null)
        {
            throw new ArgumentException(
                "Audio evidence references require an absolute stream index.",
                nameof(audioStreamIndex));
        }

        if (!audioKind &&
            audioStreamIndex is not null)
        {
            throw new ArgumentException(
                "Only audio evidence references can identify an audio stream.",
                nameof(audioStreamIndex));
        }

        if ((kind is
                 MomentEvidenceReferenceKind.GameplayActivitySample or
                 MomentEvidenceReferenceKind.GameplayActivityBurst) &&
            regionRole != CompositionRegionRole.Gameplay)
        {
            throw new ArgumentException(
                "Gameplay activity references must identify a confirmed Gameplay region.",
                nameof(regionRole));
        }

        if (kind ==
                MomentEvidenceReferenceKind.PresenterActivitySample &&
            regionRole != CompositionRegionRole.Presenter)
        {
            throw new ArgumentException(
                "Presenter activity references must identify a confirmed Presenter region.",
                nameof(regionRole));
        }

        Kind = kind;
        Start = start;
        End = end;
        SourceDescription = sourceDescription.Trim();
        VisualTargetKey = visualTargetKey?.Trim();
        CompositionIntervalIndex = compositionIntervalIndex;
        RegionId = regionId?.Trim();
        RegionRole = regionRole;
        AudioStreamIndex = audioStreamIndex;
        RawValue = rawValue;
        NormalizedValue = normalizedValue;
    }

    public MomentEvidenceReferenceKind Kind { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration => End - Start;

    public string SourceDescription { get; }

    public string? VisualTargetKey { get; }

    public int? CompositionIntervalIndex { get; }

    public string? RegionId { get; }

    public CompositionRegionRole? RegionRole { get; }

    public int? AudioStreamIndex { get; }

    public double? RawValue { get; }

    public double? NormalizedValue { get; }
}
