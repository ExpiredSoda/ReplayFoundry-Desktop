using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;

namespace ReplayFoundry.PreparationTests;

internal static class GenerateWorkflowStateOwnerTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Source-selection state adds in order and rejects case-insensitive duplicates",
            SourceStateAddsInOrder),
        new(
            "Source-selection state preserves an explicit reference",
            SourceStatePreservesReference),
        new(
            "Source-selection state removal preserves one explicit reference",
            SourceStateRemovalPreservesReference),
        new(
            "Source-selection state clears sources and validation",
            SourceStateClears),
        new(
            "Source-selection state validates supported files",
            SourceStateValidates),
        new(
            "Source-selection state returns an immutable snapshot",
            SourceStateSnapshots),
        new(
            "Source-selection state publishes focused notifications",
            SourceStateNotifies),
        new(
            "Workflow session retains valid preparation setup composition and evidence",
            SessionRetainsValidState),
        new(
            "Workflow session source invalidation clears every dependent artifact",
            SessionInvalidatesAfterSourceChange),
        new(
            "Workflow session mode invalidation clears setup only",
            SessionInvalidatesSetupOnly),
        new(
            "Workflow session composition invalidation clears composition and evidence",
            SessionInvalidatesCompositionAndEvidence),
        new(
            "Workflow session analyzer invalidation clears evidence only",
            SessionInvalidatesEvidenceOnly),
        new(
            "Workflow session rejects incompatible composition",
            SessionRejectsIncompatibleComposition),
        new(
            "Workflow session invalidation is idempotent",
            SessionInvalidationIsIdempotent),
        new(
            "Operation controller permits only one active operation",
            OperationControllerRejectsDuplicate),
        new(
            "Operation controller cancellation reaches the active token",
            OperationControllerCancels),
        new(
            "Operation controller rejects stale completion",
            OperationControllerRejectsStaleCompletion),
        new(
            "Operation controller finalization clears busy state",
            OperationControllerFinalizes),
        new(
            "Operation controller disposal cancels the active operation",
            OperationControllerDisposes),
        new(
            "Operation controller does not translate failure into cancellation",
            OperationControllerDistinguishesFailure),
    ];

    private static Task SourceStateAddsInOrder()
    {
        GenerationSourceSelectionState state =
            CreateSourceState(
                out string first,
                out string second);

        state.AddCandidates(
        [
            first,
            first.ToUpperInvariant(),
            second,
        ]);

        TestAssert.Equal(
            2,
            state.Count,
            "A duplicate path must not be retained.");

        TestAssert.Equal(
            first,
            state.Sources[0].FullPath,
            "The first source must retain candidate order.");

        TestAssert.Equal(
            second,
            state.Sources[1].FullPath,
            "The second source must retain candidate order.");

        TestAssert.True(
            state.ValidationMessage?.Contains(
                "already",
                StringComparison.OrdinalIgnoreCase) == true,
            "Duplicate validation must remain user-visible.");

        return Task.CompletedTask;
    }

    private static Task SourceStatePreservesReference()
    {
        GenerationSourceSelectionState state =
            CreateSourceState(
                out string first,
                out string second);

        state.AddCandidates([first, second]);
        state.SetReference(second);

        TestAssert.False(
            state.Sources[0].IsReference,
            "Changing the reference must clear the prior reference.");

        TestAssert.True(
            state.Sources[1].IsReference,
            "The explicitly selected source must become the only reference.");

        GenerationSourcePreparationRequest request =
            new(state.CreateSnapshot());

        TestAssert.Equal(
            second,
            request.ReferenceSource.FullPath,
            "The immutable workflow snapshot must preserve the explicit reference.");

        return Task.CompletedTask;
    }

    private static Task SourceStateClears()
    {
        GenerationSourceSelectionState state =
            CreateSourceState(
                out string first,
                out _);

        state.AddCandidates([first, first]);
        state.Clear();

        TestAssert.False(
            state.HasSources,
            "Clear must remove all selected sources.");

        TestAssert.Null(
            state.ValidationMessage,
            "Clear must remove source-selection validation.");

        return Task.CompletedTask;
    }

    private static Task SourceStateRemovalPreservesReference()
    {
        GenerationSourceSelectionState state =
            CreateSourceState(
                out string first,
                out string second);

        state.AddCandidates([first, second]);
        state.Remove(first);

        TestAssert.Equal(
            1,
            state.Count,
            "Removing a selected source must preserve the remaining order.");

        TestAssert.True(
            state.Sources[0].IsReference,
            "Removing the reference must promote exactly one remaining source.");

        return Task.CompletedTask;
    }

    private static Task SourceStateValidates()
    {
        GenerationSourceSelectionState state =
            CreateSourceState(
                out string first,
                out _);

        string unsupported =
            TestMediaFactory.CreateExistingSourcePath(
                "generate-source-state.txt");

        state.AddCandidates([unsupported, first]);

        TestAssert.Equal(
            1,
            state.Count,
            "Unsupported extensions must not enter the selection.");

        TestAssert.True(
            state.ValidationMessage?.Contains(
                "Unsupported video format",
                StringComparison.Ordinal) == true,
            "Unsupported-extension validation must identify the format.");

        TestAssert.Equal(
            0,
            state.ValidateCurrentSelection().Count,
            "The retained valid source must revalidate successfully.");

        return Task.CompletedTask;
    }

    private static Task SourceStateSnapshots()
    {
        GenerationSourceSelectionState state =
            CreateSourceState(
                out string first,
                out string second);

        state.AddCandidates([first]);
        IReadOnlyList<SelectedVideoSource> snapshot =
            state.CreateSnapshot();
        state.AddCandidates([second]);

        TestAssert.Equal(
            1,
            snapshot.Count,
            "A workflow snapshot must not observe later source mutations.");

        TestAssert.Throws<NotSupportedException>(
            () => ((IList<SelectedVideoSource>)snapshot)[0] =
                new SelectedVideoSource(second, true),
            "The source snapshot must reject caller mutation.");

        return Task.CompletedTask;
    }

    private static Task SourceStateNotifies()
    {
        GenerationSourceSelectionState state =
            CreateSourceState(
                out string first,
                out _);

        var notifications =
            new List<GenerationSourceSelectionChangedEventArgs>();

        state.Changed +=
            (_, eventArgs) =>
                notifications.Add(eventArgs);

        state.AddCandidates([first]);
        state.AddCandidates([first]);

        TestAssert.Equal(
            2,
            notifications.Count,
            "Source and validation changes must each publish one focused notification.");

        TestAssert.True(
            notifications[0].SourcesChanged,
            "Adding a source must identify a source projection change.");

        TestAssert.True(
            notifications[1].ValidationChanged &&
            !notifications[1].SourcesChanged,
            "A duplicate-only operation must identify validation without a source change.");

        return Task.CompletedTask;
    }

    private static Task SessionRetainsValidState()
    {
        SessionContext context = CreateSessionContext();
        GenerationSourcePreparationResult preparation =
            context.Preparation;
        GenerationCompositionReviewResult composition =
            PreparedGenerationWorkflowTests.CreateCompositionReview(
                preparation);
        GenerationSetupOptions setup = CreateSetup();

        context.Session.AcceptPreparation(preparation);
        context.Session.SetSetup(setup);
        context.Session.SetComposition(composition);

        var request =
            new GenerationEvidenceAnalysisRequest(
                preparation,
                composition,
                context.Evidence.Settings);

        GenerationEvidenceAnalysisResult evidence =
            TestMediaFactory.CreateEvidenceAnalysisResult(request);

        context.Evidence.SetCurrent(evidence);
        context.Session.AcceptEvidence(evidence);

        TestAssert.Same(
            preparation,
            context.Session.Preparation!,
            "The session must project the current preparation.");

        TestAssert.Same(
            setup,
            context.Session.Setup!,
            "The session must retain Setup.");

        TestAssert.Same(
            composition,
            context.Session.Composition!,
            "The session must retain composition.");

        TestAssert.Same(
            evidence,
            context.Session.Evidence!,
            "The session must project current evidence.");

        return Task.CompletedTask;
    }

    private static Task SessionInvalidatesAfterSourceChange()
    {
        SessionContext context = CreatePopulatedSession();

        context.Session.InvalidateAfterSourceChange();

        TestAssert.Null(
            context.Session.Preparation,
            "Source invalidation must clear preparation.");
        TestAssert.Null(
            context.Session.Setup,
            "Source invalidation must clear Setup.");
        TestAssert.Null(
            context.Session.Composition,
            "Source invalidation must clear composition.");
        TestAssert.Null(
            context.Session.Evidence,
            "Source invalidation must clear evidence.");
        TestAssert.Equal(
            1,
            context.PreparationCoordinator.InvalidationCount,
            "Source invalidation must reach the preparation coordinator.");
        TestAssert.Equal(
            1,
            context.Evidence.InvalidationCount,
            "Source invalidation must reach the evidence coordinator.");

        return Task.CompletedTask;
    }

    private static Task SessionInvalidatesSetupOnly()
    {
        SessionContext context = CreatePopulatedSession();
        GenerationSourcePreparationResult preparation =
            context.Session.Preparation!;
        GenerationCompositionReviewResult composition =
            context.Session.Composition!;
        GenerationEvidenceAnalysisResult evidence =
            context.Session.Evidence!;

        context.Session.InvalidateAfterModeChange();

        TestAssert.Null(
            context.Session.Setup,
            "Mode invalidation must clear mode-specific Setup.");
        TestAssert.Same(
            preparation,
            context.Session.Preparation!,
            "Mode invalidation must retain preparation.");
        TestAssert.Same(
            composition,
            context.Session.Composition!,
            "Mode invalidation must retain composition.");
        TestAssert.Same(
            evidence,
            context.Session.Evidence!,
            "Mode invalidation must retain deterministic evidence.");

        return Task.CompletedTask;
    }

    private static Task SessionInvalidatesCompositionAndEvidence()
    {
        SessionContext context = CreatePopulatedSession();

        context.Session.InvalidateCompositionAndEvidence();

        TestAssert.Same(
            context.Preparation,
            context.Session.Preparation!,
            "Composition invalidation must retain preparation.");
        TestAssert.True(
            context.Session.Setup is not null,
            "Composition invalidation must retain Setup.");
        TestAssert.Null(
            context.Session.Composition,
            "Composition invalidation must clear composition.");
        TestAssert.Null(
            context.Session.Evidence,
            "Composition invalidation must clear evidence.");

        return Task.CompletedTask;
    }

    private static Task SessionInvalidatesEvidenceOnly()
    {
        SessionContext context = CreatePopulatedSession();
        GenerationCompositionReviewResult composition =
            context.Session.Composition!;

        context.Session.InvalidateAfterAnalyzerOrPolicyChange();

        TestAssert.Same(
            composition,
            context.Session.Composition!,
            "Analyzer invalidation must retain confirmed composition.");
        TestAssert.Null(
            context.Session.Evidence,
            "Analyzer invalidation must clear evidence.");

        return Task.CompletedTask;
    }

    private static Task SessionRejectsIncompatibleComposition()
    {
        SessionContext context = CreateSessionContext();
        GenerationSourcePreparationResult foreign =
            GenerationEvidenceAnalysisTests.CreatePreparation();

        context.Session.AcceptPreparation(context.Preparation);

        GenerationCompositionReviewResult foreignComposition =
            PreparedGenerationWorkflowTests.CreateCompositionReview(
                foreign);

        TestAssert.Throws<ArgumentException>(
            () => context.Session.SetComposition(
                foreignComposition),
            "The session must reject composition from another preparation.");

        return Task.CompletedTask;
    }

    private static Task SessionInvalidationIsIdempotent()
    {
        SessionContext context = CreatePopulatedSession();

        context.Session.InvalidateAfterSourceChange();
        context.Session.InvalidateAfterSourceChange();

        TestAssert.Null(
            context.Session.Preparation,
            "Repeated source invalidation must remain cleared.");
        TestAssert.Null(
            context.Session.Setup,
            "Repeated source invalidation must not recreate Setup.");
        TestAssert.Null(
            context.Session.Composition,
            "Repeated source invalidation must not recreate composition.");
        TestAssert.Null(
            context.Session.Evidence,
            "Repeated source invalidation must not recreate evidence.");

        return Task.CompletedTask;
    }

    private static Task OperationControllerRejectsDuplicate()
    {
        using var controller =
            new GenerationOperationController();
        using GenerationOperationLease active =
            controller.Begin(
                GenerationOperationKind.SourcePreparation);

        TestAssert.Throws<InvalidOperationException>(
            () => controller.Begin(
                GenerationOperationKind.EvidenceAnalysis),
            "A second active Generate operation must be rejected.");

        return Task.CompletedTask;
    }

    private static Task OperationControllerCancels()
    {
        using var controller =
            new GenerationOperationController();
        using GenerationOperationLease active =
            controller.Begin(
                GenerationOperationKind.EvidenceAnalysis);

        controller.CancelActive();

        TestAssert.True(
            active.CancellationToken.IsCancellationRequested,
            "Cancellation must reach the active operation token.");

        return Task.CompletedTask;
    }

    private static Task OperationControllerRejectsStaleCompletion()
    {
        using var controller =
            new GenerationOperationController();
        GenerationOperationLease first =
            controller.Begin(
                GenerationOperationKind.SourcePreparation);

        first.Dispose();

        using GenerationOperationLease second =
            controller.Begin(
                GenerationOperationKind.EvidenceAnalysis);

        first.Dispose();

        TestAssert.True(
            second.IsCurrent,
            "A stale lease must not finalize a newer active operation.");

        return Task.CompletedTask;
    }

    private static Task OperationControllerFinalizes()
    {
        using var controller =
            new GenerationOperationController();
        GenerationOperationLease active =
            controller.Begin(
                GenerationOperationKind.Generation);

        active.Dispose();

        TestAssert.False(
            controller.HasActiveOperation,
            "Finalization must clear active-operation state.");

        return Task.CompletedTask;
    }

    private static Task OperationControllerDisposes()
    {
        var controller =
            new GenerationOperationController();
        GenerationOperationLease active =
            controller.Begin(
                GenerationOperationKind.EvidenceAnalysis);
        CancellationToken token =
            active.CancellationToken;

        controller.Dispose();

        TestAssert.True(
            token.IsCancellationRequested,
            "Controller disposal must cancel the owned active token.");

        TestAssert.False(
            controller.HasActiveOperation,
            "Controller disposal must clear active-operation state.");

        return Task.CompletedTask;
    }

    private static Task OperationControllerDistinguishesFailure()
    {
        using var controller =
            new GenerationOperationController();
        using GenerationOperationLease active =
            controller.Begin(
                GenerationOperationKind.Generation);

        var failure =
            new InvalidOperationException("synthetic failure");

        TestAssert.False(
            active.IsCancellationRequested,
            "Starting or observing a failure must not fabricate cancellation.");

        TestAssert.Equal(
            "synthetic failure",
            failure.Message,
            "The operation controller must not translate workflow failures.");

        return Task.CompletedTask;
    }

    private static GenerationSourceSelectionState CreateSourceState(
        out string first,
        out string second)
    {
        first = TestMediaFactory.CreateExistingSourcePath(
            "generate-source-state-first.mkv");
        second = TestMediaFactory.CreateExistingSourcePath(
            "generate-source-state-second.mp4");

        return new GenerationSourceSelectionState(
            new VideoSourceValidator());
    }

    private static SessionContext CreateSessionContext()
    {
        GenerationSourcePreparationResult preparation =
            GenerationEvidenceAnalysisTests.CreatePreparation();
        var preparationCoordinator =
            new SessionPreparationCoordinator(preparation);
        var evidenceCoordinator =
            new SessionEvidenceCoordinator();
        var session =
            new GenerationWorkflowSessionState(
                preparationCoordinator,
                evidenceCoordinator);

        return new SessionContext(
            session,
            preparation,
            preparationCoordinator,
            evidenceCoordinator);
    }

    private static SessionContext CreatePopulatedSession()
    {
        SessionContext context = CreateSessionContext();
        GenerationCompositionReviewResult composition =
            PreparedGenerationWorkflowTests.CreateCompositionReview(
                context.Preparation);

        context.Session.AcceptPreparation(context.Preparation);
        context.Session.SetSetup(CreateSetup());
        context.Session.SetComposition(composition);

        var request =
            new GenerationEvidenceAnalysisRequest(
                context.Preparation,
                composition,
                context.Evidence.Settings);

        GenerationEvidenceAnalysisResult evidence =
            TestMediaFactory.CreateEvidenceAnalysisResult(request);

        context.Evidence.SetCurrent(evidence);
        context.Session.AcceptEvidence(evidence);

        return context;
    }

    private static GenerationSetupOptions CreateSetup()
    {
        return new GenerationSetupOptions(
            GenerationMode.IndividualClips,
            DetectionMethod.Heuristics,
            AudioSelectionMode.Auto,
            desiredResultCount: 3,
            qualityThreshold: 70,
            ContentEmphasis.Balanced);
    }

    private sealed record SessionContext(
        GenerationWorkflowSessionState Session,
        GenerationSourcePreparationResult Preparation,
        SessionPreparationCoordinator PreparationCoordinator,
        SessionEvidenceCoordinator Evidence);

    private sealed class SessionPreparationCoordinator :
        IGenerationSourcePreparationCoordinator
    {
        public SessionPreparationCoordinator(
            GenerationSourcePreparationResult current)
        {
            Current = current;
        }

        public GenerationSourcePreparationResult? Current { get; private set; }

        public int InvalidationCount { get; private set; }

        public Task<GenerationSourcePreparationResult> GetOrPrepareAsync(
            GenerationSourcePreparationRequest request,
            IProgress<GenerationSourcePreparationProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Current ??
                throw new InvalidOperationException(
                    "No preparation is current."));
        }

        public void EnsureFresh(
            GenerationSourcePreparationResult preparation)
        {
            if (!ReferenceEquals(Current, preparation))
            {
                throw new InvalidOperationException(
                    "The preparation is not current.");
            }
        }

        public void Invalidate()
        {
            InvalidationCount++;
            Current = null;
        }
    }

    private sealed class SessionEvidenceCoordinator :
        IGenerationEvidenceAnalysisCoordinator
    {
        public GenerationEvidenceAnalysisSettings Settings { get; } =
            GenerationEvidenceAnalysisSettings.CreateDefault();

        public GenerationEvidenceAnalysisResult? Current { get; private set; }

        public int InvalidationCount { get; private set; }

        public void SetCurrent(
            GenerationEvidenceAnalysisResult current)
        {
            Current = current;
        }

        public Task<GenerationEvidenceAnalysisResult> GetOrAnalyzeAsync(
            GenerationEvidenceAnalysisRequest request,
            IProgress<GenerationEvidenceAnalysisProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Current ??
                throw new InvalidOperationException(
                    "No evidence is current."));
        }

        public void Invalidate()
        {
            InvalidationCount++;
            Current = null;
        }
    }
}
