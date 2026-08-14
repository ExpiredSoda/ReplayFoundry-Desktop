using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Moments;

public enum GenerationCandidateSelectionReason
{
    UserReservedRange,
    UserPriority,
    QualityQualified,
    CountFillBelowQualityTarget,
    CountFillRelaxedDiversity,
    HiddenMomentRecovery,
}

public sealed class GenerationMomentCandidate
{
    public GenerationMomentCandidate(
        string id,
        AnalyzedGenerationSource analyzedSource,
        MomentCandidate candidate,
        int sourceOrder,
        int globalRank,
        GenerationCandidateSelectionReason selectionReason =
            GenerationCandidateSelectionReason.QualityQualified,
        GenerationCandidateRefinement? refinement = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A generation moment candidate requires a stable identifier.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(analyzedSource);
        ArgumentNullException.ThrowIfNull(candidate);

        if (sourceOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceOrder));
        }

        if (globalRank <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(globalRank));
        }

        if (!Enum.IsDefined(selectionReason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectionReason));
        }

        Id = id.Trim();
        AnalyzedSource = analyzedSource;
        Candidate = candidate;
        SourceOrder = sourceOrder;
        GlobalRank = globalRank;
        SelectionReason = selectionReason;
        if (refinement is not null &&
            !ReferenceEquals(refinement.Candidate, candidate))
        {
            throw new ArgumentException(
                "Candidate refinement must describe the selected candidate.",
                nameof(refinement));
        }
        Refinement = refinement;
    }

    public string Id { get; }
    public AnalyzedGenerationSource AnalyzedSource { get; }
    public MomentCandidate Candidate { get; }
    public int SourceOrder { get; }
    public int GlobalRank { get; }
    public GenerationCandidateSelectionReason SelectionReason { get; }
    public GenerationCandidateRefinement? Refinement { get; }
    public double FinalScore =>
        Refinement?.FinalScore ?? Candidate.HeuristicScore;
    public bool RequiredDiversityRelaxation =>
        SelectionReason ==
        GenerationCandidateSelectionReason.CountFillRelaxedDiversity;
    public bool IsHumanPriority =>
        SelectionReason is
            GenerationCandidateSelectionReason.UserReservedRange or
            GenerationCandidateSelectionReason.UserPriority;
}
