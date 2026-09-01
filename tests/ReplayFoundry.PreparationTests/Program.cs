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
            .. GenerationSetupDetectionSelectionTests.GetTests(),
            .. PreparedGenerationWorkflowTests.GetTests(),
            .. CompositionReviewPreviewTests.GetTests(),
            .. VideoPreviewFrameRequestTests.GetTests(),
            .. FfmpegPreviewCommandBuilderTests.GetTests(),
            .. FfmpegVideoPreviewFrameProviderTests.GetTests(),
            .. UiUxApplicationSurfaceTests.GetTests(),
            .. YouTubePublishingTests.GetTests(),
            .. ProductionHandoffLifecycleTests.GetTests(),
            .. OutputLocationAndLibraryTests.GetTests(),
            .. LocalDataChannelTests.GetTests(),
        ];

        return await TestRunner.RunAsync("Replay Foundry Preparation Tests", tests);
    }
}
