using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Moments;

public sealed class GenerationMomentFindingSettings
{
    public const string CurrentPolicyVersion = "1.0";

    public GenerationMomentFindingSettings(
        MediaMomentFindingOptions options,
        string policyVersion = CurrentPolicyVersion)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException(
                "Generation moment-finding settings require a policy version.",
                nameof(policyVersion));
        }

        Options = options;
        PolicyVersion = policyVersion.Trim();
    }

    public MediaMomentFindingOptions Options { get; }
    public string PolicyVersion { get; }

    public static GenerationMomentFindingSettings FromSetup(
        GenerationSetupOptions setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        MomentOutputKind outputKind =
            setup.Mode switch
            {
                GenerationMode.IndividualClips =>
                    MomentOutputKind.StandaloneClip,
                GenerationMode.Montage =>
                    MomentOutputKind.MontageSegment,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(setup)),
            };

        MomentContentEmphasis emphasis =
            setup.ContentEmphasis switch
            {
                ContentEmphasis.GameplayFocused =>
                    MomentContentEmphasis.GameplayFocused,
                ContentEmphasis.Balanced =>
                    MomentContentEmphasis.Balanced,
                ContentEmphasis.CommentaryFocused =>
                    MomentContentEmphasis.CommentaryFocused,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(setup)),
            };

        MediaMomentFindingOptions defaults =
            MediaMomentFindingOptions.CreateDefaults(
                outputKind,
                emphasis,
                setup.DesiredResultCount,
                setup.QualityThreshold);

        return new GenerationMomentFindingSettings(
            defaults.WithMaximumDuration(
                setup.MaximumClipDuration));
    }
}
