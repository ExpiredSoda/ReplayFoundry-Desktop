using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static async Task CaptionEditBeforeSaveKeepsAuthoredHistory()
    {
        (GenerationOutputAsset original, _) = await CreateAssetAsync(hasAudio: true);
        original = original.WithCaptionTrack(AuthoredTrack(original, changed: false, timingOnly: false));
        original = original.WithCurrentCutEditorialMetadata(original.EditorialContext!, original.EditorialMetadata!);
        var corrected = original.WithCaptionTrack(AuthoredTrack(original, changed: true, timingOnly: false));
        TestAssert.False(corrected.IsEditorialMetadataCurrentForCut, "Unaudited wording cannot be rebound by a caption edit.");
        TestAssert.Equal(original.EditorialAuthoredContextRevision, corrected.EditorialAuthoredContextRevision, "Caption editing must preserve the old authored identity.");
        var project = AuthoredProject(corrected);
        var session = new GenerationOutputSession(); session.Publish(project);
        var service = new StudioEditorialMetadataService(session, null, new ClipEditorialProfileSession());
        TestAssert.True(service.LoadDraft(corrected).NeedsCurrentCutRefresh, "The editor must visibly report stale caption context before saving.");
        service.Save(project, corrected, "The final door was closed", "The doorway remained shut at the end.", "");
        var saved = session.Current!.PrimaryAsset;
        TestAssert.True(saved.IsEditorialMetadataCurrentForCut, "An explicit save establishes the new authored context.");
        TestAssert.Equal(original.EditorialAuthoredContextRevision, saved.EditorialMetadata!.CopyVersions.Single().ContextFingerprint, "Earlier wording must retain its original caption context.");
        using var editor = new StudioEditorialMetadataViewModel(session, null, new ClipEditorialProfileSession());
        editor.Bind(session.Current, saved);
        TestAssert.False(editor.RestoreCopyCommand.CanExecute(null), "The prior caption context must remain compare-only after the correction.");
    }

    private static async Task CaptionEditBeforeRerollKeepsAuthoredHistory()
    {
        foreach (bool timingOnly in new[] { false, true })
        {
            (GenerationOutputAsset original, _) = await CreateAssetAsync(hasAudio: true);
            original = original.WithCaptionTrack(AuthoredTrack(original, false, timingOnly));
            original = original.WithCurrentCutEditorialMetadata(original.EditorialContext!, original.EditorialMetadata!);
            var corrected = original.WithCaptionTrack(AuthoredTrack(original, true, timingOnly));
            TestAssert.Equal(original.EditorialContext!.EditorialBrief.Fingerprint, corrected.EditorialContext!.EditorialBrief.Fingerprint,
                "The actual correction must be outside the brief's bounded text/timing identity.");
            TestAssert.False(corrected.IsEditorialMetadataCurrentForCut, "Full authored context must detect a late phrase or timing-only correction.");
            var project = AuthoredProject(corrected);
            var session = new GenerationOutputSession(); session.Publish(project);
            var generator = new RecordingRequestMetadataGenerator();
            var service = new StudioEditorialMetadataService(session, generator, new ClipEditorialProfileSession());
            await service.RerollAsync(project, corrected, "Chat", "", "", false, CancellationToken.None);
            var rerolled = session.Current!.PrimaryAsset;
            TestAssert.True(rerolled.IsEditorialMetadataCurrentForCut, "Successful reroll must bind its new package to the full current context.");
            TestAssert.Equal(original.EditorialAuthoredContextRevision, rerolled.EditorialMetadata!.CopyVersions.Single().ContextFingerprint,
                "Reroll must not assign edited-caption authority to the earlier wording.");
            TestAssert.Equal(0, generator.LastRequest!.PriorAcceptedTitleExclusions.Count, "Stale wording must not constrain the refreshed factual context's title history.");
        }
    }

    private static GenerationOutputProject AuthoredProject(GenerationOutputAsset asset) => new(
        "authored-caption-context", GenerationMode.IndividualClips, Path.GetFullPath("authored-caption-context-output"),
        1, ClipFulfillmentPreference.FillRequestedCount, GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
        [asset], DateTimeOffset.UtcNow);

    private static GenerationCandidateCaptionTrack AuthoredTrack(GenerationOutputAsset asset, bool changed, bool timingOnly)
    {
        const string neighborhood = "authored-caption-context";
        var segments = Enumerable.Range(0, 13).Select(index =>
        {
            TimeSpan start = TimeSpan.FromSeconds(1 + index * 2) +
                (changed && timingOnly && index == 0 ? TimeSpan.FromMilliseconds(100) : TimeSpan.Zero);
            TimeSpan end = TimeSpan.FromSeconds(2 + index * 2);
            string text = changed && !timingOnly && index == 12 ? "The final door was closed." : $"Sentence {index + 1} stays here.";
            return new AudioTranscriptionSegment($"authored-{index}", neighborhood, text, start, end,
                asset.SourceStart + start, asset.SourceStart + end);
        }).ToArray();
        return GenerationCandidateCaptionTrack.RestoreStudioHandoff(asset.Id, neighborhood,
            new GenerationCaptionSourceSelection(asset.SourceFullPath, asset.SourceMedia.AudioStreams.Single().Index,
                CaptionAudioContentRole.CreatorCommentary, GenerationCaptionLanguagePolicy.English),
            asset.Appearance.CaptionStyle, asset.SourceStart, asset.Duration, asset.SourceDuration, segments,
            isUserEdited: true, GenerationCaptionSuppressionReason.None);
    }
}
