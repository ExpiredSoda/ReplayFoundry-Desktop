using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Summaries;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationEvidenceAnalysisTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Evidence settings snapshot options and canonical roles",
            SettingsSnapshotInputs),
        new(
            "Evidence settings reject undefined duplicate or missing Gameplay roles",
            SettingsRejectInvalidRoles),
        new(
            "Evidence request validates retained composition and explicit reference",
            RequestValidatesCompositionAndReference),
        new(
            "Analyzed source validates identity path duration roles and summary",
            AnalyzedSourceValidatesPayload),
        new(
            "Evidence batch validates completeness order reference and immutability",
            BatchValidatesCompletenessAndOrder),
        new(
            "Evidence service analyzes every source once with its exact plan",
            ServiceAnalyzesSourcesAndBuildsSummaries),
        new(
            "Evidence service analyzes sources sequentially",
            ServiceIsSequential),
        new(
            "Evidence service cancellation stops before later sources",
            ServiceCancellationStopsLaterSources),
        new(
            "Evidence service preserves source diagnostics and inner failure",
            ServicePreservesFailureDetails),
        new(
            "Evidence service keeps tool-unavailable failures distinguishable",
            ServiceDistinguishesToolFailure),
        new(
            "Evidence progress uses typed truthful phases and real boundaries",
            ServiceTranslatesTypedProgress),
        new(
            "Evidence coordinator reuses and rebinds an identical request",
            CoordinatorReusesAndRebinds),
        new(
            "Semantically identical layouts reuse evidence despite creation time",
            CoordinatorIgnoresCompositionCreationTime),
        new(
            "Composition geometry changes invalidate evidence",
            GeometryChangeInvalidates),
        new(
            "Composition interval and region identifier changes invalidate evidence",
            IntervalAndIdChangesInvalidate),
        new(
            "Composition role and trait changes invalidate evidence",
            RoleAndTraitChangesInvalidate),
        new(
            "Changed source snapshots fail freshness before evidence reuse",
            SourceSnapshotChangeFailsFreshness),
        new(
            "Evidence options and included roles invalidate reuse",
            SettingsChangesInvalidate),
        new(
            "Signal cadence changes invalidate evidence reuse",
            SignalCadenceChangesInvalidate),
        new(
            "Analyzer version changes invalidate reuse",
            AnalyzerVersionChangeInvalidates),
        new(
            "Cancellation and failure never cache partial evidence",
            CancellationAndFailureDoNotCache),
        new(
            "Concurrent identical requests share one analysis",
            ConcurrentRequestsDoNotDuplicateAnalysis),
    ];

}
