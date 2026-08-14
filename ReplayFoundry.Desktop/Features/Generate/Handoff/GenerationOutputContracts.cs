using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;

namespace ReplayFoundry.Desktop.Features.Generate.Handoff;

public sealed class GenerationOutputChangedEventArgs : EventArgs
{
    public GenerationOutputChangedEventArgs(
        GenerationOutputProject? current)
    {
        Current = current;
    }

    public GenerationOutputProject? Current { get; }
}

public interface IGenerationOutputSink
{
    void Publish(GenerationOutputProject project);
}

public interface IGenerationOutputSession
{
    GenerationOutputProject? Current { get; }

    event EventHandler<GenerationOutputChangedEventArgs>? CurrentChanged;
}

public interface IGenerationOutputSessionMaintenance
{
    void Clear();
}

public sealed class GenerationRenderedOutputEventArgs : EventArgs
{
    public GenerationRenderedOutputEventArgs(
        GenerationOutputProject renderedProject)
    {
        RenderedProject = renderedProject ??
            throw new ArgumentNullException(nameof(renderedProject));
    }

    public GenerationOutputProject RenderedProject { get; }
}

public interface IGenerationRenderedOutputSink
{
    void CommitRenderedOutput(GenerationOutputProject renderedProject);
}

public interface IGenerationRenderedOutputSession
{
    event EventHandler<GenerationRenderedOutputEventArgs>?
        RenderedOutputCommitted;
}

public enum StudioProjectSwitchOutcome
{
    Switched,
    AlreadyOpen,
    BlockedActiveRender,
    BlockedBusyOperation,
    BlockedUnsavedDraft,
    BlockedInvalidClipEdit,
    BlockedInvalidMetadata,
    Unavailable,
}

public sealed record StudioProjectSwitchResult
{
    public StudioProjectSwitchResult(
        StudioProjectSwitchOutcome outcome,
        string message)
    {
        if (!Enum.IsDefined(outcome) || string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A Studio project-switch result requires a defined outcome and message.");
        }

        Outcome = outcome;
        Message = message.Trim();
    }

    public StudioProjectSwitchOutcome Outcome { get; }
    public string Message { get; }
    public bool Succeeded => Outcome is
        StudioProjectSwitchOutcome.Switched or
        StudioProjectSwitchOutcome.AlreadyOpen;
}

public interface IStudioProjectSwitchService
{
    StudioProjectSwitchResult TrySwitchProject(
        GenerationOutputProject project);
}

public interface IGenerationOutputEditor
{
    void ReplaceAsset(
        string projectId,
        GenerationOutputAsset replacement);

    void ReplaceAssets(
        string projectId,
        IReadOnlyList<GenerationOutputAsset> replacements);

    void FinalizeProject(
        GenerationOutputProject finalizedProject);

    void AcceptHiddenMoment(
        string projectId,
        string hiddenMomentId,
        GenerationCandidateCaptionTrack? captions = null,
        ClipEditorialContext? editorialContext = null,
        ClipEditorialMetadataDraft? editorialMetadata = null);
}
