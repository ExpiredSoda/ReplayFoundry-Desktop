namespace ReplayFoundry.Desktop.Features.Generate.Progress;

internal enum GenerationFailurePresentation
{
    Preparation,
    Evidence,
    Generation,
}

internal static class GenerationFailurePresenter
{
    public static void Present(
        GenerationProgressViewModel progress,
        GenerationFailurePresentation presentation,
        string friendlyMessage,
        Exception exception,
        ReplayFoundry.Desktop.Features.Generate.ModeSelection.GenerationMode mode,
        int sourceCount,
        bool initializeProgress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(exception);

        switch (presentation)
        {
            case GenerationFailurePresentation.Preparation:
                if (initializeProgress)
                {
                    progress.BeginPreparation(
                        mode,
                        sourceCount);
                }

                progress.FailPreparation(
                    friendlyMessage,
                    exception);
                return;

            case GenerationFailurePresentation.Evidence:
                if (initializeProgress)
                {
                    progress.BeginEvidenceAnalysis(
                        mode,
                        sourceCount);
                }

                progress.FailEvidenceAnalysis(
                    friendlyMessage,
                    exception);
                return;

            case GenerationFailurePresentation.Generation:
                progress.Fail(
                    friendlyMessage,
                    exception);
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(presentation),
                    presentation,
                    "The failure presentation is not defined.");
        }
    }
}
