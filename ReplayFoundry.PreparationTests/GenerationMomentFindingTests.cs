using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.PreparationTests;

internal static class GenerationMomentFindingTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Generation moment settings map mode emphasis count and threshold", SettingsMapSetup),
        new("Generation setup supports exact 1-30 and quality-filtered Auto counts", SetupSupportsExactAndAutoCounts),
        new("Generation scan-depth profiles change deterministic evidence work", ScanDepthProfilesAreDistinct),
        new("Generation moment request rejects unsupported detection", RequestRejectsUnsupportedDetection),
        new("Generation moment request rejects foreign option mapping", RequestRejectsForeignMapping),
        new("Generation moment service calls finder once per source sequentially", ServiceCallsOncePerSource),
        new("Generation moment batch preserves source and explicit reference identity", BatchPreservesIdentity),
        new("Generation moment desired count is global", DesiredCountIsGlobal),
        new("Generation moment fill-count policy returns exactly five and ten clips", FillCountReturnsRequestedAmounts),
        new("Generation moment fill-count policy labels below-target clips", FillCountUsesBelowTargetCandidates),
        new("Generation moment fill-count policy relaxes diversity only as a last resort", FillCountRelaxesDiversityLast),
        new("Generation moment quality-first policy preserves a shortfall", QualityFirstPreservesShortfall),
        new("Generation moment Auto returns all quality matches within the cap", AutomaticCountReturnsQualityMatches),
        new("Generation moment selection does not impose per-source quotas", NoPerSourceQuota),
        new("Generation moments from different sources do not suppress each other", DifferentSourcesDoNotSuppress),
        new("Generation moment global rank is deterministic", GlobalRankIsDeterministic),
        new("Generation moment cancellation stops before later sources", CancellationStopsLaterSources),
        new("Generation source moment result rejects a foreign source", SourceResultRejectsForeign),
        new("Generation moment collections are immutable", CollectionsAreImmutable),
        new("Hidden Moments retains safe proposals disjoint from selected clips", HiddenMomentsRetainSafeAlternates),
        new("Hidden Moments carries the selected analysis tier into accepted review", HiddenMomentsCarryEditorialTier),
        new("Hidden Moments acceptance adds one editable Studio asset", HiddenMomentAcceptanceAddsAsset),
    ];

    private static Task SettingsMapSetup()
    {
        GenerationSetupOptions setup =
            CreateSetup(
                GenerationMode.Montage,
                ContentEmphasis.CommentaryFocused,
                7,
                63);
        GenerationMomentFindingSettings settings =
            GenerationMomentFindingSettings.FromSetup(setup);

        TestAssert.Equal(
            MomentOutputKind.MontageSegment,
            settings.Options.OutputKind,
            "Mode mapping.");
        TestAssert.Equal(
            MomentContentEmphasis.CommentaryFocused,
            settings.Options.ContentEmphasis,
            "Emphasis mapping.");
        TestAssert.Equal(7, settings.Options.DesiredCandidateCount, "Count mapping.");
        TestAssert.Equal(63d, settings.Options.MinimumHeuristicScore, "Threshold mapping.");

        return Task.CompletedTask;
    }

    private static Task SetupSupportsExactAndAutoCounts()
    {
        GenerationSetupOptions one = CreateSetup(
            GenerationMode.IndividualClips,
            ContentEmphasis.Balanced,
            1,
            0);
        GenerationSetupOptions thirty = CreateSetup(
            GenerationMode.IndividualClips,
            ContentEmphasis.Balanced,
            30,
            100,
            ClipFulfillmentPreference.QualityFirst);
        var automatic = new GenerationSetupOptions(
            GenerationMode.IndividualClips,
            DetectionMethod.Heuristics,
            AudioSelectionMode.Auto,
            30,
            70,
            ContentEmphasis.Balanced,
            ClipFulfillmentPreference.QualityFirst,
            resultCountMode: GenerationResultCountMode.Auto);

        TestAssert.Equal(1, one.DesiredResultCount, "One-clip request.");
        TestAssert.Equal(30, thirty.DesiredResultCount, "Thirty-clip cap.");
        TestAssert.True(
            automatic.IsAutomaticResultCount,
            "Auto amount must remain explicit in the immutable setup.");
        TestAssert.True(
            automatic.ResultCountLabel.Contains(
                "up to 30",
                StringComparison.Ordinal),
            "Auto must disclose its safety cap.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => CreateSetup(
                GenerationMode.IndividualClips,
                ContentEmphasis.Balanced,
                31,
                70),
            "Exact count must reject values above 30.");
        TestAssert.Throws<ArgumentException>(
            () => new GenerationSetupOptions(
                GenerationMode.IndividualClips,
                DetectionMethod.Heuristics,
                AudioSelectionMode.Auto,
                10,
                70,
                ContentEmphasis.Balanced,
                ClipFulfillmentPreference.QualityFirst,
                resultCountMode: GenerationResultCountMode.Auto),
            "Auto must always use the fixed 30-clip cap.");
        return Task.CompletedTask;
    }

    private static Task ScanDepthProfilesAreDistinct()
    {
        GenerationEvidenceAnalysisSettings fast =
            GenerationEvidenceAnalysisSettings.CreateForDepth(
                GenerationAnalysisDepth.Fast);
        GenerationEvidenceAnalysisSettings balanced =
            GenerationEvidenceAnalysisSettings.CreateForDepth(
                GenerationAnalysisDepth.Balanced);
        GenerationEvidenceAnalysisSettings thorough =
            GenerationEvidenceAnalysisSettings.CreateForDepth(
                GenerationAnalysisDepth.Thorough);

        TestAssert.Equal(
            TimeSpan.FromSeconds(2),
            fast.Options.VisualSignalSampleInterval,
            "Fast visual cadence.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(1),
            balanced.Options.VisualSignalSampleInterval,
            "Balanced visual cadence.");
        TestAssert.Equal(
            TimeSpan.FromMilliseconds(500),
            thorough.Options.VisualSignalSampleInterval,
            "Thorough visual cadence.");
        TestAssert.Equal(
            1,
            fast.IncludedRegionRoles.Count,
            "Fast scans Gameplay only.");
        TestAssert.True(
            balanced.IncludedRegionRoles.Contains(
                CompositionRegionRole.Presenter) &&
            thorough.IncludedRegionRoles.Contains(
                CompositionRegionRole.Presenter),
            "Balanced and Thorough retain confirmed Presenter evidence.");
        TestAssert.False(
            fast.PolicyVersion.Equals(
                balanced.PolicyVersion,
                StringComparison.Ordinal),
            "Depth must participate in evidence cache identity.");
        return Task.CompletedTask;
    }

    private static Task RequestRejectsUnsupportedDetection()
    {
        GenerationEvidenceAnalysisResult evidence =
            CreateEvidence(sourceCount: 1);
        var setup =
            new GenerationSetupOptions(
                GenerationMode.IndividualClips,
                DetectionMethod.LocalAi,
                AudioSelectionMode.Auto,
                5,
                50,
                ContentEmphasis.Balanced);

        TestAssert.Throws<ArgumentException>(
            () => new GenerationMomentFindingRequest(evidence, setup),
            "Only Heuristics is supported.");

        return Task.CompletedTask;
    }

    private static Task RequestRejectsForeignMapping()
    {
        GenerationEvidenceAnalysisResult evidence =
            CreateEvidence(sourceCount: 1);
        GenerationSetupOptions setup =
            CreateSetup(
                GenerationMode.IndividualClips,
                ContentEmphasis.Balanced,
                5,
                50);
        var foreign =
            new GenerationMomentFindingSettings(
                MediaMomentFindingOptions.CreateDefaults(
                    MomentOutputKind.MontageSegment,
                    MomentContentEmphasis.Balanced,
                    5,
                    50));

        TestAssert.Throws<ArgumentException>(
            () => new GenerationMomentFindingRequest(
                evidence,
                setup,
                foreign),
            "Custom settings must preserve setup mapping.");

        return Task.CompletedTask;
    }

    private static Task ServiceCallsOncePerSource()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(sourceCount: 3);
        var finder =
            new RecordingMomentFinder(
                [[90], [80], [70]]);
        var service =
            new GenerationMomentFindingService(finder);

        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.Equal(3, finder.Requests.Count, "One call per source.");
        TestAssert.Equal(3, result.Sources.Count, "One result per source.");

        for (int index = 0; index < 3; index++)
        {
            TestAssert.Same(
                request.Sources[index].PreparedSource.Media,
                finder.Requests[index].Media,
                "Sequential exact media.");
            TestAssert.Same(
                request.Sources[index].CompositionPlan.Plan,
                finder.Requests[index].Composition,
                "Exact plan.");
        }

        return Task.CompletedTask;
    }

    private static Task BatchPreservesIdentity()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(
                sourceCount: 2,
                referenceIndex: 1);
        var service =
            new GenerationMomentFindingService(
                new RecordingMomentFinder([[90], [80]]));
        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.Same(
            request.ReferenceSource,
            result.ReferenceSource.AnalyzedSource,
            "Explicit reference.");
        TestAssert.Same(
            request.Sources[0],
            result.Sources[0].AnalyzedSource,
            "Source identity.");

        return Task.CompletedTask;
    }

    private static Task DesiredCountIsGlobal()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(
                sourceCount: 2,
                desiredCount: 2);
        var service =
            new GenerationMomentFindingService(
                new RecordingMomentFinder(
                    [[95, 85], [90, 80]]));
        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.Equal(2, result.SelectedCandidates.Count, "Global maximum.");
        return Task.CompletedTask;
    }

    private static Task FillCountReturnsRequestedAmounts()
    {
        foreach (int requested in new[] { 5, 10 })
        {
            GenerationMomentFindingRequest request =
                CreateRequest(
                    sourceCount: 2,
                    desiredCount: requested,
                    qualityThreshold: 70,
                    fulfillmentPreference:
                        ClipFulfillmentPreference.FillRequestedCount);
            double[] first =
                Enumerable.Range(0, 6)
                    .Select(index => 96d - index * 8)
                    .ToArray();
            double[] second =
                Enumerable.Range(0, 6)
                    .Select(index => 93d - index * 8)
                    .ToArray();
            var service =
                new GenerationMomentFindingService(
                    new RecordingMomentFinder([first, second]));

            GenerationMomentFindingResult result =
                service.Find(request);

            TestAssert.Equal(
                requested,
                result.SelectedCount,
                $"Fill-count must return exactly {requested} when enough safe candidates exist.");
            TestAssert.True(
                result.IsRequestedCountMet,
                "The result must report count fulfillment.");
        }

        return Task.CompletedTask;
    }

    private static Task FillCountUsesBelowTargetCandidates()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(
                sourceCount: 1,
                desiredCount: 3,
                qualityThreshold: 70,
                fulfillmentPreference:
                    ClipFulfillmentPreference.FillRequestedCount);
        var service =
            new GenerationMomentFindingService(
                new RecordingMomentFinder([[90, 60, 50]]));

        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.Equal(3, result.SelectedCount, "Exact fill count.");
        TestAssert.Equal(2, result.BelowQualityTargetCount, "Below-target disclosure.");
        TestAssert.Equal(
            GenerationClipFulfillmentOutcome.RequestedCountMetWithLowerQuality,
            result.FulfillmentOutcome,
            "Below-target fulfillment outcome.");
        TestAssert.True(
            result.SelectedCandidates.Skip(1).All(
                candidate =>
                    candidate.SelectionReason ==
                    GenerationCandidateSelectionReason
                        .CountFillBelowQualityTarget),
            "Lower-scoring selections must retain an explicit reason.");

        return Task.CompletedTask;
    }

    private static Task FillCountRelaxesDiversityLast()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(
                sourceCount: 1,
                desiredCount: 3,
                qualityThreshold: 0,
                fulfillmentPreference:
                    ClipFulfillmentPreference.FillRequestedCount);
        var service =
            new GenerationMomentFindingService(
                new RecordingMomentFinder(
                    [[90, 80, 70]],
                    windowSpacingSeconds: 5));

        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.Equal(3, result.SelectedCount, "Overlapping safe windows may fill count last.");
        TestAssert.Equal(2, result.DiversityRelaxedCount, "Only the fallback windows relax diversity.");
        TestAssert.Equal(
            GenerationClipFulfillmentOutcome.RequestedCountMetWithDiversityRelaxation,
            result.FulfillmentOutcome,
            "Diversity relaxation must be visible.");

        return Task.CompletedTask;
    }

    private static Task QualityFirstPreservesShortfall()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(
                sourceCount: 1,
                desiredCount: 3,
                qualityThreshold: 70,
                fulfillmentPreference:
                    ClipFulfillmentPreference.QualityFirst);
        var service =
            new GenerationMomentFindingService(
                new RecordingMomentFinder([[90, 60, 50]]));

        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.Equal(1, result.SelectedCount, "Quality first must not lower the target.");
        TestAssert.Equal(0, result.BelowQualityTargetCount, "No below-target selection.");
        TestAssert.Equal(
            GenerationClipFulfillmentOutcome.QualityFirstShortfall,
            result.FulfillmentOutcome,
            "Quality-first shortfall outcome.");

        return Task.CompletedTask;
    }

    private static Task AutomaticCountReturnsQualityMatches()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(
                sourceCount: 1,
                desiredCount: 30,
                qualityThreshold: 70,
                fulfillmentPreference:
                    ClipFulfillmentPreference.QualityFirst,
                resultCountMode:
                    GenerationResultCountMode.Auto);
        var service = new GenerationMomentFindingService(
            new RecordingMomentFinder([[95, 80, 60]]));

        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.Equal(
            2,
            result.SelectedCount,
            "Auto must retain every distinct quality-qualified match without filling below the target.");
        TestAssert.True(
            result.IsRequestedCountMet,
            "Auto does not promise that all 30 slots will be filled.");
        TestAssert.Equal(
            GenerationClipFulfillmentOutcome.AutomaticQualityMatches,
            result.FulfillmentOutcome,
            "Auto completion must remain distinguishable from an exact-count result.");
        return Task.CompletedTask;
    }

    private static Task NoPerSourceQuota()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(
                sourceCount: 2,
                desiredCount: 2);
        var service =
            new GenerationMomentFindingService(
                new RecordingMomentFinder(
                    [[99, 98], [50]]));
        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.True(
            result.SelectedCandidates.All(
                candidate =>
                    ReferenceEquals(
                        candidate.AnalyzedSource,
                        request.Sources[0])),
            "The best source may supply every selected candidate.");

        return Task.CompletedTask;
    }

    private static Task DifferentSourcesDoNotSuppress()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(
                sourceCount: 2,
                desiredCount: 2);
        var service =
            new GenerationMomentFindingService(
                new RecordingMomentFinder([[90], [90]]));
        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.Equal(
            2,
            result.SelectedCandidates.Count,
            "Identical windows in different sources must both remain eligible.");
        return Task.CompletedTask;
    }

    private static Task GlobalRankIsDeterministic()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(
                sourceCount: 2,
                desiredCount: 2);
        var service =
            new GenerationMomentFindingService(
                new RecordingMomentFinder([[80], [90]]));
        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.Equal(1, result.SelectedCandidates[0].SourceOrder, "Highest score first.");
        TestAssert.Equal(1, result.SelectedCandidates[0].GlobalRank, "Rank one.");
        TestAssert.Equal(2, result.SelectedCandidates[1].GlobalRank, "Rank two.");
        return Task.CompletedTask;
    }

    private static Task CancellationStopsLaterSources()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(sourceCount: 3);
        using var cancellation =
            new CancellationTokenSource();
        var finder =
            new RecordingMomentFinder(
                [[90], [80], [70]],
                onCall: call =>
                {
                    if (call == 1)
                    {
                        cancellation.Cancel();
                    }
                });
        var service =
            new GenerationMomentFindingService(finder);

        TestAssert.Throws<OperationCanceledException>(
            () => service.Find(request, cancellation.Token),
            "Cancellation should stop the batch.");
        TestAssert.Equal(1, finder.Requests.Count, "Later sources must not run.");
        return Task.CompletedTask;
    }

    private static Task SourceResultRejectsForeign()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(sourceCount: 2);
        var finder =
            new RecordingMomentFinder([[90], [80]]);
        MediaMomentFindingResult low =
            finder.Find(
                CreateLowRequest(
                    request.Sources[0],
                    request.Settings.Options));

        TestAssert.Throws<ArgumentException>(
            () => new GenerationSourceMomentResult(
                request.Sources[1],
                low),
            "Foreign source result must fail.");
        return Task.CompletedTask;
    }

    private static Task CollectionsAreImmutable()
    {
        GenerationMomentFindingRequest request =
            CreateRequest(sourceCount: 1);
        var service =
            new GenerationMomentFindingService(
                new RecordingMomentFinder([[90]]));
        GenerationMomentFindingResult result =
            service.Find(request);

        TestAssert.True(
            result.Sources is not List<GenerationSourceMomentResult> &&
            result.SelectedCandidates is not List<GenerationMomentCandidate>,
            "Results must expose immutable snapshots.");
        return Task.CompletedTask;
    }

    private static Task HiddenMomentsRetainSafeAlternates()
    {
        GenerationMomentFindingRequest request = CreateRequest(
            sourceCount: 1,
            desiredCount: 1,
            qualityThreshold: 70,
            fulfillmentPreference: ClipFulfillmentPreference.QualityFirst);
        var service = new GenerationMomentFindingService(
            new RecordingMomentFinder([[90, 80, 60]]));
        GenerationMomentFindingResult result = service.Find(request);

        GenerationHiddenMomentDeck deck =
            GenerationHiddenMomentPlanner.Create(result);

        TestAssert.Equal(2, deck.Count, "Every unselected safe proposal is reviewable.");
        TestAssert.Equal(80d, deck.Moments[0].FinalScore, "Near-cutoff moment is first.");
        TestAssert.Equal(
            GenerationHiddenMomentReason.RequestedCountReached,
            deck.Moments[0].Reason,
            "A quality-qualified alternate records the count boundary.");
        TestAssert.Equal(
            GenerationHiddenMomentReason.BelowQualityTarget,
            deck.Moments[1].Reason,
            "A below-target alternate remains explicitly labeled.");
        TestAssert.False(
            deck.Moments.Any(hidden => result.SelectedCandidates.Any(
                selected => selected.Id == hidden.Id)),
            "The deck and selected clips must remain disjoint.");
        return Task.CompletedTask;
    }

    private static Task HiddenMomentAcceptanceAddsAsset()
    {
        GenerationMomentFindingRequest request = CreateRequest(
            sourceCount: 1,
            desiredCount: 1,
            qualityThreshold: 70,
            fulfillmentPreference: ClipFulfillmentPreference.QualityFirst);
        GenerationMomentFindingResult moments =
            new GenerationMomentFindingService(
                new RecordingMomentFinder([[90, 80]]))
            .Find(request);
        GenerationHiddenMomentDeck deck =
            GenerationHiddenMomentPlanner.Create(moments);
        GenerationMomentCandidate selected = moments.SelectedCandidates[0];
        var asset = new ReplayFoundry.Desktop.Features.Generate.Handoff.GenerationOutputAsset(
            selected.Id,
            1,
            selected.AnalyzedSource.PreparedSource.Media,
            outputFullPath: null,
            selected.Candidate.Window.Start,
            selected.Candidate.Window.End,
            selected.FinalScore,
            request.Setup.QualityThreshold,
            selected.SelectionReason,
            "Selected deterministic candidate.",
            preferenceFeatures:
                GenerationClipPreferenceFeatureExtractor.Create(selected));
        var project = new ReplayFoundry.Desktop.Features.Generate.Handoff.GenerationOutputProject(
            "project-hidden-moment",
            GenerationMode.IndividualClips,
            Path.Combine(Path.GetTempPath(), "ReplayFoundryHiddenMomentTest"),
            1,
            ClipFulfillmentPreference.QualityFirst,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UtcNow,
            hiddenMoments: deck.Moments);
        var session = new ReplayFoundry.Desktop.Features.Generate.Handoff.GenerationOutputSession();
        session.Publish(project);

        session.AcceptHiddenMoment(project.Id, deck.Moments[0].Id);

        TestAssert.Equal(2, session.Current!.Assets.Count, "Accepted asset count.");
        TestAssert.Equal(0, session.Current.HiddenMomentCount, "Accepted alternate leaves the deck.");
        TestAssert.Equal(
            GenerationCandidateSelectionReason.HiddenMomentRecovery,
            session.Current.Assets[1].SelectionReason,
            "Accepted moments retain a distinct discovery provenance.");
        TestAssert.False(
            session.Current.Assets[1].IsRendered,
            "Acceptance must not render before Studio finalization.");
        return Task.CompletedTask;
    }

    private static Task HiddenMomentsCarryEditorialTier()
    {
        GenerationMomentFindingRequest balancedRequest = CreateRequest(
            sourceCount: 1,
            desiredCount: 1,
            analysisDepth: GenerationAnalysisDepth.Balanced);
        GenerationMomentFindingRequest thoroughRequest = CreateRequest(
            sourceCount: 1,
            desiredCount: 1,
            analysisDepth: GenerationAnalysisDepth.Thorough);
        var finder = new RecordingMomentFinder([[90, 80]]);

        GenerationHiddenMoment balanced = GenerationHiddenMomentPlanner.Create(
            new GenerationMomentFindingService(finder).Find(balancedRequest))
            .Moments[0];
        GenerationHiddenMoment thorough = GenerationHiddenMomentPlanner.Create(
            new GenerationMomentFindingService(finder).Find(thoroughRequest))
            .Moments[0];

        TestAssert.Equal(
            ClipEditorialGenerationPreference.HeuristicOnly,
            balanced.EditorialPreference,
            "Balanced Hidden Moments must retain the lightweight local path.");
        TestAssert.Equal(
            ClipEditorialGenerationPreference.AiRequired,
            thorough.EditorialPreference,
            "Thorough Hidden Moments must require qualified AI rather than silently substituting a heuristic label.");
        return Task.CompletedTask;
    }

    internal static GenerationMomentFindingRequest CreateRequest(
        int sourceCount,
        int referenceIndex = 0,
        int desiredCount = 5,
        double qualityThreshold = 0,
        ClipFulfillmentPreference fulfillmentPreference =
            ClipFulfillmentPreference.FillRequestedCount,
        GenerationResultCountMode resultCountMode =
            GenerationResultCountMode.Exact,
        GenerationAnalysisDepth analysisDepth =
            GenerationAnalysisDepth.Balanced)
    {
        GenerationEvidenceAnalysisResult evidence =
            CreateEvidence(sourceCount, referenceIndex);
        GenerationSetupOptions setup =
            CreateSetup(
                GenerationMode.IndividualClips,
                ContentEmphasis.Balanced,
                desiredCount,
                qualityThreshold,
                fulfillmentPreference,
                resultCountMode,
                analysisDepth);

        return new GenerationMomentFindingRequest(
            evidence,
            setup);
    }

    internal static GenerationEvidenceAnalysisResult CreateEvidence(
        int sourceCount,
        int referenceIndex = 0)
    {
        GenerationEvidenceAnalysisRequest request =
            GenerationEvidenceAnalysisTests.CreateRequest(
                sourceCount,
                referenceIndex);

        return TestMediaFactory.CreateEvidenceAnalysisResult(
            request);
    }

    internal static GenerationSetupOptions CreateSetup(
        GenerationMode mode,
        ContentEmphasis emphasis,
        int count,
        double threshold,
        ClipFulfillmentPreference fulfillmentPreference =
            ClipFulfillmentPreference.FillRequestedCount,
        GenerationResultCountMode resultCountMode =
            GenerationResultCountMode.Exact,
        GenerationAnalysisDepth analysisDepth =
            GenerationAnalysisDepth.Balanced) =>
        new(
            mode,
            DetectionMethod.Heuristics,
            AudioSelectionMode.Auto,
            count,
            threshold,
            emphasis,
            fulfillmentPreference,
            resultCountMode: resultCountMode,
            analysisDepth: analysisDepth);

    private static MediaMomentFindingRequest CreateLowRequest(
        AnalyzedGenerationSource source,
        MediaMomentFindingOptions options) =>
        new(
            source.PreparedSource.Media,
            source.CompositionPlan.Plan,
            source.Evidence,
            source.Summary,
            options);

    internal sealed class RecordingMomentFinder :
        IMediaMomentFinder
    {
        private readonly IReadOnlyList<IReadOnlyList<double>>
            _scoresByCall;
        private readonly Action<int>? _onCall;
        private readonly double _windowSpacingSeconds;

        public RecordingMomentFinder(
            IReadOnlyList<IReadOnlyList<double>> scoresByCall,
            Action<int>? onCall = null,
            double windowSpacingSeconds = 50)
        {
            _scoresByCall = scoresByCall;
            _onCall = onCall;
            _windowSpacingSeconds = windowSpacingSeconds;
        }

        public MediaMomentFinderIdentity Identity { get; } =
            new(
                "test-moment-finder",
                "1.0");

        public List<MediaMomentFindingRequest> Requests { get; } =
            [];

        public MediaMomentFindingResult Find(
            MediaMomentFindingRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            int call = Requests.Count;
            _onCall?.Invoke(call);

            IReadOnlyList<double> scores =
                _scoresByCall[Math.Min(
                    call - 1,
                    _scoresByCall.Count - 1)];
            var candidates =
                new List<MomentCandidate>();

            for (int index = 0;
                 index < scores.Count;
                 index++)
            {
                TimeSpan start =
                    TimeSpan.FromSeconds(
                        10 + (index * _windowSpacingSeconds));
                TimeSpan end =
                    start + TimeSpan.FromSeconds(30);
                VisualEvidenceTarget target =
                    request.Evidence.RegionVisualResults
                        .First(
                            static result =>
                                result.Target.Role ==
                                CompositionRegionRole.Gameplay)
                        .Target;
                var reference =
                    new MomentEvidenceReference(
                        MomentEvidenceReferenceKind.GameplayActivitySample,
                        start,
                        start,
                        "test Gameplay activity",
                        target.TargetKey,
                        target.IntervalIndex,
                        target.RegionId,
                        target.Role,
                        rawValue: scores[index] / 100,
                        normalizedValue: scores[index] / 100);
                var anchor =
                    new MomentAnchor(
                        $"anchor-{call}-{index}",
                        MomentAnchorKind.GameplayActivityBurst,
                        start,
                        scores[index] / 100,
                        scores[index] / 100,
                        [reference]);
                var component =
                    new MomentScoreComponent(
                        MomentScoreComponentCode.GameplayProminence,
                        scores[index] / 100,
                        scores[index] / 100,
                        100,
                        scores[index],
                        "test",
                        [reference]);
                MomentCandidateDisposition disposition =
                    scores[index] <
                        request.Options.MinimumHeuristicScore
                        ? MomentCandidateDisposition.BelowThreshold
                        : scores
                            .Take(index)
                            .Count(
                                score =>
                                    score >=
                                    request.Options.MinimumHeuristicScore) <
                            request.Options.DesiredCandidateCount
                            ? MomentCandidateDisposition.Selected
                            : MomentCandidateDisposition.Eligible;
                var neighborhood =
                    new MomentEventNeighborhood(
                        $"neighborhood-{call}-{index}",
                        start,
                        start,
                        start,
                        [anchor],
                        [MomentSignalFamily.GameplayBurst]);

                candidates.Add(
                    new MomentCandidate(
                        $"candidate-{call}-{index}",
                        new MomentCandidateWindow(
                            start,
                            end,
                            request.Media.Duration),
                        MomentCandidateConstructionReason.SingleAnchor,
                        neighborhood,
                        [anchor],
                        new MomentScore([component]),
                        disposition,
                        0,
                        0,
                        0,
                        0));
            }

            MomentCandidate[] selected =
                candidates
                    .Where(
                        static candidate =>
                            candidate.Disposition ==
                            MomentCandidateDisposition.Selected)
                    .ToArray();

            var manifest =
                new MediaMomentFindingManifest(
                    Identity,
                    DateTimeOffset.UtcNow,
                    request.Media.FullPath,
                    request.Media.Duration,
                    request.Options,
                    request.Evidence.Manifest.AnalyzerName,
                    request.Evidence.Manifest.AnalyzerVersion,
                    request.Evidence.Manifest.SignalSchemaVersion,
                    request.Evidence.Manifest.Options.VisualSignalSampleInterval,
                    request.Evidence.Manifest.Options.AudioSignalWindowDuration,
                    request.Evidence.Manifest.RequestedIncludedRegionRoles,
                    request.Composition.Manifest.SchemaVersion,
                    request.Composition.Manifest.CoordinateSpaceVersion,
                    request.Composition.Manifest.Origin,
                    Enum.GetValues<MomentAnchorKind>()
                        .Select(
                            kind =>
                                new KeyValuePair<MomentAnchorKind, int>(
                                    kind,
                                    kind == MomentAnchorKind.GameplayActivityBurst
                                        ? candidates.Count
                                        : 0)),
                    candidates.Count,
                    0,
                    candidates.Count(
                        static candidate =>
                            candidate.Disposition ==
                            MomentCandidateDisposition.BelowThreshold),
                    0,
                    selected.Length,
                    TimeSpan.Zero,
                    "test deterministic coverage");

            return new MediaMomentFindingResult(
                request,
                candidates,
                selected,
                [],
                manifest);
        }
    }
}
