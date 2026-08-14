using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.Captions;

public enum GenerationCaptionSuppressionReason
{
    None,
    NonSpeechOnlyTranscript,
    RepetitiveLowInformationTranscript,
}

public sealed class GenerationCandidateCaptionTrack
{
    private readonly GenerationMomentCandidate? _candidate;
    private readonly AudioTranscriptionResult? _transcription;

    public GenerationCandidateCaptionTrack(
        GenerationMomentCandidate candidate,
        GenerationCaptionSourceSelection sourceSelection,
        GenerationCaptionStylePreset requestedStyle,
        AudioTranscriptionResult transcription,
        IEnumerable<AudioTranscriptionSegment>? segments = null,
        bool isUserEdited = false,
        GenerationCaptionSuppressionReason suppressionReason =
            GenerationCaptionSuppressionReason.None)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(sourceSelection);
        ArgumentNullException.ThrowIfNull(transcription);
        if (!Enum.IsDefined(requestedStyle) ||
            !Enum.IsDefined(suppressionReason) ||
            !candidate.AnalyzedSource.PreparedSource.Media.FullPath.Equals(
                sourceSelection.SourceFullPath,
                StringComparison.OrdinalIgnoreCase) ||
            transcription.AbsoluteAudioStreamIndex !=
                sourceSelection.AbsoluteAudioStreamIndex ||
            transcription.Manifest.AbsoluteSourceOffset !=
                candidate.Candidate.Window.Start ||
            transcription.Manifest.InputDuration !=
                candidate.Candidate.Window.Duration)
        {
            throw new ArgumentException(
                "A candidate caption track must retain the exact selected candidate, stream, style, and source window.");
        }

        AudioTranscriptionSegment[] segmentSnapshot =
            segments?.ToArray() ?? transcription.Segments.ToArray();
        if (suppressionReason != GenerationCaptionSuppressionReason.None &&
            segmentSnapshot.Length != 0)
        {
            throw new ArgumentException(
                "Suppressed caption tracks cannot retain renderable display segments.",
                nameof(segments));
        }
        if (segmentSnapshot.Any(static segment => segment is null) ||
            segmentSnapshot.Any(segment =>
                !segment.NeighborhoodId.Equals(
                    transcription.NeighborhoodId,
                    StringComparison.Ordinal) ||
                segment.RelativeEnd > transcription.Manifest.InputDuration) ||
            segmentSnapshot.Zip(
                    segmentSnapshot.Skip(1),
                    static (left, right) =>
                        right.RelativeStart < left.RelativeEnd)
                .Any(static overlaps => overlaps))
        {
            throw new ArgumentException(
                "Caption display segments must remain ordered and bounded by the retained transcription window.",
                nameof(segments));
        }

