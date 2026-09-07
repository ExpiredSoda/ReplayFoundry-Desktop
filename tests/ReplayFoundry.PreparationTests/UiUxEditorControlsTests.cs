using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task ManualRangeEditsAreReversible()
    {
        RunOnSta(() =>
        {
            string path = Path.GetTempFileName();
            try
            {
                var session = new GenerationOutputSession(); session.Publish(ManualWorkspaceProject(path));
                using var model = new StudioManualClipViewModel(session, new ManualWorkspacePreview(path), () => true);
                model.Bind(session.Current);
                model.BeginRangeGesture();
                model.MoveRange(30); model.MoveRange(70); model.MoveRange(90);
                model.EndRangeGesture(false);
                TestAssert.Equal(120d, model.SelectionEndSeconds, "Moving the body preserves its thirty-second duration.");
                model.UndoRangeCommand.Execute(null);
                TestAssert.Equal(0d, model.SelectionStartSeconds, "One undo restores the entire gesture.");
                TestAssert.False(model.UndoRangeCommand.CanExecute(null), "Intermediate pointer positions are not undo steps.");
                model.RedoRangeCommand.Execute(null);
                TestAssert.Equal(90d, model.SelectionStartSeconds, "Redo restores the completed move.");
                model.BeginRangeGesture(); model.SelectionStartSeconds = 95; model.EndRangeGesture(true);
                TestAssert.Equal(90d, model.SelectionStartSeconds, "Escape restores an unfinished edge trim.");
                model.MoveRange(9999);
                TestAssert.Equal(1170d, model.SelectionStartSeconds, "The range stops at the end without shortening.");
                TestAssert.Equal(1200d, model.SelectionEndSeconds, "The range cannot leave the recording.");
                model.MoveRange(-100);
                TestAssert.Equal(0d, model.SelectionStartSeconds, "The range stops at the beginning.");
                TestAssert.Equal(30d, model.SelectionEndSeconds, "Clamping retains the full range.");
                model.UndoRangeCommand.Execute(null); model.MoveRange(60);
                TestAssert.False(model.RedoRangeCommand.CanExecute(null), "A new edit invalidates redo history.");
                model.StartText = "285"; model.EndText = "293";
                model.UndoRangeCommand.Execute(null);
                TestAssert.Equal(60d, model.SelectionStartSeconds, "Typing both times remembers the last valid range across an invalid intermediate value.");
                TestAssert.Equal(90d, model.SelectionEndSeconds, "Undo cannot restore a temporarily invalid range.");
            }
            finally { File.Delete(path); }
        });
        return Task.CompletedTask;
    }

    private static Task ManualShortcutsUseSourceFrames()
    {
        RunOnSta(() =>
        {
            string path = Path.GetTempFileName();
            try
            {
                using var model = new StudioManualClipViewModel(null, new ManualWorkspacePreview(path), () => true);
                model.Bind(ManualWorkspaceProject(path, new(60, 1)));
                model.SourcePositionSeconds = 120;
                StudioManualKeyboard.Handle(model, Key.Right, ModifierKeys.None);
                TestAssert.True(Math.Abs(model.SourcePositionSeconds - (120 + 1d / 60)) < 0.000001, "Right advances one source frame.");
                StudioManualKeyboard.Handle(model, Key.Left, ModifierKeys.Shift);
                TestAssert.True(Math.Abs(model.SourcePositionSeconds - 119.85) < 0.000001, "Shift steps ten frames.");
                StudioManualKeyboard.Handle(model, Key.I, ModifierKeys.None);
                model.SourcePositionSeconds = 130;
                StudioManualKeyboard.Handle(model, Key.O, ModifierKeys.None);
                double duration = model.SelectionEndSeconds - model.SelectionStartSeconds;
                StudioManualKeyboard.Handle(model, Key.Right, ModifierKeys.Alt | ModifierKeys.Shift);
                TestAssert.True(Math.Abs(model.SelectionEndSeconds - model.SelectionStartSeconds - duration) < 0.001, "Keyboard range movement retains duration.");
                StudioManualKeyboard.Handle(model, Key.Z, ModifierKeys.Control);
                TestAssert.Equal(119.85, model.SelectionStartSeconds, "Keyboard undo restores the marked In.");
                TestAssert.False(StudioManualKeyboard.Handle(model, Key.S, ModifierKeys.Control), "Unrelated shortcuts remain available to the host.");
            }
            finally { File.Delete(path); }
        });
        return Task.CompletedTask;
    }

    private static Task EffectComparisonPreservesDraft()
    {
        string path = Path.GetTempFileName();
        try
        {
            var session = new GenerationOutputSession(); session.Publish(ManualWorkspaceProject(path));
            var editor = new StudioClipEditorViewModel(session); editor.Bind(session.Current, session.Current!.PrimaryAsset);
            editor.SelectedCaptionStyle = editor.CaptionStyleOptions.Single(o => o.Value == GenerationCaptionStylePreset.WordFocus);
            editor.CaptionFontScalePercent = 120;
            editor.SelectedVideoEffect = editor.VideoEffectOptions.Single(o => o.Value == StudioVideoEffectPreset.Noir);
            editor.VideoEffectIntensityPercent = 35;
            editor.Effects.ShowOriginal = true;
            TestAssert.Equal(StudioVideoEffectPreset.None, editor.Effects.PreviewAppearance.VideoEffect, "Comparison bypasses color in the preview.");
            TestAssert.Equal(StudioVideoEffectPreset.Noir, editor.DraftAppearance.VideoEffect, "Comparison must not mutate the effect draft.");
            editor.ApplyBoundaryEditCommand.Execute(null);
            TestAssert.Equal(StudioVideoEffectPreset.Noir, session.Current.PrimaryAsset.Appearance.VideoEffect, "Saving during comparison keeps the chosen look.");
            TestAssert.Equal(GenerationCaptionStylePreset.WordFocus, session.Current.PrimaryAsset.Appearance.CaptionStyle,
                "An uncaptioned clip retains its chosen look for later caption creation.");
            editor.Effects.ResetCommand.Execute(null);
            TestAssert.Equal(StudioVideoEffectPreset.None, editor.DraftAppearance.VideoEffect, "Reset removes the color effect.");
            TestAssert.Equal(120d, editor.DraftAppearance.CaptionFontScalePercent, "Reset leaves independent caption styling intact.");
            TestAssert.False(editor.Effects.CanAdjust, "An absent effect has no adjustable intensity.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }

    private static Task EffectComparisonChangesPreviewMedia()
    {
        RunOnSta(() =>
        {
            string path = Path.GetTempFileName();
            try
            {
                var session = new GenerationOutputSession(); session.Publish(ManualWorkspaceProject(path));
                var media = new ManualWorkspacePreview(path);
                using var studio = new StudioViewModel(session, session, new RecordingStudioClipRenderer(), new ManualClipWriter(),
                    new ClipEditorialProfileSession(), media);
                studio.Inspector.SelectedInspector = StudioInspectorSection.Effects;
                var editor = studio.Inspector.Clip;
                editor.SelectedVideoEffect = editor.VideoEffectOptions.Single(o => o.Value == StudioVideoEffectPreset.Noir);
                editor.VideoEffectIntensityPercent = 35;
                editor.ApplyBoundaryEditCommand.Execute(null);
                TestAssert.Equal(StudioVideoEffectPreset.Noir, media.Last!.Asset.Appearance.VideoEffect, "The normal preview renders the saved effect.");
                studio.Preview.PreviewPositionSeconds = 20;
                editor.Effects.ShowOriginal = true;
                TestAssert.Equal(StudioVideoEffectPreset.None, media.Last.Asset.Appearance.VideoEffect, "Comparison requests original video, not just a caption overlay refresh.");
                TestAssert.Equal(20d, studio.Preview.PreviewPositionSeconds, "Comparison stays at the same source frame.");
                editor.VideoEffectIntensityPercent = 60;
                editor.ApplyBoundaryEditCommand.Execute(null);
                TestAssert.True(editor.Effects.ShowOriginal, "Saving a same-clip edit retains comparison mode.");
                TestAssert.Equal(StudioVideoEffectPreset.None, media.Last.Asset.Appearance.VideoEffect, "Autosave must not restore the effect in the comparison preview.");
                TestAssert.Equal(60d, session.Current!.PrimaryAsset.Appearance.VideoEffectIntensityPercent, "The saved export retains the newly chosen intensity.");
                editor.Effects.ShowOriginal = false;
                TestAssert.Equal(StudioVideoEffectPreset.Noir, media.Last.Asset.Appearance.VideoEffect, "Ending comparison restores the effect video.");
                TestAssert.Equal(60d, media.Last.Asset.Appearance.VideoEffectIntensityPercent, "The restored preview uses the latest saved strength.");
                editor.Effects.ShowOriginal = true;
                studio.Inspector.SelectedInspector = StudioInspectorSection.Metadata;
                TestAssert.False(editor.Effects.ShowOriginal, "Leaving Effects ends the temporary comparison.");
                TestAssert.Equal(StudioVideoEffectPreset.Noir, media.Last.Asset.Appearance.VideoEffect, "Other editing tools show the saved look.");
                studio.Inspector.SelectedInspector = StudioInspectorSection.Effects;
                editor.Effects.ShowOriginal = true;
                editor.SelectedVideoEffect = editor.VideoEffectOptions.Single(o => o.Value == StudioVideoEffectPreset.None);
                TestAssert.False(editor.Effects.ShowOriginal, "Removing the effect cannot leave a disabled comparison checked.");
                editor.ApplyBoundaryEditCommand.Execute(null);
                TestAssert.Equal(StudioVideoEffectPreset.None, media.Last.Asset.Appearance.VideoEffect, "Removing the look restores the original preview.");
            }
            finally { File.Delete(path); }
        });
        return Task.CompletedTask;
    }

    private static Task CustomClipsStartWriting()
    {
        RunOnSta(() =>
        {
            string path = Path.GetTempFileName();
            try
            {
                var session = new GenerationOutputSession(); session.Publish(ManualWorkspaceProject(path));
                var writer = new ManualClipWriter();
                using var studio = new StudioViewModel(session, session, new RecordingStudioClipRenderer(), writer,
                    new ClipEditorialProfileSession(), new ManualWorkspacePreview(path), editorialRerollPreference: new ManualWritingPreference());
                studio.ManualClips.StartText = "120"; studio.ManualClips.EndText = "145";
                studio.ManualClips.AddCommand.Execute(null);
                TestAssert.Equal(StudioInspectorSection.Metadata, studio.Inspector.SelectedInspector, "The new clip opens its writing controls.");
                TestAssert.True(writer.Request is not null, "Creating a custom clip starts the configured writing service.");
                TestAssert.Equal("Writing…", studio.Inspector.Editorial.DraftState, "The initial draft must show writing, not Ready.");
                TestAssert.False(studio.Inspector.Editorial.NeedsCurrentCutRefresh, "A new placeholder is not stale wording to repair.");
                TestAssert.Equal(TimeSpan.FromSeconds(120), writer.Request!.Context.SourceStart, "Writing uses the custom In, not the previous candidate.");
                TestAssert.Equal(TimeSpan.FromSeconds(145), writer.Request.Context.SourceEnd, "Writing uses the custom Out.");
                writer.Complete();
                TestAssert.Equal(1, session.Current!.Assets.Last().EditorialMetadata!.Attempt, "The new draft replaces the placeholder.");
                var restored = StudioProjectDocumentMapper.Restore(StudioProjectDocumentMapper.Capture(session.Current, 1, DateTimeOffset.UtcNow));
                TestAssert.Equal(session.Current.Assets.Last().EditorialMetadata!.Title, restored.Assets.Last().EditorialMetadata!.Title, "Custom writing survives reopening.");
                TestAssert.Equal(session.Current.Assets.Last().EditorialMetadata!.Description, restored.Assets.Last().EditorialMetadata!.Description,
                    "The description survives reopening too.");
            }
            finally { File.Delete(path); }
        });
        return Task.CompletedTask;
    }

    private sealed class ManualClipWriter : IClipEditorialMetadataGenerationService
    {
        private readonly ClipEditorialMetadataGenerationService _inner = new(new HeuristicClipEditorialMetadataGenerator());
        private readonly TaskCompletionSource<ClipEditorialMetadataDraft> _pending = new();
        public ClipEditorialMetadataRequest? Request { get; private set; }
        public bool IsAiAvailable => false;
        public Task<ClipEditorialMetadataDraft> GenerateAsync(ClipEditorialMetadataRequest request, CancellationToken cancellationToken)
        { Request = request; return _pending.Task; }
        public void Complete() => _pending.SetResult(_inner.GenerateAsync(Request!, CancellationToken.None).GetAwaiter().GetResult());
    }
    private sealed class ManualWritingPreference : IEditorialRerollPreference
    {
        public bool UseLocalAi => false;
        public bool IsPersistent => false;
        public event EventHandler? Changed { add { } remove { } }
    }
}
