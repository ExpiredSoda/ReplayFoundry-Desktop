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

public sealed class GenerationOutputSession :
    IGenerationOutputSink,
    IGenerationOutputSession,
    IGenerationOutputSessionMaintenance,
    IGenerationRenderedOutputSink,
    IGenerationRenderedOutputSession,
    IGenerationOutputEditor
{
    public GenerationOutputProject? Current { get; private set; }

    public event EventHandler<GenerationOutputChangedEventArgs>?
        CurrentChanged;

    public event EventHandler<GenerationRenderedOutputEventArgs>?
        RenderedOutputCommitted;

    public void Publish(GenerationOutputProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        Current = project;
        CurrentChanged?.Invoke(
            this,
            new GenerationOutputChangedEventArgs(Current));
    }

    public void Clear()
    {
        if (Current is null)
        {
            return;
        }

        Current = null;
        CurrentChanged?.Invoke(
            this,
            new GenerationOutputChangedEventArgs(null));
    }

    public void ReplaceAsset(
        string projectId,
        GenerationOutputAsset replacement)
    {
        if (Current is null ||
            string.IsNullOrWhiteSpace(projectId) ||
            !Current.Id.Equals(
                projectId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Studio edit does not belong to the current generated project.");
        }

        Current = Current.ReplaceAsset(replacement);
        CurrentChanged?.Invoke(
            this,
            new GenerationOutputChangedEventArgs(Current));
    }

    public void ReplaceAssets(
        string projectId,
        IReadOnlyList<GenerationOutputAsset> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        if (Current is null ||
            string.IsNullOrWhiteSpace(projectId) ||
            !Current.Id.Equals(projectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Studio edits do not belong to the current generated project.");
        }

        Current = Current.ReplaceAssets(replacements);
        CurrentChanged?.Invoke(
            this,
            new GenerationOutputChangedEventArgs(Current));
    }

    public void FinalizeProject(
        GenerationOutputProject finalizedProject)
    {
        ArgumentNullException.ThrowIfNull(finalizedProject);
        if (Current is null ||
            Current.IsFinalized ||
            !finalizedProject.IsFinalized ||
            !Current.Id.Equals(
                finalizedProject.Id,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Only the current draft Studio project can be finalized.");
        }

        GenerationOutputProject draft = Current;
        Current = finalizedProject;
        try
        {
            CurrentChanged?.Invoke(
                this,
                new GenerationOutputChangedEventArgs(Current));
        }
        catch
        {
            Current = draft;
            try
            {
                CurrentChanged?.Invoke(
                    this,
                    new GenerationOutputChangedEventArgs(Current));
            }
            catch
            {
                // Preserve the original commit failure after best-effort
                // projection rollback.
            }
            throw;
        }
    }

    public void CommitRenderedOutput(
        GenerationOutputProject renderedProject)
    {
        ArgumentNullException.ThrowIfNull(renderedProject);
        if (!renderedProject.IsFinalized ||
            renderedProject.IncludedAssets.Count == 0 ||
            renderedProject.IncludedAssets.Any(static asset =>
                !asset.IsRendered))
        {
            throw new InvalidOperationException(
                "Only a complete finalized render can be committed to Library.");
        }

        RenderedOutputCommitted?.Invoke(
            this,
            new GenerationRenderedOutputEventArgs(renderedProject));
    }

    public void AcceptHiddenMoment(
        string projectId,
        string hiddenMomentId,
        GenerationCandidateCaptionTrack? captions = null,
        ClipEditorialContext? editorialContext = null,
        ClipEditorialMetadataDraft? editorialMetadata = null)
    {
        if (Current is null ||
            string.IsNullOrWhiteSpace(projectId) ||
            !Current.Id.Equals(projectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Hidden Moments decision does not belong to the current Studio project.");
        }

        Current = Current.AcceptHiddenMoment(
            hiddenMomentId,
            captions,
            editorialContext,
            editorialMetadata);
        CurrentChanged?.Invoke(
            this,
            new GenerationOutputChangedEventArgs(Current));
    }
}
