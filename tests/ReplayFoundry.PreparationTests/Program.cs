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
            .. ApplicationUpdateTests.GetTests(),
            .. GenerationSourceFreshnessTests.GetTests(),
            .. GenerationSourcePreparationCoordinatorTests.GetTests(),
            .. GenerationEvidenceAnalysisTests.GetTests(),
            .. GenerationMomentFindingTests.GetTests(),
            .. GenerationMomentGuidanceTests.GetTests(),
            .. GenerateUsabilityTests.GetTests(),
            .. GenerationSpeechActivityTests.GetTests(),
            .. ClipPreferenceTests.GetTests(),
#if !REPLAYFOUNDRY_PUBLIC_SOURCE
            .. TasteLearningTests.GetTests(),
            .. TasteStorageTests.GetTests(),
#endif
            .. TasteIntegrationTests.GetTests(),
            .. ReportConnectionTests.GetTests(),
            .. EditorialMetadataPreferenceLearningTests.GetTests(),
            .. EditorialWriterLearningTests.GetTests(),
            .. EditorialRerollPreferenceTests.GetTests(),
            .. EditorialRerollDiversityTests.GetTests(),
            .. StudioCreativePackTests.GetTests(),
            .. EditorialMetadataTests.GetTests(),
            .. EditorialPackagingTests.GetTests(),
            .. HeuristicEditorialMetadataTests.GetTests(),
            .. GameKnowledgeTests.GetTests(),
            .. VisualTextTests.GetTests(),
            .. QwenModelLoadDiagnosticsTests.GetTests(),
            .. QwenEditorialPassDiagnosticsTests.GetTests(),
            .. MediaWorkBudgetTests.GetTests(),
            .. StudioSourceCropTrackingIntegrationTests.GetTests(),
            .. CaptionForcedAlignmentTests.GetTests(),
            .. StudioCaptionLineLayoutTests.GetTests(),
            .. StudioSpeechPhrasingTests.GetTests(),
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
            .. YouTubeAnalyticsTests.GetTests(),
            .. ProductionHandoffLifecycleTests.GetTests(),
            .. OutputLocationAndLibraryTests.GetTests(),
            .. LocalDataChannelTests.GetTests(),
        ];

        return await TestRunner.RunAsync("Replay Foundry Preparation Tests", tests);
    }
}
