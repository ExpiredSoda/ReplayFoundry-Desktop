namespace ReplayFoundry.PreparationTests;

internal static class Program
{
    private static async Task<int> Main(
        string[] arguments)
    {
        if (WindowsProcessRunnerTests.IsTestHostRequest(
                arguments))
        {
            return await WindowsProcessRunnerTests.RunTestHostAsync(
                arguments);
        }

        IReadOnlyList<TestCase> tests =
        [
            .. WindowsProcessRunnerTests.GetTests(),
            .. GenerationSourcePreparationRequestTests.GetTests(),
            .. GenerationSourcePreparationServiceTests.GetTests(),
            .. AsyncDelegateCommandTests.GetTests(),
            .. GenerationSourceFreshnessTests.GetTests(),
            .. GenerationSourcePreparationCoordinatorTests.GetTests(),
            .. GenerationEvidenceAnalysisTests.GetTests(),
            .. GenerationMomentFindingTests.GetTests(),
            .. GenerationMomentGuidanceTests.GetTests(),
            .. GenerateUsabilityTests.GetTests(),
            .. GenerationSpeechActivityTests.GetTests(),
            .. ClipPreferenceTests.GetTests(),
            .. EditorialMetadataPreferenceLearningTests.GetTests(),
            .. EditorialRerollPreferenceTests.GetTests(),
            .. EditorialRerollDiversityTests.GetTests(),
            .. StudioCreativePackTests.GetTests(),
            .. EditorialMetadataTests.GetTests(),
            .. HeuristicEditorialMetadataTests.GetTests(),
            .. GameKnowledgeTests.GetTests(),
            .. VisualTextTests.GetTests(),
            .. GenerationClipRenderingTests.GetTests(),
            .. StudioProjectPersistenceTests.GetTests(),
            .. GenerateWorkflowStateOwnerTests.GetTests(),
            .. GenerationProgressPresentationTests.GetTests(),
            .. GenerateViewModelWorkflowTests.GetTests(),
            .. PreparedGenerationWorkflowTests.GetTests(),
            .. CompositionReviewPreviewTests.GetTests(),
            .. VideoPreviewFrameRequestTests.GetTests(),
            .. FfmpegPreviewCommandBuilderTests.GetTests(),
            .. FfmpegVideoPreviewFrameProviderTests.GetTests(),
            .. UiUxApplicationSurfaceTests.GetTests(),
            .. YouTubePublishingTests.GetTests(),
            .. ProductionHandoffLifecycleTests.GetTests(),
            .. OutputLocationAndLibraryTests.GetTests(),
        ];

        int passed = 0;
        int failed = 0;

        Console.WriteLine(
            "Replay Foundry Preparation Tests");

        Console.WriteLine(
            new string('=', 60));

        foreach (TestCase test in tests)
        {
            try
            {
                await test.ExecuteAsync();
                passed++;

                Console.WriteLine(
                    $"PASS  {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;

                Console.WriteLine(
                    $"FAIL  {test.Name}");

                Console.WriteLine(
                    $"      {exception.Message}");

                Console.WriteLine();
                Console.WriteLine(exception);
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            new string('=', 60));

        Console.WriteLine(
            $"Passed: {passed}");

        Console.WriteLine(
            $"Failed: {failed}");

        Console.WriteLine(
            $"Total:  {tests.Count}");

        return failed == 0
            ? 0
            : 1;
    }
}
