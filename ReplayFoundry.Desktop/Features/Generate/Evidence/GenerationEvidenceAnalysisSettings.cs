using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Summaries;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

public sealed class GenerationEvidenceAnalysisSettings
{
    public const string CurrentPolicyVersion = "1.0";

    private readonly ReadOnlyCollection<CompositionRegionRole>
        _includedRegionRoles;

    public GenerationEvidenceAnalysisSettings(
        MediaEvidenceAnalysisOptions options,
        IEnumerable<CompositionRegionRole> includedRegionRoles,
        MediaEvidenceSummaryOptions summaryOptions,
        string policyVersion = CurrentPolicyVersion)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(includedRegionRoles);
        ArgumentNullException.ThrowIfNull(summaryOptions);

        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException(
                "Evidence-analysis settings require a policy version.",
                nameof(policyVersion));
        }

        CompositionRegionRole[] roleSnapshot =
            includedRegionRoles
                .OrderBy(static role => role)
                .ToArray();

        if (roleSnapshot.Any(
                static role =>
                    !Enum.IsDefined(role)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(includedRegionRoles),
                "Included evidence roles must be defined values.");
        }

        if (roleSnapshot.Distinct().Count() !=
            roleSnapshot.Length)
        {
            throw new ArgumentException(
                "Included evidence roles must be unique.",
                nameof(includedRegionRoles));
        }

        if (!roleSnapshot.Contains(
                CompositionRegionRole.Gameplay))
        {
            throw new ArgumentException(
                "Desktop evidence analysis requires the Gameplay role.",
                nameof(includedRegionRoles));
        }

        Options =
            new MediaEvidenceAnalysisOptions(
                options.SceneThresholdPercent,
                options.MinimumBlackDuration,
                options.BlackPixelThreshold,
                options.BlackPictureRatio,
                options.MinimumFreezeDuration,
                options.FreezeNoiseToleranceDb,
                options.MinimumSilenceDuration,
                options.SilenceNoiseThresholdDb,
                options.ProcessTimeout,
                options.VisualSignalSampleInterval,
                options.AudioSignalWindowDuration);

        SummaryOptions =
            new MediaEvidenceSummaryOptions(
                summaryOptions.SceneClusterMaximumGap,
                summaryOptions.SceneDensityBucketDuration,
                summaryOptions.SilenceMergeTolerance,
                summaryOptions.ShortSilenceMaximum,
                summaryOptions.LongSilenceMinimum,
                summaryOptions.DarkLumaThreshold,
                summaryOptions.BrightLumaThreshold,
                summaryOptions.SignalSummaryPolicyVersion);

        _includedRegionRoles =
            Array.AsReadOnly(roleSnapshot);

        PolicyVersion = policyVersion.Trim();
    }

    public MediaEvidenceAnalysisOptions Options { get; }

    public IReadOnlyList<CompositionRegionRole>
        IncludedRegionRoles =>
        _includedRegionRoles;

    public MediaEvidenceSummaryOptions SummaryOptions { get; }

    public string PolicyVersion { get; }

    public static GenerationEvidenceAnalysisSettings
        CreateDefault()
        => CreateForDepth(GenerationAnalysisDepth.Balanced);

    public static GenerationEvidenceAnalysisSettings CreateForDepth(
        GenerationAnalysisDepth depth)
    {
        if (!Enum.IsDefined(depth))
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        TimeSpan cadence = depth switch
        {
            GenerationAnalysisDepth.Fast => TimeSpan.FromSeconds(2),
            GenerationAnalysisDepth.Balanced => TimeSpan.FromSeconds(1),
            GenerationAnalysisDepth.Thorough =>
                MediaEvidenceAnalysisOptions
                    .DefaultVisualSignalSampleInterval,
            _ => throw new ArgumentOutOfRangeException(nameof(depth)),
        };
        MediaEvidenceAnalysisOptions options =
            MediaEvidenceAnalysisOptions
                .CreateFullPrecisionDefaults()
                .WithSignalSampling(cadence, cadence);
        CompositionRegionRole[] roles = depth ==
            GenerationAnalysisDepth.Fast
            ? [CompositionRegionRole.Gameplay]
            :
            [
                CompositionRegionRole.Gameplay,
                CompositionRegionRole.Presenter,
            ];
        return new GenerationEvidenceAnalysisSettings(
            options,
            roles,
            MediaEvidenceSummaryOptions.CreateDefault(),
            policyVersion: $"{CurrentPolicyVersion}-{depth.ToString().ToLowerInvariant()}");
    }
}
