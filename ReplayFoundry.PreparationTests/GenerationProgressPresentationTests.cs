using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Progress;

namespace ReplayFoundry.PreparationTests;

internal static class GenerationProgressPresentationTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Progress presentations describe preparation without losing run identity",
            PreparationPresentationDescribesRun),
        new(
            "Progress presentations describe evidence as an indeterminate analysis",
            EvidencePresentationDescribesAnalysis),
        new(
            "Terminal progress presentations preserve their active run context",
            TerminalPresentationsPreserveRunContext),
        new(
            "Progress source text requires one complete source identity",
            SourceProgressRequiresCompleteIdentity),
        new(
            "Progress presentations reject invalid run inputs",
            RunningPresentationRejectsInvalidInputs),
    ];

    private static Task PreparationPresentationDescribesRun()
    {
        GenerationProgressPresentation presentation =
            GenerationProgressPresentationFactory.BeginPreparation(
                GenerationMode.Montage,
                3);

        TestAssert.Equal(
            GenerationProgressState.Running,
            presentation.State,
            "Preparation must begin in the running state.");
        TestAssert.Equal(
            "Montage",
            presentation.ModeDisplayName,
            "The presentation must retain the selected mode.");
        TestAssert.Equal(
            "3 source videos",
            presentation.SourceSummary,
            "The presentation must describe the complete source batch.");
        TestAssert.Equal(
            "Cancel Preparation",
            presentation.CancelButtonLabel,
            "Preparation must expose its stage-specific cancellation action.");
        TestAssert.False(
            presentation.IsIndeterminate,
            "Preparation reports real progress boundaries.");

        return Task.CompletedTask;
    }

    private static Task EvidencePresentationDescribesAnalysis()
    {
        GenerationProgressPresentation presentation =
            GenerationProgressPresentationFactory.BeginEvidenceAnalysis(
                GenerationMode.IndividualClips,
                1);

        TestAssert.Equal(
            "Individual Clips",
            presentation.ModeDisplayName,
            "Evidence progress must retain the selected mode.");
        TestAssert.Equal(
            "1 source video",
            presentation.SourceSummary,
            "Evidence progress must use singular source copy.");
        TestAssert.Equal(
            "Cancel Analysis",
            presentation.CancelButtonLabel,
            "Evidence analysis must expose its own cancellation action.");
        TestAssert.True(
            presentation.IsIndeterminate,
            "An active media pass must not claim synthetic percentage progress.");

        return Task.CompletedTask;
    }

    private static Task TerminalPresentationsPreserveRunContext()
    {
        GenerationProgressRunContext context =
            new(
                "Montage",
                "2 source videos",
                "Cancel Analysis");
        InvalidOperationException exception =
            new("technical detail");

        GenerationProgressPresentation failed =
            GenerationProgressPresentationFactory.Failure(
                "Evidence analysis stopped",
                "The current source could not be analyzed.",
                exception,
                context,
                42);
        GenerationProgressPresentation cancelled =
            GenerationProgressPresentationFactory.Cancelled(
                "Evidence analysis cancelled",
                "The retained inputs are still available.",
                "No partial evidence was saved.",
                context);

        foreach (GenerationProgressPresentation presentation in
                 new[] { failed, cancelled })
        {
            TestAssert.Equal(
                context.ModeDisplayName,
                presentation.ModeDisplayName,
                "Terminal progress must preserve the active mode.");
            TestAssert.Equal(
                context.SourceSummary,
                presentation.SourceSummary,
                "Terminal progress must preserve the active source summary.");
            TestAssert.Equal(
                context.CancelButtonLabel,
                presentation.CancelButtonLabel,
                "Terminal progress must preserve its run context without inventing a new stage.");
            TestAssert.False(
                presentation.IsIndeterminate,
                "A terminal presentation cannot remain indeterminate.");
        }

        TestAssert.Equal(
            42d,
            failed.ProgressPercent,
            "A determinate failure must retain its last real boundary.");
        TestAssert.True(
            failed.TechnicalDetails?.Contains(
                "technical detail",
                StringComparison.Ordinal) == true,
            "Failure diagnostics must retain the original exception.");

        return Task.CompletedTask;
    }

    private static Task SourceProgressRequiresCompleteIdentity()
    {
        TestAssert.Equal(
            "Video 2 of 4: session.mkv",
            GenerationProgressPresentationFactory.FormatSourceProgress(
                2,
                4,
                "session.mkv"),
            "A complete source identity must be formatted deterministically.");
        TestAssert.Null(
            GenerationProgressPresentationFactory.FormatSourceProgress(
                2,
                null,
                "session.mkv"),
            "A partial source identity must not create misleading progress copy.");
        TestAssert.Null(
            GenerationProgressPresentationFactory.FormatSourceProgress(
                2,
                4,
                "  "),
            "Blank source names must not be displayed.");

        return Task.CompletedTask;
    }

    private static Task RunningPresentationRejectsInvalidInputs()
    {
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => GenerationProgressPresentationFactory.BeginPreparation(
                (GenerationMode)int.MaxValue,
                1),
            "Undefined generation modes must be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => GenerationProgressPresentationFactory.BeginEvidenceAnalysis(
                GenerationMode.IndividualClips,
                0),
            "A progress run must contain a source.");

        return Task.CompletedTask;
    }
}
