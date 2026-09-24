using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static async Task PopRepairsTimingBeforeEncoding()
    {
        foreach (var mode in new[] { GenerationMode.IndividualClips, GenerationMode.Montage })
        {
            using var fixture = CreateFixture(mode, hasAudio: true, createPhysicalSource: true);
            var draft = fixture.CreateDraft();
            var asset = PopWithMissingWord(draft.PrimaryAsset);
            draft = draft.ReplaceAsset(asset);
            var aligner = new PopTestAligner();
            var runner = new WritingProcessRunner();
            var result = await fixture.CreateStudioRenderer(runner, aligner).FinalizeAsync(draft,
                new RecordingProgress<StudioProjectRenderProgress>(), CancellationToken.None);
            var repaired = result.FinalizedProject.Assets.Single(value => value.Id == asset.Id);
            TestAssert.Equal(1, aligner.Calls, "Only the incomplete phrase needs acoustic alignment.");
            TestAssert.Equal(asset.Captions!.SourceSelection.AbsoluteAudioStreamIndex, aligner.Request!.AbsoluteAudioStreamIndex,
                "Repair must listen to the retained caption audio track, not the mixed output.");
            TestAssert.Equal("I know now.", repaired.Captions!.Segments[0].Text, "Repair cannot rewrite the user's transcript.");
            TestAssert.Equal(2, asset.Captions.Segments[0].Words.Count, "Render preparation must not mutate the editable source draft.");
            TestAssert.Equal(3, repaired.Captions.Segments[0].Words.Count, "The finalized handoff must retain the repaired clocks.");
            TestAssert.True(repaired.Captions.Segments[0].Warnings.Any(warning => warning.Code == AudioTranscriptionWarningCode.CorrectedTextAlignment),
                "Repaired words must retain acoustic provenance for later review.");
            string[] events = runner.CaptionScripts.Single().Split('\n').Where(line => line.StartsWith("Dialogue:", StringComparison.Ordinal)).ToArray();
            TestAssert.Equal(3, events.Length, "Pop must emit exactly one event for each spoken word.");
            TestAssert.True(events.All(line => Regex.Matches(Regex.Replace(line.Split(',', 10)[9], @"\{[^}]*\}", ""), @"\S+").Count == 1),
                "No full sentence or multiword fragment may be burned as one Pop event.");
            var preview = new StudioLiveCaptionFrameCalculator();
            TestAssert.Null(preview.Calculate(repaired.Captions, StudioCaptionWordLimitPreset.FullSegment, GenerationCaptionStylePreset.Pop,
                asset.SourceStart.TotalSeconds + 2, asset.SourceStart, asset.SourceEnd).Text,
                "Pop must disappear during the measured pause between I and know.");
            TestAssert.Equal("know", preview.Calculate(repaired.Captions, StudioCaptionWordLimitPreset.FullSegment, GenerationCaptionStylePreset.Pop,
                asset.SourceStart.TotalSeconds + 4.1, asset.SourceStart, asset.SourceEnd).Text!, "Pop preview must follow the repaired word clock.");
        }
    }

    private static async Task PopRejectsUnresolvedTiming()
    {
        foreach (string failure in new[] { "unavailable", "changed text", "weak", "cancelled" })
        {
            using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, createPhysicalSource: true);
            var draft = fixture.CreateDraft();
            var asset = PopWithMissingWord(draft.PrimaryAsset);
            draft = draft.ReplaceAsset(asset);
            var runner = new WritingProcessRunner();
            var aligner = failure == "unavailable" ? null : new PopTestAligner(failure);
            bool rejected = false;
            try { await fixture.CreateStudioRenderer(runner, aligner).FinalizeAsync(draft,
                new RecordingProgress<StudioProjectRenderProgress>(), CancellationToken.None); }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or OperationCanceledException) { rejected = true; }
            TestAssert.True(rejected, "Unusable or cancelled alignment must be surfaced before encoding.");
            TestAssert.Equal(0, runner.Requests.Count, "No encoder or thumbnail command may run before Pop timing is ready.");
            TestAssert.False(Directory.Exists(draft.OutputDirectory), "A failed preflight must not leave a misleading finished export.");
            TestAssert.Throws<InvalidOperationException>(() => AssSubtitleDocumentBuilder.Build(asset.Captions!, 1080, 1920),
                "Calling the subtitle writer directly must not bypass the one-word Pop contract.");
            TestAssert.Null(new StudioLiveCaptionFrameCalculator().Calculate(asset.Captions, StudioCaptionWordLimitPreset.FullSegment,
                GenerationCaptionStylePreset.Pop, asset.SourceStart.TotalSeconds + .1, asset.SourceStart, asset.SourceEnd).Text,
                "Preview must not present an untimed phrase as sentence-sized Pop.");
        }
    }

    private static async Task PopPreservesUsableProviderWords()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, createPhysicalSource: true);
        var missing = PopWithMissingWord(fixture.CreateDraft().PrimaryAsset);
        var original = missing.Captions!.Segments[0];
        var repaired = await StudioPopCaptionPreparation.PrepareAsync(missing, new PartialPopTestAligner(), null, CancellationToken.None);
        var words = repaired.Captions!.Segments[0].Words;
        TestAssert.Equal(3, words.Count, "The omitted opening word must acquire measured timing.");
        TestAssert.Equal(missing.SourceStart + TimeSpan.FromSeconds(4.05), words[1].AbsoluteSourceStart,
            "A strong acoustic match can replace a provider word that collides with the repaired opening word.");
        TestAssert.True(ReferenceEquals(original.Words[1], words[2]),
            "The usable final provider word must survive even when its proposed replacement has weak acoustic fit.");
        var provenance = StudioCaptionAlignmentProvenance.Read(repaired.Captions.Segments[0].Warnings
            .Single(warning => warning.Code == AudioTranscriptionWarningCode.CorrectedTextAlignment).Message)!;
        TestAssert.Equal(2, provenance.Words.Count, "Provenance must describe only adopted acoustic clocks.");
        TestAssert.True(provenance.Words.All(word => word.AcousticScore >= .15), "No weak proposed clock may be adopted automatically.");
        bool rejected = false;
        try { await StudioPopCaptionPreparation.PrepareAsync(missing, new PartialPopTestAligner(conflict: true), null, CancellationToken.None); }
        catch (StudioCaptionTimingException) { rejected = true; }
        TestAssert.True(rejected, "A weak replacement cannot resolve a collision with an unusable original clock.");
    }

    private sealed class PartialPopTestAligner(bool conflict = false) : ICorrectedCaptionAlignmentService
    {
        public Task<CorrectedCaptionAlignmentResult> AlignAsync(CorrectedCaptionAlignmentRequest request,
            IProgress<string>? progress, CancellationToken cancellationToken) => Task.FromResult(new CorrectedCaptionAlignmentResult(
                [new("I", TimeSpan.FromSeconds(3.9), TimeSpan.FromSeconds(4.04), .9),
                 new("know", TimeSpan.FromSeconds(4.05), TimeSpan.FromSeconds(conflict ? 4.3 : 4.2), .9),
                 new("now.", TimeSpan.FromSeconds(4.35), TimeSpan.FromSeconds(4.6), .02)],
                "test acoustic observations", new string('A', 64), TimeSpan.Zero, "review", new string('B', 64)));
    }

    private static async Task PopLeavesCleanAndTimedExportsUntouched()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, createPhysicalSource: true);
        var missing = PopWithMissingWord(fixture.CreateDraft().PrimaryAsset);
        var clean = missing.WithRenderSettings(new StudioRenderSettings(StudioOutputCanvas.Portrait, burnCaptions: false));
        var aligner = new PopTestAligner();
        TestAssert.True(ReferenceEquals(clean, await StudioPopCaptionPreparation.PrepareAsync(clean, aligner, null, CancellationToken.None)),
            "Separate subtitle delivery does not require Pop word timing or model work.");
        TestAssert.Equal(0, aligner.Calls, "Clean exports must not start unnecessary AI work.");
        var repaired = await StudioPopCaptionPreparation.PrepareAsync(missing, aligner, null, CancellationToken.None);
        TestAssert.True(ReferenceEquals(repaired, await StudioPopCaptionPreparation.PrepareAsync(repaired, aligner, null, CancellationToken.None)),
            "A second render must preserve complete measured word clocks exactly.");
        TestAssert.Equal(1, aligner.Calls, "Already repaired words must not be aligned again.");
        var segment = repaired.Captions!.Segments[0];
        var punctuation = new AudioTranscriptionWord("-", TimeSpan.FromSeconds(.3), TimeSpan.FromSeconds(.4),
            repaired.SourceStart + TimeSpan.FromSeconds(.3), repaired.SourceStart + TimeSpan.FromSeconds(.4));
        var punctuated = new AudioTranscriptionSegment(segment.Id, segment.NeighborhoodId, "I - know now.", segment.RelativeStart,
            segment.RelativeEnd, segment.AbsoluteSourceStart, segment.AbsoluteSourceEnd,
            [segment.Words[0], punctuation, segment.Words[1], segment.Words[2]]);
        var track = repaired.Captions;
        var punctuatedTrack = GenerationCandidateCaptionTrack.RestoreStudioHandoff(track.CandidateId, track.NeighborhoodId,
            track.SourceSelection, track.RequestedStyle, track.SourceWindowStart, track.SourceWindowDuration, track.SourceDuration,
            [punctuated], false, track.SuppressionReason);
        var punctuatedAsset = repaired.WithCaptionTrack(punctuatedTrack);
        TestAssert.True(ReferenceEquals(punctuatedAsset,
            await StudioPopCaptionPreparation.PrepareAsync(punctuatedAsset, aligner, null, CancellationToken.None)),
            "A standalone punctuation token must not require acoustic repair of otherwise complete speech.");
        var cues = StudioCaptionPresentationPolicy.ProjectCues(punctuatedTrack, StudioCaptionWordLimitPreset.FullSegment);
        TestAssert.True(cues.All(cue => cue.WordSpans.Count > 0), "Punctuation must not trigger phrase fallback in preview or render.");
        TestAssert.Equal(3, cues.Sum(cue => cue.Words.Count), "Only the three spoken words should animate.");
        TestAssert.True(AssSubtitleDocumentBuilder.Build(punctuatedTrack, 1080, 1920).Script.Contains('-'),
            "The dash remains in the rendered caption text.");
        var cut = StudioCaptionCutProjection.Project(punctuatedTrack, repaired.SourceStart + TimeSpan.FromSeconds(.28), repaired.SourceEnd);
        TestAssert.Equal("- know now.", cut.Track.Segments[0].Text, "A cut past the first word must preserve punctuation without failing its word mapping.");
        TestAssert.Equal(2, cut.Track.Segments[0].Words.Count, "Cut projection must retain only the two complete spoken words.");
    }

    private static GenerationOutputAsset PopWithMissingWord(GenerationOutputAsset asset)
    {
        TimeSpan start = asset.SourceStart;
        var segment = new AudioTranscriptionSegment("missing-word", "pop-review", "I know now.", TimeSpan.Zero, TimeSpan.FromSeconds(5),
            start, start + TimeSpan.FromSeconds(5),
            [new("know", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4.25), start + TimeSpan.FromSeconds(4), start + TimeSpan.FromSeconds(4.25)),
             new("now.", TimeSpan.FromSeconds(4.25), TimeSpan.FromSeconds(4.6), start + TimeSpan.FromSeconds(4.25), start + TimeSpan.FromSeconds(4.6))],
            language: new AudioTranscriptionLanguage("en", "English"));
        var track = GenerationCandidateCaptionTrack.RestoreStudioHandoff(asset.Id, "pop-review",
            new GenerationCaptionSourceSelection(asset.SourceFullPath, asset.SourceMedia.AudioStreams[0].Index, CaptionAudioContentRole.CreatorCommentary),
            GenerationCaptionStylePreset.Pop, start, asset.Duration, asset.SourceDuration, [segment], false, GenerationCaptionSuppressionReason.None);
        return asset.WithCaptionTrack(track).WithStudioEdits(asset.SourceStart, asset.SourceEnd,
            new StudioClipAppearance(GenerationCaptionStylePreset.Pop, 47, StudioVideoEffectPreset.None, 0));
    }

    private sealed class PopTestAligner(string? failure = null) : ICorrectedCaptionAlignmentService
    {
        public int Calls { get; private set; }
        public CorrectedCaptionAlignmentRequest? Request { get; private set; }
        public Task<CorrectedCaptionAlignmentResult> AlignAsync(CorrectedCaptionAlignmentRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            Calls++; Request = request;
            if (failure == "cancelled") throw new OperationCanceledException();
            return Task.FromResult(new CorrectedCaptionAlignmentResult(
                [new(failure == "changed text" ? "You" : "I", TimeSpan.FromSeconds(.05), TimeSpan.FromSeconds(.25), failure == "weak" ? .1 : .9),
                 new("know", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4.25), .9),
                 new("now.", TimeSpan.FromSeconds(4.25), TimeSpan.FromSeconds(4.6), .9)],
                "test acoustic observations", new string('A', 64), TimeSpan.Zero, "review", new string('B', 64)));
        }
    }
}
