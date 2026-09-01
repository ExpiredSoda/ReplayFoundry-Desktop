using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Enrichment;

public static class CandidateNeighborhoodPlanner
{
    public static CandidateNeighborhoodPlan Plan(
        MomentEnrichmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        MomentEnrichmentCandidateSnapshot[] candidates =
            SelectCandidates(request)
                .ToArray();

        var expanded =
            new List<ExpandedCandidate>(
                candidates.Length);

        foreach (MomentEnrichmentCandidateSnapshot candidate in
                 candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan start =
                candidate.Start >
                request.Options.ContextBefore
                    ? candidate.Start -
                      request.Options.ContextBefore
                    : TimeSpan.Zero;
            TimeSpan end =
                candidate.End +
                    request.Options.ContextAfter <
                request.SourceDuration
                    ? candidate.End +
                      request.Options.ContextAfter
                    : request.SourceDuration;

            if (end - start >
                request.Options.MaximumNeighborhoodDuration)
            {
                throw new InvalidOperationException(
                    $"Candidate '{candidate.CandidateId}' expands to " +
                    $"{end - start:c}, exceeding the configured neighborhood maximum of " +
                    $"{request.Options.MaximumNeighborhoodDuration:c}.");
            }

            expanded.Add(
                new ExpandedCandidate(
                    candidate,
                    start,
                    end));
        }

        expanded.Sort(
            static (left, right) =>
            {
                int start =
                    left.Start.CompareTo(right.Start);

                return start != 0
                    ? start
                    : left.Candidate.SourceOrder.CompareTo(
                        right.Candidate.SourceOrder);
            });

        var neighborhoods =
            new List<CandidateNeighborhood>();
        var group =
            new List<ExpandedCandidate>();
        TimeSpan groupStart = default;
        TimeSpan groupEnd = default;

        foreach (ExpandedCandidate candidate in expanded)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (group.Count == 0)
            {
                group.Add(candidate);
                groupStart = candidate.Start;
                groupEnd = candidate.End;
                continue;
            }

            bool shouldMerge =
                candidate.Start <=
                groupEnd +
                request.Options.MaximumMergeGap;

            if (!shouldMerge)
            {
                neighborhoods.Add(
                    CreateNeighborhood(
                        request,
                        groupStart,
                        groupEnd,
                        group));
                group.Clear();
                group.Add(candidate);
                groupStart = candidate.Start;
                groupEnd = candidate.End;
                continue;
            }

            TimeSpan mergedEnd =
                candidate.End > groupEnd
                    ? candidate.End
                    : groupEnd;

            if (mergedEnd - groupStart >
                request.Options.MaximumNeighborhoodDuration)
            {
                throw new InvalidOperationException(
                    $"Merging candidate '{candidate.Candidate.CandidateId}' " +
                    "would create an overlong transcription neighborhood. " +
                    "Reduce context or the merge gap.");
            }

            group.Add(candidate);
            groupEnd = mergedEnd;
        }

        if (group.Count > 0)
        {
            neighborhoods.Add(
                CreateNeighborhood(
                    request,
                    groupStart,
                    groupEnd,
                    group));
        }

        return new CandidateNeighborhoodPlan(
            request,
            neighborhoods);
    }

    private static IEnumerable<MomentEnrichmentCandidateSnapshot>
        SelectCandidates(
            MomentEnrichmentRequest request)
    {
        IEnumerable<MomentEnrichmentCandidateSnapshot> source =
            request.Options.ProposalSource switch
            {
                MomentEnrichmentProposalSource.SelectedCandidates =>
                    request.Candidates.Where(
                        static candidate =>
                            candidate.IsSelected),
                MomentEnrichmentProposalSource.EligibleTopN =>
                    request.Candidates.Where(IsEligible),
                MomentEnrichmentProposalSource.CompleteProposalPool =>
                    request.Candidates.Where(
                        candidate =>
                            request.Options
                                .IncludeBelowThresholdProposals ||
                            candidate.Disposition !=
                            MomentCandidateDisposition.BelowThreshold),
                _ => throw new InvalidOperationException(
                    "Unsupported enrichment proposal source."),
            };

        return source
            .Take(
                request.Options.MaximumCandidateCount);
    }

    private static bool IsEligible(
        MomentEnrichmentCandidateSnapshot candidate) =>
        candidate.Disposition is not
            MomentCandidateDisposition.RejectedBlack and not
            MomentCandidateDisposition.RejectedFreeze and not
            MomentCandidateDisposition.BelowThreshold;

    private static CandidateNeighborhood CreateNeighborhood(
        MomentEnrichmentRequest request,
        TimeSpan start,
        TimeSpan end,
        IReadOnlyList<ExpandedCandidate> members)
    {
        CandidateNeighborhoodMembership[] memberships =
            members
                .Select(
                    static item =>
                        new CandidateNeighborhoodMembership(
                            item.Candidate.CandidateId,
                            item.Candidate.Start,
                            item.Candidate.End,
                            item.Candidate.SourceOrder))
                .ToArray();

        string identityMaterial =
            string.Join(
                "\u001F",
                request.SourcePath.ToUpperInvariant(),
                start.Ticks,
                end.Ticks,
                string.Join(
                    ",",
                    memberships
                        .OrderBy(
                            static item =>
                                item.CandidateSourceOrder)
                        .Select(
                            static item =>
                                item.CandidateId)));
        string hash =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        identityMaterial)));

        return new CandidateNeighborhood(
            $"n-{hash[..16].ToLowerInvariant()}",
            start,
            end,
            request.SourceDuration,
            memberships);
    }

    private sealed record ExpandedCandidate(
        MomentEnrichmentCandidateSnapshot Candidate,
        TimeSpan Start,
        TimeSpan End);
}
