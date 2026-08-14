using ReplayFoundry.Desktop.Features.Generate.Handoff;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioProjectRenderProgress
{
    public StudioProjectRenderProgress(
        string title,
        string detail,
        int completedOutputs,
        int totalOutputs)
    {
        if (string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(detail) ||
            totalOutputs <= 0 ||
            completedOutputs < 0 ||
            completedOutputs > totalOutputs)
        {
            throw new ArgumentException(
                "Studio final-render progress requires text and a valid completed-output boundary.");
        }

        Title = title.Trim();
        Detail = detail.Trim();
        CompletedOutputs = completedOutputs;
        TotalOutputs = totalOutputs;
    }

    public string Title { get; }
    public string Detail { get; }
    public int CompletedOutputs { get; }
    public int TotalOutputs { get; }
    public double Percentage =>
        CompletedOutputs * 100d / TotalOutputs;
}

public sealed class StudioProjectRenderResult
{
    public StudioProjectRenderResult(
        GenerationOutputProject draft,
        GenerationOutputProject finalizedProject,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(finalizedProject);
        if (draft.IsFinalized ||
            !finalizedProject.IsFinalized ||
            !draft.Id.Equals(
                finalizedProject.Id,
                StringComparison.Ordinal) ||
            elapsed < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Studio render result must finalize the supplied draft project.",
                nameof(finalizedProject));
        }

        Draft = draft;
        FinalizedProject = finalizedProject;
        Elapsed = elapsed;
    }

    public GenerationOutputProject Draft { get; }
    public GenerationOutputProject FinalizedProject { get; }
    public TimeSpan Elapsed { get; }
}

public interface IStudioProjectRenderingService
{
    // A successful result remains owned by the renderer until exactly one
    // terminal accept or discard operation releases it.
    Task<StudioProjectRenderResult> FinalizeAsync(
        GenerationOutputProject draft,
        IProgress<StudioProjectRenderProgress> progress,
        CancellationToken cancellationToken);

    void AcceptCompletedRender(StudioProjectRenderResult result);

    void DiscardCompletedRender(StudioProjectRenderResult result);
}
