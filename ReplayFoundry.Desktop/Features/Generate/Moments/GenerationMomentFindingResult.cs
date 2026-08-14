using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Moments;

public enum GenerationClipFulfillmentOutcome
{
    RequestedCountMetAtQualityTarget,
    RequestedCountMetWithLowerQuality,
    RequestedCountMetWithDiversityRelaxation,
    QualityFirstShortfall,
    InsufficientSafeCandidates,
    AutomaticQualityMatches,
}

public sealed class GenerationMomentFindingResult
{
    private readonly ReadOnlyCollection<GenerationSourceMomentResult>
        _sources;
    private readonly ReadOnlyCollection<GenerationMomentCandidate>
        _selectedCandidates;

    public GenerationMomentFindingResult(
        GenerationMomentFindingRequest request,
        IEnumerable<GenerationSourceMomentResult> sources,
        IEnumerable<GenerationMomentCandidate> selectedCandidates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(selectedCandidates);

        GenerationSourceMomentResult[] sourceSnapshot =
            sources.ToArray();
        GenerationMomentCandidate[] selectedSnapshot =
            selectedCandidates.ToArray();

        if (sourceSnapshot.Any(static item => item is null) ||
            selectedSnapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Generation moment collections cannot contain null entries.");
        }

        if (sourceSnapshot.Length != request.SourceCount)
        {
            throw new ArgumentException(
                "Generation moment finding requires one result per analyzed source.",
                nameof(sources));
        }

        for (int index = 0;
             index < sourceSnapshot.Length;
             index++)
        {
            if (!ReferenceEquals(
                    sourceSnapshot[index].AnalyzedSource,
                    request.Sources[index]))
            {
                throw new ArgumentException(
                    "Source moment results must preserve evidence-analysis order and identity.",
                    nameof(sources));
            }
        }

        if (selectedSnapshot.Length >
                request.Setup.DesiredResultCount ||
            selectedSnapshot
                .Select(static item => item.GlobalRank)
                .SequenceEqual(
                    Enumerable.Range(
                        1,
                        selectedSnapshot.Length)) is false ||
            selectedSnapshot
                .GroupBy(static item => item.Id, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            selectedSnapshot.Any(
                item =>
                    item.SourceOrder >=
                        sourceSnapshot.Length ||
                    !ReferenceEquals(
                        sourceSnapshot[item.SourceOrder]
                            .AnalyzedSource,
                        item.AnalyzedSource) ||
                    !sourceSnapshot[item.SourceOrder]
                        .Moments.Proposals.Any(
                            proposal =>
                                ReferenceEquals(
                                    proposal,
                                    item.Candidate))))
        {
            throw new ArgumentException(
                "Selected generation candidates must be ranked unique entries from the source proposal pools.",
                nameof(selectedCandidates));
        }

        int belowQualityTargetCount =
            selectedSnapshot.Count(
                candidate =>
                    candidate.FinalScore <
                    request.Setup.QualityThreshold);
        int diversityRelaxedCount =
            selectedSnapshot.Count(
                static candidate =>
                    candidate.RequiredDiversityRelaxation);
        int humanPriorityCount =
            selectedSnapshot.Count(static candidate => candidate.IsHumanPriority);

        if (request.Setup.ClipFulfillmentPreference ==
                ClipFulfillmentPreference.QualityFirst &&
            (diversityRelaxedCount != 0 ||
             selectedSnapshot.Any(
                 candidate =>
                     candidate.FinalScore <
                         request.Setup.QualityThreshold &&
                     !candidate.IsHumanPriority)))
        {
            throw new ArgumentException(
                "Quality-first results cannot include below-target or diversity-relaxed candidates.",
                nameof(selectedCandidates));
        }

        int safeCandidateCount =
            sourceSnapshot.Sum(
                static source =>
                    source.Moments.Proposals.Count(
                        static candidate =>
                            candidate.Disposition is not
                                (MomentCandidateDisposition.RejectedBlack or
                                 MomentCandidateDisposition.RejectedFreeze)));
        if (request.Setup.ClipFulfillmentPreference ==
                ClipFulfillmentPreference.FillRequestedCount &&
            safeCandidateCount >=
                request.Setup.DesiredResultCount &&
            selectedSnapshot.Length !=
                request.Setup.DesiredResultCount)
        {
            throw new ArgumentException(
                "Fill-count results must meet the requested count whenever enough safe candidates exist.",
                nameof(selectedCandidates));
        }

        Request = request;
        _sources = Array.AsReadOnly(sourceSnapshot);
        _selectedCandidates =
            Array.AsReadOnly(selectedSnapshot);
        BelowQualityTargetCount =
            belowQualityTargetCount;
        DiversityRelaxedCount =
            diversityRelaxedCount;
        HumanPriorityCount = humanPriorityCount;
        SafeCandidateCount = safeCandidateCount;
        FulfillmentOutcome = DetermineFulfillmentOutcome(
            request,
            selectedSnapshot.Length,
            belowQualityTargetCount,
            diversityRelaxedCount);
        ReferenceSource =
            _sources.Single(
                source =>
                    ReferenceEquals(
                        source.AnalyzedSource,
                        request.ReferenceSource));
    }

    public GenerationMomentFindingRequest Request { get; }
    public IReadOnlyList<GenerationSourceMomentResult> Sources => _sources;
    public GenerationSourceMomentResult ReferenceSource { get; }
    public IReadOnlyList<GenerationMomentCandidate> SelectedCandidates =>
        _selectedCandidates;
    public int RequestedCount => Request.Setup.DesiredResultCount;
    public bool IsAutomaticResultCount =>
        Request.Setup.IsAutomaticResultCount;
    public int SelectedCount => _selectedCandidates.Count;
    public int SafeCandidateCount { get; }
    public int BelowQualityTargetCount { get; }
    public int DiversityRelaxedCount { get; }
    public int HumanPriorityCount { get; }
    public bool IsRequestedCountMet =>
        IsAutomaticResultCount ||
        SelectedCount == RequestedCount;
    public GenerationClipFulfillmentOutcome FulfillmentOutcome { get; }

    public string FulfillmentMessage =>
        FulfillmentOutcome switch
        {
            GenerationClipFulfillmentOutcome
                .RequestedCountMetAtQualityTarget =>
                $"Found all {RequestedCount} requested moments at the quality target.",
            GenerationClipFulfillmentOutcome
                .RequestedCountMetWithLowerQuality =>
                HumanPriorityCount > 0
                    ? $"Found all {RequestedCount} requested moments; " +
                      $"{HumanPriorityCount} came from your priority marks and " +
                      $"{BelowQualityTargetCount} were below the automatic quality target."
                    : $"Found all {RequestedCount} requested moments, including {BelowQualityTargetCount} below the quality target.",
            GenerationClipFulfillmentOutcome
                .RequestedCountMetWithDiversityRelaxation =>
                $"Found all {RequestedCount} requested moments; {DiversityRelaxedCount} similar moments were included as a last resort.",
            GenerationClipFulfillmentOutcome
                .QualityFirstShortfall =>
                $"Found {SelectedCount} of {RequestedCount} requested moments without lowering the quality target.",
            GenerationClipFulfillmentOutcome
                .InsufficientSafeCandidates =>
                $"Found {SelectedCount} of {RequestedCount} requested moments because only {SafeCandidateCount} safe candidates were available.",
            GenerationClipFulfillmentOutcome
                .AutomaticQualityMatches =>
                $"Auto found {SelectedCount} distinct moments at or above the {Request.Setup.QualityThreshold:0}% quality target, within the 30-clip cap.",
            _ => throw new InvalidOperationException(
                "The clip-fulfillment outcome is not supported."),
        };

    private static GenerationClipFulfillmentOutcome
        DetermineFulfillmentOutcome(
            GenerationMomentFindingRequest request,
            int selectedCount,
            int belowQualityTargetCount,
            int diversityRelaxedCount)
    {
        if (request.Setup.IsAutomaticResultCount)
        {
            return GenerationClipFulfillmentOutcome
                .AutomaticQualityMatches;
        }
        if (selectedCount < request.Setup.DesiredResultCount)
        {
            return request.Setup.ClipFulfillmentPreference ==
                ClipFulfillmentPreference.QualityFirst
                ? GenerationClipFulfillmentOutcome.QualityFirstShortfall
                : GenerationClipFulfillmentOutcome.InsufficientSafeCandidates;
        }

        if (diversityRelaxedCount > 0)
        {
            return GenerationClipFulfillmentOutcome
                .RequestedCountMetWithDiversityRelaxation;
        }

        return belowQualityTargetCount > 0
            ? GenerationClipFulfillmentOutcome
                .RequestedCountMetWithLowerQuality
            : GenerationClipFulfillmentOutcome
                .RequestedCountMetAtQualityTarget;
    }
}
