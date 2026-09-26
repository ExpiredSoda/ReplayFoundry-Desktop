using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed record VisualSemanticTranscriptSpan
{
    public VisualSemanticTranscriptSpan(
        string id,
        string text,
        TimeSpan reviewRelativeStart,
        TimeSpan reviewRelativeEnd,
        bool isNonSpeech,
        TranscriptTimingPrecision timingPrecision)
    {
        if (reviewRelativeStart < TimeSpan.Zero ||
            reviewRelativeEnd < reviewRelativeStart ||
            !Enum.IsDefined(timingPrecision))
        {
            throw new ArgumentException(
                "Visual-semantic transcript spans require defined provenance and ordered review-relative timestamps.");
        }

        Id = VisualSemanticContractText.Required(
            id,
            nameof(id),
            128);
        Text = VisualSemanticContractText.Required(
            text,
            nameof(text),
            1000);
        ReviewRelativeStart = reviewRelativeStart;
        ReviewRelativeEnd = reviewRelativeEnd;
        IsNonSpeech = isNonSpeech;
        TimingPrecision = timingPrecision;
    }

    public string Id { get; }

    public string Text { get; }

    public TimeSpan ReviewRelativeStart { get; }

    public TimeSpan ReviewRelativeEnd { get; }

    public bool IsNonSpeech { get; }

    public TranscriptTimingPrecision TimingPrecision { get; }

    public static VisualSemanticTranscriptSpan FromTranscriptSpan(
        TranscriptSpan span,
        TimeSpan reviewAbsoluteStart)
    {
        ArgumentNullException.ThrowIfNull(span);

        if (reviewAbsoluteStart < TimeSpan.Zero ||
            span.AbsoluteSourceStart < reviewAbsoluteStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reviewAbsoluteStart),
                "The transcript span must begin inside the bounded review media.");
        }

        return new VisualSemanticTranscriptSpan(
            span.Id,
            span.Text,
            span.AbsoluteSourceStart - reviewAbsoluteStart,
            span.AbsoluteSourceEnd - reviewAbsoluteStart,
            TranscriptTextClassifier.Classify(span.Text) ==
                TranscriptTextKind.NonSpeechToken,
            span.TimingPrecision);
    }
}

public sealed class VisualSemanticTranscriptContext
{
    private readonly ReadOnlyCollection<VisualSemanticTranscriptSpan> _spans;

    public VisualSemanticTranscriptContext(
        VisualSemanticTranscriptContextPolicy policy,
        TranscriptEvidenceStatus? evidenceStatus,
        IEnumerable<VisualSemanticTranscriptSpan>? spans,
        string transcriptAccuracyWarning)
    {
        if (!Enum.IsDefined(policy) ||
            evidenceStatus.HasValue &&
            !Enum.IsDefined(evidenceStatus.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        VisualSemanticTranscriptSpan[] spanSnapshot =
            spans?
                .OrderBy(static value => value.ReviewRelativeStart)
                .ThenBy(static value => value.ReviewRelativeEnd)
                .ThenBy(static value => value.Id, StringComparer.Ordinal)
                .ToArray() ??
            [];

        if (spanSnapshot.Any(static value => value is null) ||
            spanSnapshot
                .GroupBy(static value => value.Id, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Visual-semantic transcript spans must be non-null and unique by ID.",
                nameof(spans));
        }

        if (policy == VisualSemanticTranscriptContextPolicy.VisualOnlyV1 &&
            (
                evidenceStatus.HasValue ||
                spanSnapshot.Length != 0
            ))
        {
            throw new ArgumentException(
                "VisualOnlyV1 cannot retain transcript evidence.");
        }

        if (policy == VisualSemanticTranscriptContextPolicy.FullContextV1 &&
            !evidenceStatus.HasValue)
        {
            throw new ArgumentException(
                "FullContextV1 requires an explicit transcript evidence status.",
                nameof(evidenceStatus));
        }

        if (evidenceStatus == TranscriptEvidenceStatus.LexicalText &&
            !spanSnapshot.Any(static value => !value.IsNonSpeech) ||
            evidenceStatus == TranscriptEvidenceStatus.NonSpeechTokenOnly &&
            (
                spanSnapshot.Length == 0 ||
                spanSnapshot.Any(static value => !value.IsNonSpeech)
            ) ||
            evidenceStatus == TranscriptEvidenceStatus.EmptyProviderOutput &&
            spanSnapshot.Length != 0)
        {
            throw new ArgumentException(
                "Transcript content must match its evidence status.");
        }

        Policy = policy;
        EvidenceStatus = evidenceStatus;
        TranscriptAccuracyWarning =
            VisualSemanticContractText.Required(
                transcriptAccuracyWarning,
                nameof(transcriptAccuracyWarning),
                300);
        _spans = Array.AsReadOnly(spanSnapshot);
    }

    public VisualSemanticTranscriptContextPolicy Policy { get; }

    public TranscriptEvidenceStatus? EvidenceStatus { get; }

    public IReadOnlyList<VisualSemanticTranscriptSpan> Spans => _spans;

    public string TranscriptAccuracyWarning { get; }

    public bool TranscriptSupplied =>
        Policy == VisualSemanticTranscriptContextPolicy.FullContextV1;
}