        _candidate = candidate;
        SourceSelection = sourceSelection;
        RequestedStyle = requestedStyle;
        _transcription = transcription;
        CandidateId = candidate.Id;
        NeighborhoodId = transcription.NeighborhoodId;
        SourceWindowStart = transcription.Manifest.AbsoluteSourceOffset;
        SourceWindowDuration = transcription.Manifest.InputDuration;
        SourceDuration = transcription.Manifest.SourceDuration;
        Segments = Array.AsReadOnly(segmentSnapshot);
        IsUserEdited = isUserEdited;
        SuppressionReason = suppressionReason;
    }

    /// <summary>
    /// Gets the Generate-time candidate while caption preparation is active.
    /// Studio handoff tracks intentionally release the analysis graph; use
    /// <see cref="CandidateId"/> for retained project identity.
    /// </summary>
    public GenerationMomentCandidate Candidate => _candidate ??
        throw new InvalidOperationException(
            "The retained Studio caption track no longer owns the Generate analysis candidate.");

    public string CandidateId { get; }
    public string NeighborhoodId { get; }
    public TimeSpan SourceWindowStart { get; }
    public TimeSpan SourceWindowDuration { get; }
    public TimeSpan SourceDuration { get; }
    public GenerationCaptionSourceSelection SourceSelection { get; }
    public GenerationCaptionStylePreset RequestedStyle { get; }
    public AudioTranscriptionResult Transcription => _transcription ??
        throw new InvalidOperationException(
            "The retained Studio caption track no longer owns provider execution state.");
    public IReadOnlyList<AudioTranscriptionSegment> Segments { get; }
    public bool IsUserEdited { get; }
    public GenerationCaptionSuppressionReason SuppressionReason { get; }
    public bool IsSuppressed =>
        SuppressionReason != GenerationCaptionSuppressionReason.None;
    public bool HasRenderableSegments => Segments.Count > 0;
    public bool HasTimedWords =>
        Segments.Any(
            static segment => segment.Words.Count > 0);

    public GenerationCandidateCaptionTrack WithRequestedStyle(
        GenerationCaptionStylePreset requestedStyle) =>
        CreateRetained(
            requestedStyle,
            Segments,
            IsUserEdited,
            SuppressionReason);

    public GenerationCandidateCaptionTrack WithEditedSegments(
        IEnumerable<AudioTranscriptionSegment> segments) =>
        CreateRetained(
            RequestedStyle,
            segments,
            isUserEdited: true,
            suppressionReason: GenerationCaptionSuppressionReason.None);

    internal GenerationCandidateCaptionTrack ToStudioHandoff() =>
        CreateRetained(
            RequestedStyle,
            Segments,
            IsUserEdited,
            SuppressionReason,
            retainGenerationObjects: false);

    internal static GenerationCandidateCaptionTrack RestoreStudioHandoff(
        string candidateId,
        string neighborhoodId,
        GenerationCaptionSourceSelection sourceSelection,
        GenerationCaptionStylePreset requestedStyle,
        TimeSpan sourceWindowStart,
        TimeSpan sourceWindowDuration,
        TimeSpan sourceDuration,
        IEnumerable<AudioTranscriptionSegment> segments,
        bool isUserEdited,
        GenerationCaptionSuppressionReason suppressionReason) =>
        new(
            candidateId,
            neighborhoodId,
            sourceSelection,
            requestedStyle,
            sourceWindowStart,
            sourceWindowDuration,
            sourceDuration,
            segments,
            isUserEdited,
            suppressionReason,
            candidate: null,
            transcription: null);

    private GenerationCandidateCaptionTrack CreateRetained(
        GenerationCaptionStylePreset requestedStyle,
        IEnumerable<AudioTranscriptionSegment> segments,
        bool isUserEdited,
        GenerationCaptionSuppressionReason suppressionReason,
        bool retainGenerationObjects = true) =>
        new(
            CandidateId,
            NeighborhoodId,
            SourceSelection,
            requestedStyle,
            SourceWindowStart,
            SourceWindowDuration,
            SourceDuration,
            segments,
            isUserEdited,
            suppressionReason,
            retainGenerationObjects ? _candidate : null,
            retainGenerationObjects ? _transcription : null);

    private GenerationCandidateCaptionTrack(
        string candidateId,
        string neighborhoodId,
        GenerationCaptionSourceSelection sourceSelection,
        GenerationCaptionStylePreset requestedStyle,
        TimeSpan sourceWindowStart,
        TimeSpan sourceWindowDuration,
        TimeSpan sourceDuration,
        IEnumerable<AudioTranscriptionSegment> segments,
        bool isUserEdited,
        GenerationCaptionSuppressionReason suppressionReason,
        GenerationMomentCandidate? candidate,
        AudioTranscriptionResult? transcription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(neighborhoodId);
        ArgumentNullException.ThrowIfNull(sourceSelection);
        ArgumentNullException.ThrowIfNull(segments);
        if (!Enum.IsDefined(requestedStyle) ||
            !Enum.IsDefined(suppressionReason) ||
            sourceWindowStart < TimeSpan.Zero ||
            sourceWindowDuration <= TimeSpan.Zero ||
            sourceDuration <= TimeSpan.Zero ||
            sourceWindowStart + sourceWindowDuration > sourceDuration)
        {
            throw new ArgumentException(
                "A retained Studio caption track requires a valid source window and defined policies.");
        }

        AudioTranscriptionSegment[] snapshot = segments.ToArray();
        if (suppressionReason != GenerationCaptionSuppressionReason.None &&
            snapshot.Length != 0 ||
            snapshot.Any(static segment => segment is null) ||
            snapshot.Any(segment =>
                !segment.NeighborhoodId.Equals(
                    neighborhoodId,
                    StringComparison.Ordinal) ||
                segment.RelativeEnd > sourceWindowDuration ||
                segment.AbsoluteSourceStart < sourceWindowStart ||
                segment.AbsoluteSourceEnd > sourceWindowStart + sourceWindowDuration) ||
            snapshot.Zip(
                    snapshot.Skip(1),
                    static (left, right) =>
                        right.RelativeStart < left.RelativeEnd)
                .Any(static overlaps => overlaps))
        {
            throw new ArgumentException(
                "Retained Studio caption segments must remain ordered and inside their exact source window.",
                nameof(segments));
        }

        CandidateId = candidateId.Trim();
        NeighborhoodId = neighborhoodId.Trim();
        SourceSelection = sourceSelection;
        RequestedStyle = requestedStyle;
        SourceWindowStart = sourceWindowStart;
        SourceWindowDuration = sourceWindowDuration;
        SourceDuration = sourceDuration;
        Segments = Array.AsReadOnly(snapshot);
        IsUserEdited = isUserEdited;
        SuppressionReason = suppressionReason;
        _candidate = candidate;
        _transcription = transcription;
    }
}

