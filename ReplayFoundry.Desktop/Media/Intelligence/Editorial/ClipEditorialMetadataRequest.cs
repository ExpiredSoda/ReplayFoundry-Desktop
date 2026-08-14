using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

public sealed class ClipEditorialMetadataRequest
{
    private readonly ReadOnlyCollection<ClipEditorialPriorTitleExclusion>
        _priorAcceptedTitleExclusions;

    public ClipEditorialMetadataRequest(
        ClipEditorialContext context,
        ClipEditorialProfile profile,
        int attempt,
        ClipEditorialGenerationPreference preference =
            ClipEditorialGenerationPreference.HeuristicOnly,
        MediaProbeResult? sourceMedia = null,
        VisualSemanticInputManifest? reviewVideo = null,
        IEnumerable<ClipEditorialPriorTitleExclusion>?
            priorAcceptedTitleExclusions = null,
        TimeSpan? reviewFocusSourceTimestamp = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        if (attempt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }
        if (!Enum.IsDefined(preference))
        {
            throw new ArgumentOutOfRangeException(nameof(preference));
        }
        if (sourceMedia is not null &&
            (!sourceMedia.FullPath.Equals(
                context.SourceFullPath,
                StringComparison.OrdinalIgnoreCase) ||
             sourceMedia.Duration != context.SourceDuration))
        {
            throw new ArgumentException(
                "Editorial source media must match the retained clip context.",
                nameof(sourceMedia));
        }
        if (reviewVideo is not null && sourceMedia is null)
        {
            throw new ArgumentException(
                "A bounded editorial review video requires matching source media.",
                nameof(reviewVideo));
        }
        if (reviewFocusSourceTimestamp is TimeSpan focus &&
            (sourceMedia is null ||
             focus < context.SourceStart ||
             focus >= context.SourceEnd))
        {
            throw new ArgumentException(
                "An editorial review focus must fall inside the exact retained candidate cut.",
                nameof(reviewFocusSourceTimestamp));
        }

        ClipEditorialPriorTitleExclusion[] exclusions =
            priorAcceptedTitleExclusions?.ToArray() ?? [];
        if (exclusions.Length >
                ClipEditorialPriorTitleExclusion.MaximumRetainedTitles ||
            exclusions.Any(static value => value is null) ||
            exclusions.Any(value =>
                !value.CandidateId.Equals(
                    context.CandidateId,
                    StringComparison.Ordinal) ||
                value.SourceStart != context.SourceStart ||
                value.SourceEnd != context.SourceEnd))
        {
            throw new ArgumentException(
                "Prior editorial titles must belong to this exact candidate cut and remain bounded.",
                nameof(priorAcceptedTitleExclusions));
        }
        exclusions = exclusions
            .DistinctBy(static value => value.Title,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Context = context;
        Profile = profile;
        Attempt = attempt;
        Preference = preference;
        SourceMedia = sourceMedia;
        ReviewVideo = reviewVideo;
        ReviewFocusSourceTimestamp = reviewFocusSourceTimestamp;
        _priorAcceptedTitleExclusions = Array.AsReadOnly(exclusions);
        VariantIntent = ResolveVariantIntent(context, attempt);
    }

    public ClipEditorialContext Context { get; }

    public ClipEditorialProfile Profile { get; }

    public int Attempt { get; }

    public ClipEditorialGenerationPreference Preference { get; }

    public MediaProbeResult? SourceMedia { get; }

    public VisualSemanticInputManifest? ReviewVideo { get; }

    public TimeSpan? ReviewFocusSourceTimestamp { get; }

    public IReadOnlyList<ClipEditorialPriorTitleExclusion>
        PriorAcceptedTitleExclusions => _priorAcceptedTitleExclusions;

    public ClipEditorialVariantIntent VariantIntent { get; }

    public ClipEditorialMetadataRequest WithReviewVideo(
        VisualSemanticInputManifest reviewVideo)
    {
        ArgumentNullException.ThrowIfNull(reviewVideo);
        if (SourceMedia is null)
        {
            throw new InvalidOperationException(
                "Editorial visual review requires retained source inspection.");
        }

        return new ClipEditorialMetadataRequest(
            Context,
            Profile,
            Attempt,
            Preference,
            SourceMedia,
            reviewVideo,
            PriorAcceptedTitleExclusions,
            ReviewFocusSourceTimestamp);
    }

    public ClipEditorialMetadataRequest WithAttempt(int attempt) =>
        new(
            Context,
            Profile,
            attempt,
            Preference,
            SourceMedia,
            ReviewVideo,
            PriorAcceptedTitleExclusions,
            ReviewFocusSourceTimestamp);

    public ClipEditorialMetadataRequest WithPriorAcceptedTitleExclusions(
        IEnumerable<ClipEditorialPriorTitleExclusion> exclusions)
    {
        ArgumentNullException.ThrowIfNull(exclusions);
        return new(
            Context,
            Profile,
            Attempt,
            Preference,
            SourceMedia,
            ReviewVideo,
            exclusions,
            ReviewFocusSourceTimestamp);
    }

    private static ClipEditorialVariantIntent ResolveVariantIntent(
        ClipEditorialContext context,
        int attempt)
    {
        bool hasReviewedSpeech = context.Transcripts.Any(
            static transcript =>
                transcript.MaySupportVerbatimAudienceCopy);
        if (!hasReviewedSpeech)
        {
            return (attempt % 4) switch
            {
                0 => ClipEditorialVariantIntent.DirectAction,
                1 => ClipEditorialVariantIntent.SpecificCuriosity,
                2 => ClipEditorialVariantIntent.OutcomeFocused,
                _ => ClipEditorialVariantIntent.ConcreteDetail,
            };
        }

        return (attempt % 5) switch
        {
            0 => ClipEditorialVariantIntent.DirectAction,
            1 => ClipEditorialVariantIntent.SpecificCuriosity,
            2 => ClipEditorialVariantIntent.OutcomeFocused,
            3 => ClipEditorialVariantIntent.ConcreteDetail,
            _ => ClipEditorialVariantIntent.CommentaryLed,
        };
    }
}
