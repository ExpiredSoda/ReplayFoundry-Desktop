using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Generate.Handoff;

public interface IGenerationTimelineEditor
{
    void SplitAsset(string projectId, string assetId, TimeSpan sourcePosition);
    void MoveAsset(string projectId, string assetId, int direction);
    void SetTimelineMode(string projectId, GenerationMode mode);
}

public sealed partial class GenerationOutputSession : IGenerationTimelineEditor
{
    public void SplitAsset(string projectId, string assetId, TimeSpan sourcePosition) =>
        ChangeTimeline(projectId, project => project.SplitAsset(assetId, sourcePosition));
    public void MoveAsset(string projectId, string assetId, int direction) =>
        ChangeTimeline(projectId, project => project.MoveAsset(assetId, direction));
    public void SetTimelineMode(string projectId, GenerationMode mode) =>
        ChangeTimeline(projectId, project => project.WithTimelineAssets(project.Assets, mode));
    private void ChangeTimeline(string projectId, Func<GenerationOutputProject, GenerationOutputProject> change)
    {
        if (Current is null || !Current.Id.Equals(projectId, StringComparison.Ordinal))
            throw new InvalidOperationException("The timeline edit does not belong to the current Studio project.");
        Current = change(Current);
        CurrentChanged?.Invoke(this, new GenerationOutputChangedEventArgs(Current));
    }
}

public sealed partial class GenerationOutputProject
{
    internal GenerationOutputProject SplitAsset(string assetId, TimeSpan sourcePosition)
    {
        GenerationOutputAsset asset = Assets.SingleOrDefault(asset => asset.Id.Equals(assetId, StringComparison.Ordinal)) ??
            throw new ArgumentException("Choose a clip from this project.", nameof(assetId));
        TimeSpan minimum = TimeSpan.FromMilliseconds(100);
        if (sourcePosition < asset.SourceStart + minimum || sourcePosition > asset.SourceEnd - minimum)
            throw new ArgumentException("Split inside the clip, leaving at least 0.1 seconds on either side.", nameof(sourcePosition));
        var result = new List<GenerationOutputAsset>();
        foreach (GenerationOutputAsset existing in Assets)
        {
            if (ReferenceEquals(existing, asset))
            {
                result.Add(asset.WithStudioEdits(asset.SourceStart, sourcePosition, asset.Appearance));
                result.Add(asset.CreateSplitPiece(sourcePosition));
            }
            else result.Add(existing);
        }
        return WithTimelineAssets(result);
    }
    internal GenerationOutputProject MoveAsset(string assetId, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var assets = Assets.ToList();
        int index = assets.FindIndex(asset => asset.Id.Equals(assetId, StringComparison.Ordinal));
        if (index < 0) throw new ArgumentException("Choose a clip from this project.", nameof(assetId));
        int target = index + direction;
        if (target >= 0 && target < assets.Count) (assets[index], assets[target]) = (assets[target], assets[index]);
        return WithTimelineAssets(assets);
    }
    internal GenerationOutputProject WithTimelineAssets(IEnumerable<GenerationOutputAsset> assets, GenerationMode? mode = null)
    {
        if (IsFinalized) throw new InvalidOperationException("Reopen the project before changing its cut list.");
        return new(Id, mode ?? Mode, OutputDirectory, RequestedCount, FulfillmentPreference, FulfillmentOutcome,
            assets.Select((asset, index) => asset.WithTimelineRank(index + 1)), CreatedAtUtc,
            resultCountMode: ResultCountMode, hiddenMoments: HiddenMoments,
            candidateSetFingerprint: CandidateSetFingerprint, sourceMedia: SourceMedia);
    }
}

public sealed partial class GenerationOutputAsset
{
    internal GenerationOutputAsset WithTimelineRank(int rank) => rank == Rank ? this :
        RestoreStudioHandoff(Id, rank, SourceMedia, null, null, SourceStart, SourceEnd,
            OriginalSourceStart, OriginalSourceEnd, Score, QualityTarget, SelectionReason, Explanation,
            Captions, Appearance, EditorialContext, EditorialMetadata, PreferenceFeatures, Disposition, RenderSettings);

    internal GenerationOutputAsset CreateSplitPiece(TimeSpan sourceStart)
    {
        string id = "split-" + Guid.NewGuid().ToString("N");
        GenerationCandidateCaptionTrack? captions = Captions is not { } track ? null :
            GenerationCandidateCaptionTrack.RestoreStudioHandoff(id, track.NeighborhoodId, track.SourceSelection,
                track.RequestedStyle, track.SourceWindowStart, track.SourceWindowDuration, track.SourceDuration,
                track.Segments, track.IsUserEdited, track.SuppressionReason);
        const string explanation = "The creator split this section from an existing clip. Review its boundaries and metadata.";
        IReadOnlyList<ClipEditorialTranscriptContext>? transcripts = captions is null
            ? EditorialContext?.WithSourceRange(sourceStart, SourceEnd).Transcripts
            : RetainedCaptionEditorialTranscriptProjector.Project(captions, sourceStart, SourceEnd);
        var context = new ClipEditorialContext(id, SourceFullPath, System.IO.Path.GetFileNameWithoutExtension(SourceFullPath),
            sourceStart, SourceEnd, SourceDuration, 0, explanation, transcripts: transcripts,
            gameContext: EditorialContext?.GameContext, gameKnowledge: EditorialContext?.GameKnowledge,
            gameplayRegion: RenderSettings.GameplayRegion);
        var metadata = new ClipEditorialMetadataDraft($"Split section {Rank + 1}",
            "A selected section from this recording. Review its title and description before publishing.", [],
            ClipEditorialMetadataOrigin.Heuristic, new ClipEditorialMetadataGeneratorIdentity("studio-split", "1.0"), 0);
        return RestoreStudioHandoff(id, Rank + 1, SourceMedia, null, null, sourceStart, SourceEnd,
            OriginalSourceStart, OriginalSourceEnd, 0, QualityTarget, GenerationCandidateSelectionReason.ManualSourceCut,
            explanation, captions, Appearance, context, metadata, null, Disposition, RenderSettings);
    }
}