public sealed class GenerationCaptionPreparationResult
{
    private readonly ReadOnlyCollection<GenerationCandidateCaptionTrack>
        _tracks;

    public GenerationCaptionPreparationResult(
        GenerationMomentFindingResult moments,
        IEnumerable<GenerationCandidateCaptionTrack> tracks,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(moments);
        ArgumentNullException.ThrowIfNull(tracks);
        GenerationCandidateCaptionTrack[] snapshot = tracks.ToArray();
        if (!moments.Request.Setup.CaptionSettings.IsEnabled ||
            elapsed < TimeSpan.Zero ||
            snapshot.Length != moments.SelectedCandidates.Count ||
            snapshot.Any(static track => track is null) ||
            !snapshot
                .Select(static track => track.Candidate)
                .SequenceEqual(moments.SelectedCandidates))
        {
            throw new ArgumentException(
                "Caption preparation requires one ordered track per selected moment.",
                nameof(tracks));
        }

        Moments = moments;
        _tracks = Array.AsReadOnly(snapshot);
        Elapsed = elapsed;
    }

    public GenerationMomentFindingResult Moments { get; }
    public IReadOnlyList<GenerationCandidateCaptionTrack> Tracks => _tracks;
    public TimeSpan Elapsed { get; }
    public int SuppressedTrackCount =>
        _tracks.Count(static track => track.IsSuppressed);

    public GenerationCandidateCaptionTrack FindTrack(string candidateId) =>
        _tracks.Single(
            track =>
                track.Candidate.Id.Equals(
                    candidateId,
                    StringComparison.Ordinal));
}

public sealed record GenerationCaptionPreparationProgress(
    string Title,
    string Detail,
    int CompletedClips,
    int TotalClips)
{
    public double Percentage =>
        TotalClips == 0
            ? 0
            : CompletedClips * 100d / TotalClips;
}

public interface IGenerationCaptionPreparationService
{
    Task<GenerationCaptionPreparationResult> PrepareAsync(
        GenerationMomentFindingResult moments,
        IProgress<GenerationCaptionPreparationProgress> progress,
        CancellationToken cancellationToken);

    Task<GenerationCandidateCaptionTrack> PrepareCandidateAsync(
        GenerationMomentCandidate candidate,
        GenerationCaptionSourceSelection selection,
        GenerationCaptionStylePreset style,
        CancellationToken cancellationToken);
}
