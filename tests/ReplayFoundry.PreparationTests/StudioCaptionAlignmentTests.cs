using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static GenerationOutputProject CaptionAlignmentDraft(PipelineFixture fixture)
    {
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep, CreateTranscription(candidate));
        return fixture.CreateDraft(new GenerationCaptionPreparationResult(fixture.Moments, [track], TimeSpan.Zero));
    }
    private sealed class FakeCorrectedAlignment(Func<CorrectedCaptionAlignmentRequest, CancellationToken, Task<CorrectedCaptionAlignmentResult>> run) : ICorrectedCaptionAlignmentService
    {
        public int Calls { get; private set; }
        public CorrectedCaptionAlignmentRequest? Request { get; private set; }
        public Task<CorrectedCaptionAlignmentResult> AlignAsync(CorrectedCaptionAlignmentRequest request, IProgress<string>? progress, CancellationToken token)
        { Calls++; Request = request; return run(request, token); }
    }
    private sealed class UnexpectedCaptionPreparation : IGenerationCaptionPreparationService
    {
        public int Calls { get; private set; }
        public Task<GenerationCaptionPreparationResult> PrepareAsync(GenerationMomentFindingResult moments,
            IProgress<GenerationCaptionPreparationProgress> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GenerationCandidateCaptionTrack> PrepareCandidateAsync(GenerationMomentCandidate candidate,
            GenerationCaptionSourceSelection selection, GenerationCaptionStylePreset style, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GenerationCandidateCaptionTrack> PrepareRetainedCandidateAsync(string candidateId, MediaProbeResult sourceMedia,
            TimeSpan sourceStart, TimeSpan sourceEnd, GenerationCaptionSourceSelection selection, GenerationCaptionStylePreset style,
            CancellationToken cancellationToken)
        { Calls++; return Task.FromException<GenerationCandidateCaptionTrack>(new InvalidOperationException("Unsupported transcription should not have started.")); }
    }
    private static CorrectedCaptionAlignmentResult FakeAlignmentResult(CorrectedCaptionAlignmentRequest request)
    {
        string[] tokens = Regex.Matches(request.CorrectedText, @"\S+").Select(static match => match.Value).ToArray();
        double width = (request.SourceEnd - request.SourceStart).TotalSeconds / tokens.Length;
        var words = tokens.Select((text, index) => new CorrectedCaptionAlignedWord(text,
            TimeSpan.FromSeconds(width * index + .01), TimeSpan.FromSeconds(width * (index + 1) - .01), index == 0 ? .1 : .85)).ToArray();
        return new(words, "Test-only injected acoustic model", new string('A', 64), TimeSpan.Zero,
            "Review proposed words", new string('B', 64));
    }
    private static async Task CorrectedAlignmentStagesAndPersistsAReviewableProposal()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true, createPhysicalSource: true);
        var initial = CaptionAlignmentDraft(fixture);
        var original = initial.PrimaryAsset;
        var asset = original.WithStudioEdits(original.SourceStart + TimeSpan.FromSeconds(.5), original.SourceEnd, original.Appearance);
        var project = initial.ReplaceAsset(asset);
        var session = new GenerationOutputSession(); session.Publish(project);
        using var editor = new StudioCaptionTrackEditorViewModel(session);
        var backend = new FakeCorrectedAlignment((request, _) => Task.FromResult(FakeAlignmentResult(request)));
        editor.ConfigureAlignment(backend); editor.Bind(project, asset);
        editor.Segments[0].Text = "Gandalf wins!";
        TestAssert.False(editor.AlignCorrectedTextCommand.CanExecute(editor.Segments[0]), "Auto must never imply English for a forced aligner.");
        editor.SelectedLanguage = GenerationCaptionLanguagePolicy.English;
        string corrected = editor.Segments[0].Text;
        await editor.AlignCorrectedTextAsync(editor.Segments[0]);
        TestAssert.Equal(corrected, editor.Segments[0].Text, "Alignment must preserve the user's exact corrected wording.");
        TestAssert.Equal("hello world", session.Current!.PrimaryAsset.Captions!.Segments[0].Text, "A proposal must not save or replace the retained transcript.");
        TestAssert.Equal(original.SourceStart + TimeSpan.FromSeconds(1), backend.Request!.SourceStart, "Alignment audio must use absolute phrase clocks after trimming.");
        var proposed = editor.Segments[0].Snapshot();
        TestAssert.Equal(.51d, proposed.Words![0].StartSeconds, "Relative alignment results must convert to the current cut's clock.");
        TestAssert.True(editor.Segments[0].Words[0].AcousticReview!.Contains("Weak acoustic match", StringComparison.Ordinal), "Weak matches need a visible per-word review cue.");
        TestAssert.True(editor.Status.Contains("nothing has been saved", StringComparison.Ordinal), "The proposed result must be identified as unsaved.");
        editor.UndoCommand.Execute(null);
        TestAssert.Equal("hello", editor.Segments[0].Words[0].Text, "One Undo must remove the entire alignment proposal.");
        TestAssert.Equal(corrected, editor.Segments[0].Text, "Undoing alignment must retain the corrected text from before alignment.");
        editor.RedoCommand.Execute(null);
        TestAssert.Equal(proposed.AlignmentProvenance, editor.Segments[0].AlignmentProvenance, "Redo must restore the exact auditable proposal.");
        editor.SaveCommand.Execute(null);
        var saved = session.Current!.PrimaryAsset.Captions!.Segments[0];
        TestAssert.True(saved.Words.All(word => word.ProviderReportedProbability is null), "Acoustic scores must never become word correctness probabilities.");
        var warning = saved.Warnings.Single(item => item.Code == AudioTranscriptionWarningCode.CorrectedTextAlignment);
        var provenance = StudioCaptionAlignmentProvenance.Read(warning.Message)!;
        TestAssert.Equal(StudioCaptionAlignmentProvenance.TextIdentity(corrected), provenance.TextSha256, "Provenance must identify the exact corrected text.");
        TestAssert.Equal(new string('B', 64), provenance.ExtractedPcmSha256, "Provenance must retain the actual extracted-audio hash supplied by the backend.");
        TestAssert.Equal(backend.Request.AbsoluteAudioStreamIndex, provenance.AudioStreamIndex, "Provenance must record the explicitly selected audio stream.");
        var document = StudioProjectDocumentMapper.Capture(session.Current, 1, session.Current.CreatedAtUtc.AddSeconds(1));
        var restored = StudioProjectDocumentMapper.Restore(document);
        TestAssert.Equal(warning.Message, restored.PrimaryAsset.Captions!.Segments[0].Warnings.Single(item => item.Code == AudioTranscriptionWarningCode.CorrectedTextAlignment).Message,
            "Alignment model/audio/text provenance must survive project persistence.");
        TestAssert.True(StudioCaptionTrackEditing.CreateDrafts(restored.PrimaryAsset)[0].Words![0].AcousticScore == .1,
            "Reopened aligned words must retain their advisory acoustic score without altering timing.");
    }

    private static async Task CorrectedAlignmentRejectsFailuresAndStaleResults()
    {
        foreach (string scenario in new[] { "failure", "wrong text", "out of bounds", "cancel", "text changed", "language changed", "source changed", "clip changed" })
        {
            using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true, createPhysicalSource: true);
            var project = CaptionAlignmentDraft(fixture); var session = new GenerationOutputSession(); session.Publish(project);
            using var editor = new StudioCaptionTrackEditorViewModel(session);
            var pending = new TaskCompletionSource<CorrectedCaptionAlignmentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<CorrectedCaptionAlignmentRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new FakeCorrectedAlignment((request, _) => { started.SetResult(request); return pending.Task; });
            editor.ConfigureAlignment(backend); editor.Bind(project, project.PrimaryAsset); editor.SelectedLanguage = GenerationCaptionLanguagePolicy.English;
            editor.Segments[0].Text = "Gandalf wins!";
            string originalWords = JsonSerializer.Serialize(editor.Segments[0].Snapshot().Words);
            Task operation = editor.AlignCorrectedTextAsync(editor.Segments[0]);
            var request = await started.Task;
            var result = FakeAlignmentResult(request);
            switch (scenario)
            {
                case "failure": pending.SetException(new InvalidOperationException("Injected backend failure")); break;
                case "wrong text": pending.SetResult(result with { Words = result.Words.Select((word, i) => i == 0 ? word with { Text = "Replacement" } : word).ToArray() }); break;
                case "out of bounds": pending.SetResult(result with { Words = result.Words.Select((word, i) => i == 1 ? word with { RelativeEnd = request.SourceEnd - request.SourceStart + TimeSpan.FromSeconds(1) } : word).ToArray() }); break;
                case "cancel": editor.CancelAlignmentCommand.Execute(null); pending.SetResult(result); break;
                case "text changed": editor.Segments[0].Text = "Newer corrected wording"; pending.SetResult(result); break;
                case "language changed": editor.SelectedLanguage = GenerationCaptionLanguagePolicy.Spanish; pending.SetResult(result); break;
                case "source changed": File.AppendAllText(request.SourceFullPath, "changed"); pending.SetResult(result); break;
                default: editor.Bind(project, project.PrimaryAsset); pending.SetResult(result); break;
            }
            await operation;
            TestAssert.False(editor.IsAligning, $"The {scenario} operation must release busy state.");
            TestAssert.Equal(originalWords, JsonSerializer.Serialize(editor.Segments[0].Snapshot().Words), $"{scenario} cannot replace existing word timings.");
            TestAssert.True(editor.Segments[0].AlignmentProvenance is null, $"{scenario} cannot attach provenance for an unapplied result.");
            TestAssert.Equal("hello world", session.Current!.PrimaryAsset.Captions!.Segments[0].Text, $"{scenario} cannot mutate the saved transcript.");
            if (scenario == "text changed") TestAssert.Equal("Newer corrected wording", editor.Segments[0].Text, "A late result must not overwrite newer user text.");
        }
    }

    private static Task CaptionWordHandlesAreBoundedAndUndoAsOneEdit()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var project = CaptionAlignmentDraft(fixture); var session = new GenerationOutputSession(); session.Publish(project);
        using var editor = new StudioCaptionTrackEditorViewModel(session); editor.Bind(project, project.PrimaryAsset);
        var word = editor.Segments[0].Words[0];
        var changed = new HashSet<string?>(); word.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        double initial = word.StartSeconds;
        UiUxApplicationSurfaceTests.RunOnSta(() =>
        {
            var control = new StudioCaptionWordTimingHandles { Word = word, Width = 240 };
            control.Measure(new Size(240, 32)); control.Arrange(new Rect(0, 0, 240, 32)); control.UpdateLayout();
            var start = (Thumb)control.FindName("StartHandle");
            start.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            start.RaiseEvent(new DragDeltaEventArgs(10, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            start.RaiseEvent(new DragDeltaEventArgs(10, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            start.RaiseEvent(new DragCompletedEventArgs(20, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            TestAssert.True(word.StartSeconds > initial && word.StartSeconds < word.EndSeconds, "Actual routed thumb drags must move the start inside the phrase and before its end.");
        });
        TestAssert.True(changed.Contains(nameof(StudioCaptionWordDraft.StartSeconds)) && changed.Contains(nameof(StudioCaptionWordDraft.StartTime)), "Dragging must notify both the numeric value and displayed text binding.");
        editor.UndoCommand.Execute(null);
        TestAssert.Equal(initial, editor.Segments[0].Words[0].StartSeconds, "One Undo must restore an entire multi-delta drag.");
        TestAssert.False(editor.HasUnsavedChanges, "Derived notifications must not create extra undo changes after restoring a drag.");
        editor.RedoCommand.Execute(null); word = editor.Segments[0].Words[0];
        word.MoveStartBy(-100); TestAssert.Equal(editor.Segments[0].StartSeconds, word.StartSeconds, "Start handles must stop at the phrase's start.");
        word.MoveEndBy(100); TestAssert.Equal(editor.Segments[0].EndSeconds, word.EndSeconds, "End handles must stop at the phrase's end.");
        var unset = new StudioCaptionWordDraft(new("new", double.NaN, double.NaN), 0, 2);
        unset.MoveStartBy(1); unset.MoveEndBy(1);
        TestAssert.True(double.IsNaN(unset.StartSeconds) && double.IsNaN(unset.EndSeconds), "Dragging cannot manufacture initial timings for unmatched words.");
        return Task.CompletedTask;
    }

    private static async Task SilentVideoSupportsManualCaptionAuthoring()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: false, createPhysicalSource: true);
        var project = fixture.CreateDraft(); var session = new GenerationOutputSession(); session.Publish(project);
        using var editor = new StudioCaptionTrackEditorViewModel(session);
        var backend = new FakeCorrectedAlignment((request, _) => Task.FromResult(FakeAlignmentResult(request)));
        editor.ConfigureAlignment(backend); editor.Bind(project, project.PrimaryAsset);
        editor.SelectedLanguage = GenerationCaptionLanguagePolicy.English;
        TestAssert.True(editor.AddCommand.CanExecute(null) && editor.ImportCommand.CanExecute(null), "A silent video must allow manual phrase authoring and subtitle import.");
        editor.ImportText("1\n00:00:00,250 --> 00:00:01,750\nWatch the landing!\n", SubtitleSidecarFormat.Srt);
        TestAssert.False(editor.AlignCorrectedTextCommand.CanExecute(editor.Segments[0]), "A silent video must not enable audio alignment.");
        await editor.AlignCorrectedTextAsync(editor.Segments[0]);
        TestAssert.Equal(0, backend.Calls, "A direct alignment call must also reject a missing audio stream.");
        TestAssert.False(editor.AudioAudition.ListenCommand.CanExecute(null), "A silent video cannot be auditioned.");
        editor.SaveCommand.Execute(null);
        var savedProject = session.Current!; var asset = savedProject.PrimaryAsset;
        TestAssert.True(asset.Captions?.IsUserEdited == true, "Imported captions on a silent video must save as user-authored captions. " + editor.Status);
        TestAssert.True(asset.EditorialContext?.Transcripts.Count is null or 0, "Manual captions on a silent video cannot be represented as a fabricated audio-stream transcript.");
        var document = StudioProjectDocumentMapper.Capture(savedProject, 1, savedProject.CreatedAtUtc.AddSeconds(1));
        var restored = StudioProjectDocumentMapper.Restore(document).PrimaryAsset;
        var cues = SubtitleSidecarSerializer.Parse(SubtitleSidecarSerializer.Build(restored.Captions!, restored.SourceStart,
            restored.Duration, SubtitleSidecarFormat.WebVtt), SubtitleSidecarFormat.WebVtt);
        TestAssert.Equal(TimeSpan.FromSeconds(.25), cues[0].Start, "Silent video caption clocks must survive save, restore and sidecar export.");
        TestAssert.Equal("Watch the landing!", cues[0].Text, "Manual wording must survive the complete silent video caption workflow.");
        var track = restored.Captions!;
        var unreviewed = GenerationCandidateCaptionTrack.RestoreStudioHandoff(track.CandidateId, track.NeighborhoodId, track.SourceSelection,
            track.RequestedStyle, track.SourceWindowStart, track.SourceWindowDuration, track.SourceDuration, track.Segments,
            isUserEdited: false, track.SuppressionReason);
        TestAssert.Throws<ArgumentException>(() => restored.WithCaptionTrack(unreviewed), "Silent media must still reject an automatic transcript that claims an uninspected audio stream.");
        using var foreign = CreateFixture(GenerationMode.IndividualClips, hasAudio: false, sourceName: "different-silent-video.mkv");
        TestAssert.Throws<ArgumentException>(() => foreign.CreateDraft().PrimaryAsset.WithCaptionTrack(track),
            "Manual silent captions cannot bypass source-path ownership.");
    }

    private static async Task EnglishAlignmentIsIndependentOfWhisperAvailability()
    {
        foreach (var capabilities in new[] { AudioTranscriptionModelLanguageCapabilities.Missing,
            new AudioTranscriptionModelLanguageCapabilities(AudioTranscriptionModelLanguageKind.Unknown, 0, false, "Unknown speech model metadata.") })
        {
            using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true, createPhysicalSource: true);
            var project = CaptionAlignmentDraft(fixture); var session = new GenerationOutputSession(); session.Publish(project);
            using var editor = new StudioCaptionTrackEditorViewModel(session);
            var aligner = new FakeCorrectedAlignment((request, _) => Task.FromResult(FakeAlignmentResult(request)));
            var preparation = new UnexpectedCaptionPreparation();
            editor.ConfigurePreparation(preparation); editor.ConfigureLanguageCapabilities(new StudioCaptionLanguageModel(capabilities)); editor.ConfigureAlignment(aligner);
            editor.Bind(project, project.PrimaryAsset);
            var english = editor.LanguageOptions.Single(choice => choice.Value == GenerationCaptionLanguagePolicy.English);
            TestAssert.False(english.IsAvailable, "Independent alignment cannot claim that missing or unknown Whisper supports transcription.");
            editor.SelectedLanguage = english.Value;
            TestAssert.True(editor.AlignCorrectedTextCommand.CanExecute(editor.Segments[0]), "Explicit English must enable the independent aligner when audio is available.");
            TestAssert.False(editor.RegenerateCommand.CanExecute(null) || editor.TranslateSecondaryCommand.CanExecute(null), "Whisper actions must remain blocked by their own model capabilities.");
            await editor.RegenerateAsync(); await editor.TranslateSecondaryAsync();
            TestAssert.Equal(0, preparation.Calls, "Direct transcription calls must respect the same unavailable-model guard as the buttons.");
            TestAssert.True(editor.LanguageUnavailableReason == capabilities.Description, "Studio must retain the honest Whisper availability explanation.");
            await editor.AlignCorrectedTextAsync(editor.Segments[0]);
            TestAssert.Equal(1, aligner.Calls, "The explicitly selected independent English aligner must run without Whisper.");
            TestAssert.True(editor.Segments[0].HasAlignmentProvenance, "The independent aligner must still stage a reviewable local result.");
        }
    }

    private static Task CaptionLanguageSelectionSurvivesDraftNotifications()
    {
        UiUxApplicationSurfaceTests.RunOnSta(() =>
        {
            using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
            var project = CaptionAlignmentDraft(fixture); var session = new GenerationOutputSession(); session.Publish(project);
            using var editor = new StudioCaptionTrackEditorViewModel(session);
            editor.ConfigureLanguageCapabilities(new StudioCaptionLanguageModel(AudioTranscriptionModelLanguageCapabilities.Missing));
            editor.ConfigureAlignment(new FakeCorrectedAlignment((request, _) => Task.FromResult(FakeAlignmentResult(request))));
            editor.Bind(project, project.PrimaryAsset);
            var combo = new ComboBox { DataContext = editor, DisplayMemberPath = "Name", SelectedValuePath = "Value" };
            combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(editor.LanguageOptions)));
            combo.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(editor.SelectedLanguage)) { Mode = BindingMode.TwoWay });
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
            TestAssert.Equal(GenerationCaptionLanguagePolicy.Auto, (GenerationCaptionLanguagePolicy)combo.SelectedValue,
                "A retained unavailable language must still have a visible selection.");
            var choices = editor.LanguageOptions; var streams = editor.AudioStreams;
            combo.SelectedValue = GenerationCaptionLanguagePolicy.English;
            editor.Segments[0].Text = "Hello, world!";
            editor.SetHostBusy(true); editor.SetHostBusy(false);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
            TestAssert.True(ReferenceEquals(choices, editor.LanguageOptions) && ReferenceEquals(streams, editor.AudioStreams),
                "Draft and progress notifications cannot replace option identities and clear native selections.");
            TestAssert.Equal(GenerationCaptionLanguagePolicy.English, (GenerationCaptionLanguagePolicy)combo.SelectedValue,
                "Explicit English must remain visibly selected after editing and progress notifications.");
            TestAssert.False(Validation.GetHasError(combo), "The selected enum must never receive a transient empty string from an item reset.");
            editor.SaveCommand.Execute(null); editor.Bind(session.Current, session.Current!.PrimaryAsset);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
            TestAssert.Equal(GenerationCaptionLanguagePolicy.English, editor.SelectedLanguage,
                "Saving the same clip must retain the explicitly chosen alignment language without relabeling its original transcription policy.");
            TestAssert.Equal(GenerationCaptionLanguagePolicy.English, (GenerationCaptionLanguagePolicy)combo.SelectedValue,
                "The native selection must remain visible after a saved-clip refresh.");
            TestAssert.False(editor.RegenerateCommand.CanExecute(null), "A stable independent English choice cannot unblock missing Whisper.");
        });
        return Task.CompletedTask;
    }

    private static Task SavingAnotherCaptionPreservesPartialOffcutEvidence()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var initial = CaptionAlignmentDraft(fixture); var original = initial.PrimaryAsset; var track = original.Captions!;
        var phrase = track.Segments[0];
        TimeSpan wordStart = TimeSpan.FromTicks(11234567), wordEnd = TimeSpan.FromTicks(18124999);
        var partial = new AudioTranscriptionSegment(phrase.Id, phrase.NeighborhoodId, phrase.Text,
            phrase.RelativeStart, phrase.RelativeEnd, phrase.AbsoluteSourceStart, phrase.AbsoluteSourceEnd,
            [new AudioTranscriptionWord("hello", wordStart, wordEnd, track.SourceWindowStart + wordStart,
                track.SourceWindowStart + wordEnd, .987654, true)], .8, new AudioTranscriptionLanguage("en"),
            speaker: "Creator", secondaryText: "Hola mundo");
        var partialTrack = GenerationCandidateCaptionTrack.RestoreStudioHandoff(track.CandidateId, track.NeighborhoodId,
            track.SourceSelection, track.RequestedStyle, track.SourceWindowStart, track.SourceWindowDuration,
            track.SourceDuration, [partial], track.IsUserEdited, track.SuppressionReason);
        var cut = original.WithCaptionTrack(partialTrack).WithStudioEdits(original.SourceStart + TimeSpan.FromSeconds(2.5),
            original.SourceEnd, original.Appearance);
        var project = initial.ReplaceAsset(cut); var session = new GenerationOutputSession(); session.Publish(project);
        var drafts = StudioCaptionTrackEditing.CreateDrafts(cut).Concat([
            new StudioCaptionSegmentEdit("new-visible-caption", "A new visible phrase", 0, .5, [])]).ToArray();
        var saved = StudioCaptionTrackEditing.Apply(session, project, cut, drafts);
        TestAssert.Equal(JsonSerializer.Serialize(partial), JsonSerializer.Serialize(saved.Captions!.Segments[0]),
            "Saving a visible row must preserve the entire untouched offcut phrase, partial measured words, exact source ticks, probabilities and language.");
        var extended = saved.WithStudioEdits(TimeSpan.FromTicks(Math.Max(0, track.SourceWindowStart.Ticks - TimeSpan.TicksPerSecond)),
            saved.SourceEnd, saved.Appearance);
        var extendedProject = session.Current!.ReplaceAsset(extended); session.Publish(extendedProject);
        var resaved = StudioCaptionTrackEditing.Apply(session, extendedProject, extended, StudioCaptionTrackEditing.CreateDrafts(extended));
        var actual = resaved.Captions!.Segments[0].Words.Single();
        TestAssert.Equal(partial.Words[0].AbsoluteSourceStart, actual.AbsoluteSourceStart, "Expanding the retained clock cannot round an unchanged measured start.");
        TestAssert.Equal(partial.Words[0].AbsoluteSourceEnd, actual.AbsoluteSourceEnd, "Expanding the retained clock cannot round an unchanged measured end.");
        TestAssert.Equal(partial.Words[0].ProviderReportedProbability, actual.ProviderReportedProbability, "An unchanged measured word keeps its provider evidence after expansion.");
        TestAssert.Equal(actual.AbsoluteSourceStart - resaved.Captions.SourceWindowStart, actual.RelativeStart,
            "Only the relative clock may rebase when the source window expands.");
        return Task.CompletedTask;
    }

    private static Task CaptionPreviewWarningsUseOnlyVisiblePresentationCoverage()
    {
        UiUxApplicationSurfaceTests.RunOnSta(() =>
        {
            using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
            var initial = CaptionAlignmentDraft(fixture); var original = initial.PrimaryAsset; var track = original.Captions!;
            TimeSpan start = track.SourceWindowStart;
            var partial = new AudioTranscriptionSegment("partial-offcut", track.NeighborhoodId, "Only some words are measured",
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), start + TimeSpan.FromSeconds(3), start + TimeSpan.FromSeconds(4),
                [new AudioTranscriptionWord("some", TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(3.4),
                    start + TimeSpan.FromSeconds(3.2), start + TimeSpan.FromSeconds(3.4), .9)]);
            var retained = GenerationCandidateCaptionTrack.RestoreStudioHandoff(track.CandidateId, track.NeighborhoodId, track.SourceSelection,
                track.RequestedStyle, track.SourceWindowStart, track.SourceWindowDuration, track.SourceDuration,
                [track.Segments[0], partial], track.IsUserEdited, track.SuppressionReason);
            var asset = original.WithCaptionTrack(retained).WithStudioEdits(start, start + TimeSpan.FromSeconds(2.5), original.Appearance);
            var project = initial.ReplaceAsset(asset);
            using var preview = new StudioPreviewViewModel(mediaService: null);
            preview.Bind(true, project, asset);
            TestAssert.True(preview.LiveCaptionPresentationWarning is null,
                "Fully measured visible captions cannot show a phrase-fallback warning from partial retained words outside the cut.");
            TestAssert.Equal(JsonSerializer.Serialize(partial), JsonSerializer.Serialize(asset.Captions!.Segments[1]),
                "Calculating current-cut guidance must preserve the full offcut measured evidence.");
            var extended = asset.WithStudioEdits(start, start + TimeSpan.FromSeconds(4.5), asset.Appearance);
            preview.Bind(true, project.ReplaceAsset(extended), extended);
            string warning = preview.LiveCaptionPresentationWarning ?? "";
            TestAssert.True(warning.Contains("The rest follow the speech", StringComparison.Ordinal) &&
                warning.Contains("Some words appear together", StringComparison.Ordinal),
                "When both timing types become visible, guidance must describe mixed coverage without claiming all captions fall back.");
            var empty = asset.WithStudioEdits(start, start + TimeSpan.FromSeconds(.5), asset.Appearance);
            preview.Bind(true, project.ReplaceAsset(empty), empty);
            TestAssert.True(preview.LiveCaptionPresentationWarning is null, "A cut with no visible caption pages cannot report a timing fallback.");
        });
        return Task.CompletedTask;
    }

    private static Task PopDisplaysEditedCueSlicesWithoutRewritingMeasuredWords()
    {
        UiUxApplicationSurfaceTests.RunOnSta(() =>
        {
            using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
            var project = CaptionAlignmentDraft(fixture); var original = project.PrimaryAsset.Captions!;
            var source = original.Segments[0];
            var edited = new AudioTranscriptionSegment(source.Id, source.NeighborhoodId, "“HELLO,” (World!)",
                source.RelativeStart, source.RelativeEnd, source.AbsoluteSourceStart, source.AbsoluteSourceEnd,
                source.Words, source.ProviderReportedConfidence, source.Language, source.Warnings);
            var track = original.WithEditedSegments([edited]).WithRequestedStyle(GenerationCaptionStylePreset.Pop);
            string originalWords = JsonSerializer.Serialize(track.Segments[0].Words);
            var calculator = new StudioLiveCaptionFrameCalculator();
            LiveCaptionFrameState Frame(double seconds, TimeSpan start, TimeSpan end) => calculator.Calculate(track,
                StudioCaptionWordLimitPreset.Streamlined, GenerationCaptionStylePreset.Pop, seconds, start, end);
            TimeSpan cutStart = track.SourceWindowStart, cutEnd = cutStart + track.SourceWindowDuration;
            var first = Frame(source.Words[0].AbsoluteSourceStart.TotalSeconds + .1, cutStart, cutEnd);
            var second = Frame(source.Words[1].AbsoluteSourceStart.TotalSeconds + .1, cutStart, cutEnd);
            TestAssert.Equal("“HELLO,”", first.Text!, "Pop must display edited case, opening quotes and trailing punctuation from the cue.");
            TestAssert.Equal("(World!)", second.Text!, "Pop must assign the following word's opening bracket and final punctuation to its display slice.");
            TestAssert.Equal("(World!)", Frame(source.Words[1].AbsoluteSourceStart.TotalSeconds,
                source.Words[1].AbsoluteSourceStart, source.AbsoluteSourceEnd).Text!,
                "A trimmed caption boundary must retain edited display punctuation instead of rebuilding text from raw measured words.");
            foreach (var casing in new[] { StudioCaptionCasing.Original, StudioCaptionCasing.Uppercase, StudioCaptionCasing.Lowercase })
                foreach (int spacing in new[] { 100, 150 })
                {
                    var typography = new StudioCaptionTypography(casing: casing, lineSpacingPercent: spacing);
                    var document = AssSubtitleDocumentBuilder.Build(track, 1080, 1920, captionTypography: typography);
                    TestAssert.True(document.Script.Contains(typography.DisplayText("“HELLO,”"), StringComparison.Ordinal) &&
                        document.Script.Contains(typography.DisplayText("(World!)"), StringComparison.Ordinal),
                        "Default and explicit-line ASS Pop must use the same edited display slices and selected casing.");
                }
            var trimmed = StudioCaptionCutProjection.Project(track, source.Words[1].AbsoluteSourceStart, source.AbsoluteSourceEnd).Track;
            TestAssert.Equal("(World!)", trimmed.Segments.Single().Text,
                "The shared cut projection must retain the edited word's exact visible punctuation.");
            TestAssert.True(ReferenceEquals(source.Words[1], trimmed.Segments[0].Words[0]),
                "Cut text projection must retain the original measured word object and its exact source clocks.");
            TestAssert.Equal(originalWords, JsonSerializer.Serialize(track.Segments[0].Words),
                "Preview, casing, cut projection and ASS generation cannot rewrite raw measured words or their provenance.");
        });
        return Task.CompletedTask;
    }

    private static async Task ForeignStreamAlignmentCannotInheritOriginalEditorialRole()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, audioStreamCount: 2,
            captionsEnabled: true, createPhysicalSource: true);
        var project = CaptionAlignmentDraft(fixture); var session = new GenerationOutputSession(); session.Publish(project);
        using var editor = new StudioCaptionTrackEditorViewModel(session);
        editor.ConfigureAlignment(new FakeCorrectedAlignment((request, _) => Task.FromResult(FakeAlignmentResult(request))));
        editor.Bind(project, project.PrimaryAsset); editor.SelectedLanguage = GenerationCaptionLanguagePolicy.English;
        editor.SelectedAudioStreamIndex = project.PrimaryAsset.SourceMedia.AudioStreams.Last().Index;
        editor.Segments[0].Text = "Gandalf wins!";
        await editor.AlignCorrectedTextAsync(editor.Segments[0]); editor.SaveCommand.Execute(null);
        var asset = session.Current!.PrimaryAsset; var track = asset.Captions!; var aligned = track.Segments[0];
        TestAssert.True(aligned.Words.Count == 2 && aligned.Text == "Gandalf wins!", "Editorial filtering must preserve rendered captions and their aligned word clocks.");
        var untouched = new AudioTranscriptionSegment("untouched", track.NeighborhoodId, "Original stream words",
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), track.SourceWindowStart + TimeSpan.FromSeconds(4),
            track.SourceWindowStart + TimeSpan.FromSeconds(5));
        GenerationCandidateCaptionTrack WithSegments(params AudioTranscriptionSegment[] segments) => GenerationCandidateCaptionTrack.RestoreStudioHandoff(
            track.CandidateId, track.NeighborhoodId, track.SourceSelection, track.RequestedStyle, track.SourceWindowStart,
            track.SourceWindowDuration, track.SourceDuration, segments, track.IsUserEdited, track.SuppressionReason);
        var projected = RetainedCaptionEditorialTranscriptProjector.Project(WithSegments(aligned, untouched), asset.SourceStart, asset.SourceEnd);
        TestAssert.Equal("Original stream words", projected.Single().Text, "A foreign aligned stream cannot inherit the original creator-speech role, while unaffected segments remain.");
        TestAssert.Equal(track.SourceSelection.AbsoluteAudioStreamIndex, projected.Single().AbsoluteAudioStreamIndex, "Unaffected text must keep its inspected original stream.");
        var historical = new AudioTranscriptionSegment(aligned.Id, aligned.NeighborhoodId, "Entirely revised caption",
            aligned.RelativeStart, aligned.RelativeEnd, aligned.AbsoluteSourceStart, aligned.AbsoluteSourceEnd,
            warnings: aligned.Warnings);
        var historicalProjection = RetainedCaptionEditorialTranscriptProjector.Project(WithSegments(historical), asset.SourceStart, asset.SourceEnd);
        TestAssert.Equal("Entirely revised caption", historicalProjection.Single().Text, "An old alignment record with neither matching text nor matching current words cannot relabel the current user-authored caption.");
        var partial = new AudioTranscriptionSegment(aligned.Id, aligned.NeighborhoodId, aligned.Words[0].Text,
            aligned.Words[0].RelativeStart, aligned.Words[0].RelativeEnd, aligned.Words[0].AbsoluteSourceStart, aligned.Words[0].AbsoluteSourceEnd,
            [aligned.Words[0]], warnings: aligned.Warnings);
        TestAssert.Equal(0, RetainedCaptionEditorialTranscriptProjector.Project(WithSegments(partial), asset.SourceStart, asset.SourceEnd).Length,
            "Splitting a foreign-stream phrase must still recognize applicable measured words when the whole-phrase text hash changes.");
    }
}
