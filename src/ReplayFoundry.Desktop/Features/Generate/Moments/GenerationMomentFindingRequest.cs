using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Features.Generate.Moments;

public sealed class GenerationMomentFindingRequest
{
    public GenerationMomentFindingRequest(
        GenerationEvidenceAnalysisResult evidenceAnalysis,
        GenerationSetupOptions setup,
        GenerationMomentFindingSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(evidenceAnalysis);
        ArgumentNullException.ThrowIfNull(setup);

        if (setup.DetectionMethod !=
            DetectionMethod.Heuristics)
        {
            throw new ArgumentException(
                "Deterministic moment finding currently supports Heuristics detection only.",
                nameof(setup));
        }

        GenerationMomentFindingSettings actualSettings =
            settings ??
            GenerationMomentFindingSettings.FromSetup(setup);

        GenerationMomentFindingSettings expectedSettings =
            GenerationMomentFindingSettings.FromSetup(setup);

        if (actualSettings.Options.DesiredCandidateCount !=
                setup.DesiredResultCount ||
            actualSettings.Options.MinimumHeuristicScore !=
                setup.QualityThreshold ||
            actualSettings.Options.OutputKind !=
                expectedSettings.Options.OutputKind ||
            actualSettings.Options.ContentEmphasis !=
                expectedSettings.Options.ContentEmphasis ||
            actualSettings.Options.MinimumDuration !=
                expectedSettings.Options.MinimumDuration ||
            actualSettings.Options.TargetDuration !=
                expectedSettings.Options.TargetDuration ||
            actualSettings.Options.MaximumDuration !=
                expectedSettings.Options.MaximumDuration)
        {
            throw new ArgumentException(
                "Moment settings must preserve the setup mode, emphasis, exact count, quality threshold, and duration policy.",
                nameof(settings));
        }

        EvidenceAnalysis = evidenceAnalysis;
        Setup = setup;
        Settings = actualSettings;
    }

    public GenerationEvidenceAnalysisResult EvidenceAnalysis { get; }
    public GenerationSetupOptions Setup { get; }
    public GenerationMomentFindingSettings Settings { get; }
    public IReadOnlyList<AnalyzedGenerationSource> Sources =>
        EvidenceAnalysis.Sources;
    public AnalyzedGenerationSource ReferenceSource =>
        EvidenceAnalysis.ReferenceSource;
    public int SourceCount => Sources.Count;
}
