using ReplayFoundry.Desktop.Media.Intelligence.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public enum SceneMomentCategory { Action, Humor, Commentary, Lore, Discovery, Failure, Clutch, Tutorial, Reaction }
public enum SceneEvidenceVerdict { Supported, NotObserved, Uncertain }

/// <summary>Review-relative intervals; observations never imply user confirmation or calibrated probability.</summary>
public sealed record SceneCategoryEvidence(SceneMomentCategory Category, SceneEvidenceVerdict Verdict,
    TimeSpan Start, TimeSpan End, TimeSpan SetupStart, TimeSpan PayoffEnd, string Explanation,
    IReadOnlyList<string> EvidenceIds);

public sealed record SceneAudioTrack(int StreamIndex, AudioContentRoleAssignment Role,
    IReadOnlyList<VisualSemanticTranscriptSpan> Speech);

public sealed record SceneReviewContext(string SourcePath, long SourceLength, long SourceModifiedUtcTicks,
    TimeSpan SourceStart, TimeSpan SourceEnd, string Intent, string? ConfirmedGame,
    IReadOnlyList<SceneAudioTrack> AudioTracks, IReadOnlyList<TimeSpan> EventAnchors)
{
    internal SceneReviewContext Snapshot(TimeSpan duration)
    {
        if (!System.IO.Path.IsPathFullyQualified(SourcePath) || SourceLength < 0 || SourceModifiedUtcTicks < 0 ||
            SourceStart < TimeSpan.Zero || SourceEnd-SourceStart != duration || duration <= TimeSpan.Zero ||
            AudioTracks.Count > 4 || AudioTracks.Select(track => track.StreamIndex).Distinct().Count() != AudioTracks.Count ||
            AudioTracks.Any(track => track.StreamIndex < 0 || track.Speech.Count > 32 ||
                track.Speech.Select(span => span.Id).Distinct(StringComparer.Ordinal).Count() != track.Speech.Count ||
                track.Speech.Any(span => span.ReviewRelativeStart < TimeSpan.Zero || span.ReviewRelativeEnd > duration)) ||
            EventAnchors.Count > 32 || EventAnchors.Any(time => time < TimeSpan.Zero || time >= duration))
            throw new ArgumentException("Scene audio context must retain unique tracks and bounded source intervals.");
        return this with
        {
            AudioTracks = Array.AsReadOnly(AudioTracks.Select(track => track with
                { Speech = Array.AsReadOnly(track.Speech.ToArray()) }).ToArray()),
            EventAnchors = Array.AsReadOnly(EventAnchors.ToArray()),
        };
    }
}

public sealed class SceneMomentEvidence
{
    public const string Version = "moment-evidence-1";
    public SceneMomentEvidence(IEnumerable<SceneCategoryEvidence> categories, string audioStatus,
        string audioEvidenceJson, TimeSpan duration)
    {
        var rows = categories.ToArray();
        if (duration <= TimeSpan.Zero || rows.Length != Enum.GetValues<SceneMomentCategory>().Length ||
            rows.Select(row => row.Category).Distinct().Count() != rows.Length ||
            rows.Any(row => !Enum.IsDefined(row.Category) || !Enum.IsDefined(row.Verdict) ||
                row.SetupStart < TimeSpan.Zero || row.Start < row.SetupStart || row.End < row.Start ||
                row.PayoffEnd < row.End || row.PayoffEnd > duration || string.IsNullOrWhiteSpace(row.Explanation) ||
                row.Explanation.Length > 240 || row.EvidenceIds is null || row.EvidenceIds.Count > 8 ||
                row.EvidenceIds.Any(string.IsNullOrWhiteSpace) || row.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != row.EvidenceIds.Count ||
                row.Verdict == SceneEvidenceVerdict.Supported && (row.End <= row.Start || row.EvidenceIds.Count == 0)))
            throw new ArgumentException("Moment categories require complete, bounded, cited review evidence.");
        if (audioStatus is not ("Analyzed" or "AcousticOnly" or "Unavailable" or "NoAudio") ||
            string.IsNullOrWhiteSpace(audioEvidenceJson) || audioEvidenceJson.Length > 100_000)
            throw new ArgumentException("Audio evidence requires a bounded, explicit availability state.");
        Categories = Array.AsReadOnly(rows.Select(row => row with
            { EvidenceIds = Array.AsReadOnly(row.EvidenceIds.ToArray()) }).ToArray());
        AudioStatus = audioStatus;
        AudioEvidenceJson = audioEvidenceJson;
    }
    public IReadOnlyList<SceneCategoryEvidence> Categories { get; }
    public string AudioStatus { get; }
    public string AudioEvidenceJson { get; }
    public bool Supports(SceneMomentCategory category) => Categories.Any(row =>
        row.Category == category && row.Verdict == SceneEvidenceVerdict.Supported);
}
