using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Features.Studio.Inspector;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Features.Studio.Rendering;

namespace ReplayFoundry.Desktop.Features.Studio;

internal interface IStudioProjectSwitchContext
{
    bool IsDisposed { get; }
    IGenerationOutputSink? OutputSink { get; }
    IStudioProjectPersistenceCoordinator? ProjectPersistence { get; }
    GenerationOutputProject? CurrentProject { get; }
    StudioInspectorViewModel Inspector { get; }
    StudioFinalRenderViewModel FinalRender { get; }
    StudioHiddenMomentsViewModel HiddenMoments { get; }

    bool CanCommitPendingClipEdit();
    bool TryCommitPendingClipEdit();
    void CancelDelayedDraftSave();
    void RestoreDurableRecovery(StudioProjectRecoveryState recovery);
}

internal sealed class StudioProjectSwitchCoordinator
{
    private readonly IStudioProjectSwitchContext _context;

    public StudioProjectSwitchCoordinator(IStudioProjectSwitchContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<StudioProjectSwitchResult> TrySwitchAsync(
        GenerationOutputProject project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        StudioProjectSwitchResult? blocked = GetSwitchBlock(project);
        if (blocked is not null)
        {
            return blocked;
        }

        StudioPendingEditorialDraft? metadata =
            _context.Inspector.Editorial.CapturePendingDraft();
        StudioProjectSwitchResult? invalid = ValidatePendingEdits(metadata);
        if (invalid is not null || !CommitPendingEdits(metadata, out invalid))
        {
            return invalid!;
        }

        try
        {
            StudioProjectRecoveryState? recovery =
                await LoadRecoveryAsync(project, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _context.OutputSink!.Publish(project.ReopenAsDraft());
            if (recovery is not null)
            {
                _context.RestoreDurableRecovery(recovery);
            }
            return Result(
                StudioProjectSwitchOutcome.Switched,
                "Studio opened the selected recent project.");
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return Result(
                StudioProjectSwitchOutcome.Unavailable,
                "Studio could not open the selected project: " +
                exception.Message);
        }
    }

    private StudioProjectSwitchResult? GetSwitchBlock(
        GenerationOutputProject project)
    {
        if (_context.IsDisposed || _context.OutputSink is null)
        {
            return Result(
                StudioProjectSwitchOutcome.Unavailable,
                "Studio cannot open another project in this session.");
        }
        if (_context.CurrentProject?.Id.Equals(
                project.Id,
                StringComparison.Ordinal) == true &&
            _context.CurrentProject.IsFinalized == false)
        {
            return Result(
                StudioProjectSwitchOutcome.AlreadyOpen,
                "This Studio project is already open.");
        }
        if (_context.FinalRender.IsRendering)
        {
            return Result(
                StudioProjectSwitchOutcome.BlockedActiveRender,
                "Finish or cancel the active render before opening another Studio project.");
        }
        if (_context.CurrentProject?.IsFinalized == false &&
            _context.FinalRender.HasQueuedItems)
        {
            return Result(
                StudioProjectSwitchOutcome.BlockedUnsavedDraft,
                "Render the queued clips or remove them from the render queue before opening another Studio project.");
        }
        return _context.Inspector.Editorial.IsGenerating ||
               _context.HiddenMoments.HasUnfinishedQueueItems
            ? Result(
                StudioProjectSwitchOutcome.BlockedBusyOperation,
                "Finish or cancel the queued Studio additions before opening another project.")
            : null;
    }

    private StudioProjectSwitchResult? ValidatePendingEdits(
        StudioPendingEditorialDraft? metadata)
    {
        _context.CancelDelayedDraftSave();
        if (_context.Inspector.Caption.HasUnsavedChanges)
        {
            return Result(
                StudioProjectSwitchOutcome.BlockedUnsavedDraft,
                "Save the caption text and timing changes before opening another Studio project.");
        }
        if (_context.Inspector.Graphics.HasUnsavedChanges)
        {
            return Result(
                StudioProjectSwitchOutcome.BlockedUnsavedDraft,
                "Apply the graphic placement changes before opening another Studio project.");
        }
        if (_context.Inspector.Editorial.HasUnsavedProfileChanges)
        {
            return Result(
                StudioProjectSwitchOutcome.BlockedUnsavedDraft,
                "Save your reusable writing preferences before opening another Studio project.");
        }
        if (metadata is not null &&
            !_context.Inspector.Editorial.CanPersistPendingDraft(metadata))
        {
            return Result(
                StudioProjectSwitchOutcome.BlockedInvalidMetadata,
                "Fix the title and description before opening another Studio project.");
        }
        return _context.CanCommitPendingClipEdit()
            ? null
            : Result(
                StudioProjectSwitchOutcome.BlockedInvalidClipEdit,
                "Fix or reset the current clip boundaries before opening another Studio project.");
    }

    private bool CommitPendingEdits(
        StudioPendingEditorialDraft? metadata,
        out StudioProjectSwitchResult? failure)
    {
        if (!_context.TryCommitPendingClipEdit())
        {
            failure = Result(
                StudioProjectSwitchOutcome.BlockedInvalidClipEdit,
                "Replay Foundry could not save the current clip edit, so the project stayed open.");
            return false;
        }
        if (metadata is not null &&
            !_context.Inspector.Editorial.TryPersistPendingDraft(metadata))
        {
            failure = Result(
                StudioProjectSwitchOutcome.BlockedInvalidMetadata,
                "Replay Foundry could not save the current title and description, so the project stayed open.");
            return false;
        }
        failure = null;
        return true;
    }

    private async Task<StudioProjectRecoveryState?> LoadRecoveryAsync(
        GenerationOutputProject project,
        CancellationToken cancellationToken)
    {
        if (_context.ProjectPersistence is null)
        {
            return null;
        }
        await _context.ProjectPersistence.FlushAsync(cancellationToken);
        return await _context.ProjectPersistence.GetRecoveryAsync(
            project.Id,
            cancellationToken);
    }

    private static StudioProjectSwitchResult Result(
        StudioProjectSwitchOutcome outcome,
        string message) => new(outcome, message);

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or
            ArgumentException;
}
