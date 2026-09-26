using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed class VisualSemanticExecutionTimingManifest
{
    public const string SupportedSchemaVersion =
        "visual-semantic-execution-timing-1.0";
    public const string SupportedCoveragePolicyVersion =
        "candidate-sampling-coverage-1.0";
    public const string SupportedTimingSource =
        "TorchCodecFrameBatchActualPtsAndDuration";

    private readonly ReadOnlyCollection<VisualSemanticCaseExecutionTiming>
        _cases;

    internal VisualSemanticExecutionTimingManifest(
        VisualSemanticExecutionTimingCoveragePolicy coveragePolicy,
        VisualSemanticExecutionTimingSource timingSource,
        IEnumerable<VisualSemanticCaseExecutionTiming> cases,
        string canonicalExecutionTimingSha256)
    {
        ArgumentNullException.ThrowIfNull(coveragePolicy);
        ArgumentNullException.ThrowIfNull(cases);
        VisualSemanticCaseExecutionTiming[] caseSnapshot = cases.ToArray();

        if (caseSnapshot.Length == 0 ||
            caseSnapshot.Any(static value => value is null) ||
            caseSnapshot
                .Select(static value => value.CaseOrdinal)
                .Where((ordinal, index) => ordinal != index + 1)
                .Any() ||
            caseSnapshot
                .GroupBy(static value => value.CaseId, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            caseSnapshot
                .GroupBy(static value => value.CandidateId, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            !Enum.IsDefined(timingSource))
        {
            throw new ArgumentException(
                "Execution timing must preserve one ordered, unique timing case per provider request.",
                nameof(cases));
        }

        CoveragePolicy = coveragePolicy;
        TimingSource = timingSource;
        _cases = Array.AsReadOnly(caseSnapshot);
        CanonicalExecutionTimingSha256 = ModelArtifactManifest.Sha256Value(
            canonicalExecutionTimingSha256,
            nameof(canonicalExecutionTimingSha256));
    }

    public string SchemaVersion => SupportedSchemaVersion;
    public VisualSemanticExecutionTimingCoveragePolicy CoveragePolicy { get; }
    public VisualSemanticExecutionTimingSource TimingSource { get; }
    public int CaseCount => _cases.Count;
    public IReadOnlyList<VisualSemanticCaseExecutionTiming> Cases => _cases;
    public string CanonicalExecutionTimingSha256 { get; }
}
