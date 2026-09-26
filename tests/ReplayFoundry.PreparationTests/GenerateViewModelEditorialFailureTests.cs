using ReplayFoundry.Desktop.Features.Generate.Editorial;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerateViewModelWorkflowTests
{
    private static async Task EditorialRejectionExplainsFailure()
    {
        const string explanation = "AI could not produce supported wording for this clip. Try another cut.";
        foreach (var kind in new[] { ClipEditorialAiFailureKind.CaseRejected, ClipEditorialAiFailureKind.ProviderFailed })
        {
            ViewModelContext context = CreateContext();
            context.Dialog.Result = PreparedGenerationWorkflowTests.CreateOptions();
            context.Runner.Failure = new ClipEditorialAiGenerationException(kind, explanation, "test-candidate");
            await context.ViewModel.ContinueToGenerationSetupAsync();
            TestAssert.Equal(kind == ClipEditorialAiFailureKind.CaseRejected ? explanation :
                "Replay Foundry could not finish generating your clips.", context.ViewModel.GenerationProgress.ErrorMessage!,
                "Only the known per-clip rejection should become the primary user explanation.");
            TestAssert.True(context.ViewModel.SelectedSources.Count > 0,
                "Explaining a writing rejection must preserve the recordings for retry.");
        }
    }
}
