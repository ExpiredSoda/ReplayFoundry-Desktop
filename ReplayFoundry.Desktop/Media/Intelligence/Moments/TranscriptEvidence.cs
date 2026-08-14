using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Media.Intelligence.Moments;

public enum TranscriptEvidenceStatus
{
    LexicalText,
    NonSpeechTokenOnly,
    EmptyProviderOutput,
}

public enum TranscriptEvidenceFlagCode
{
    LexicalTextPresent,
    NonSpeechTokenOnly,
    EmptyProviderOutput,
    SegmentTimestampsOnly,
    WordTimestampsUnavailable,
    BoundaryClamped,
    LanguageReported,
    LanguageUnavailable,
    ProviderWarningPresent,
    PotentialSpeechOmissionUnknown,
    HumanReferenceAvailable,
    ReviewedSilence,
}

public sealed record TranscriptEvidenceFlag
{
    public TranscriptEvidenceFlag(
        TranscriptEvidenceFlagCode code,
        string explanation,
        string? segmentId = null)
    {
        if (!Enum.IsDefined(code) ||
            string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException(
                "Transcript evidence flags require a defined code and explanation.");
        }

        Code = code;
        Explanation = explanation.Trim();
        SegmentId =
            string.IsNullOrWhiteSpace(segmentId)
                ? null
                : segmentId.Trim();
    }

    public TranscriptEvidenceFlagCode Code { get; }

    public string Explanation { get; }

    public string? SegmentId { get; }
}

public sealed class TranscriptEvidenceAssessment
{
    private readonly ReadOnlyCollection<TranscriptEvidenceFlag>
        _flags;

    public TranscriptEvidenceAssessment(
        TranscriptEvidenceStatus status,
        IEnumerable<TranscriptEvidenceFlag> flags)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentNullException.ThrowIfNull(flags);

        TranscriptEvidenceFlag[] snapshot =
            flags
                .OrderBy(static flag => flag.Code)
                .ThenBy(static flag => flag.SegmentId, StringComparer.Ordinal)
                .ThenBy(static flag => flag.Explanation, StringComparer.Ordinal)
                .ToArray();

        if (snapshot.Any(static flag => flag is null) ||
            snapshot
                .GroupBy(
                    static flag => (flag.Code, flag.SegmentId),
                    new FlagIdentityComparer())
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Transcript evidence flags must be non-null and unique by code and segment.",
                nameof(flags));
        }

        TranscriptEvidenceFlagCode required =
            status switch
            {
                TranscriptEvidenceStatus.LexicalText =>
                    TranscriptEvidenceFlagCode.LexicalTextPresent,
                TranscriptEvidenceStatus.NonSpeechTokenOnly =>
                    TranscriptEvidenceFlagCode.NonSpeechTokenOnly,
                TranscriptEvidenceStatus.EmptyProviderOutput =>
                    TranscriptEvidenceFlagCode.EmptyProviderOutput,
                _ => throw new ArgumentOutOfRangeException(nameof(status)),
            };

        if (!snapshot.Any(flag => flag.Code == required) ||
            snapshot.Count(
                flag =>
                    flag.Code is
                        TranscriptEvidenceFlagCode.LexicalTextPresent or
                        TranscriptEvidenceFlagCode.NonSpeechTokenOnly or
                        TranscriptEvidenceFlagCode.EmptyProviderOutput) != 1)
        {
            throw new ArgumentException(
                "Transcript evidence status must have exactly one matching output-state flag.",
                nameof(flags));
        }

        Status = status;
        _flags = Array.AsReadOnly(snapshot);
    }

    public TranscriptEvidenceStatus Status { get; }

    public IReadOnlyList<TranscriptEvidenceFlag> Flags => _flags;

    public static TranscriptEvidenceAssessment FromProviderResult(
        IReadOnlyList<AudioTranscriptionSegment> segments,
        AudioTranscriptionLanguage? detectedLanguage,
        IReadOnlyList<AudioTranscriptionWarning> resultWarnings,
        bool wordTimestampsRequested,
        bool humanReferenceAvailable = false,
        bool reviewedSilence = false)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(resultWarnings);

        int lexicalCount =
            segments.Count(
                segment =>
                    TranscriptTextClassifier.Classify(segment.Text) ==
                    TranscriptTextKind.Lexical);
        int nonSpeechCount =
            segments.Count(
                segment =>
                    TranscriptTextClassifier.Classify(segment.Text) ==
                    TranscriptTextKind.NonSpeechToken);

        TranscriptEvidenceStatus status =
            lexicalCount > 0
                ? TranscriptEvidenceStatus.LexicalText
                : nonSpeechCount > 0
                    ? TranscriptEvidenceStatus.NonSpeechTokenOnly
                    : TranscriptEvidenceStatus.EmptyProviderOutput;

        var flags = new List<TranscriptEvidenceFlag>
        {
            new(
                status switch
                {
                    TranscriptEvidenceStatus.LexicalText =>
                        TranscriptEvidenceFlagCode.LexicalTextPresent,
                    TranscriptEvidenceStatus.NonSpeechTokenOnly =>
                        TranscriptEvidenceFlagCode.NonSpeechTokenOnly,
                    _ => TranscriptEvidenceFlagCode.EmptyProviderOutput,
                },
                status switch
                {
                    TranscriptEvidenceStatus.LexicalText =>
                        "Provider output contains lexical text; this is not proof that the text is correct.",
                    TranscriptEvidenceStatus.NonSpeechTokenOnly =>
                        "Provider output contains only bracketed or parenthesized non-speech labels.",
                    _ =>
                        "The provider returned no segments; this is not proof that the interval contains no speech.",
                }),
            new(
                TranscriptEvidenceFlagCode.SegmentTimestampsOnly,
                "Timing is limited to approximate provider segments."),
            new(
                TranscriptEvidenceFlagCode.PotentialSpeechOmissionUnknown,
                "Retained provider output cannot establish whether speech was omitted."),
        };

        if (!wordTimestampsRequested)
        {
            flags.Add(
                new TranscriptEvidenceFlag(
                    TranscriptEvidenceFlagCode.WordTimestampsUnavailable,
                    "The locked configuration did not request word timestamps."));
        }

        flags.Add(
            detectedLanguage is null
                ? new TranscriptEvidenceFlag(
                    TranscriptEvidenceFlagCode.LanguageUnavailable,
                    "The provider did not report a language.")
                : new TranscriptEvidenceFlag(
                    TranscriptEvidenceFlagCode.LanguageReported,
                    $"The provider reported language '{detectedLanguage.Code}'."));

        foreach (AudioTranscriptionSegment segment in segments)
        {
            foreach (AudioTranscriptionWarning warning in segment.Warnings)
            {
                if (warning.Code == AudioTranscriptionWarningCode.BoundaryClamped)
                {
                    flags.Add(
                        new TranscriptEvidenceFlag(
                            TranscriptEvidenceFlagCode.BoundaryClamped,
                            warning.Message,
                            segment.Id));
                }
                else
                {
                    flags.Add(
                        new TranscriptEvidenceFlag(
                            TranscriptEvidenceFlagCode.ProviderWarningPresent,
                            warning.Message,
                            segment.Id));
                }
            }
        }

        foreach (AudioTranscriptionWarning warning in resultWarnings)
        {
            flags.Add(
                new TranscriptEvidenceFlag(
                    warning.Code == AudioTranscriptionWarningCode.BoundaryClamped
                        ? TranscriptEvidenceFlagCode.BoundaryClamped
                        : TranscriptEvidenceFlagCode.ProviderWarningPresent,
                    warning.Message,
                    warning.SegmentId));
        }

        if (humanReferenceAvailable)
        {
            flags.Add(
                new TranscriptEvidenceFlag(
                    TranscriptEvidenceFlagCode.HumanReferenceAvailable,
                    "A separate human-reviewed reference is available for developer evaluation."));
        }

        if (reviewedSilence)
        {
            flags.Add(
                new TranscriptEvidenceFlag(
                    TranscriptEvidenceFlagCode.ReviewedSilence,
                    "A human reviewer marked the complete bounded case as speech-free."));
        }

        return new TranscriptEvidenceAssessment(status, flags);
    }

    private sealed class FlagIdentityComparer :
        IEqualityComparer<(TranscriptEvidenceFlagCode Code, string? SegmentId)>
    {
        public bool Equals(
            (TranscriptEvidenceFlagCode Code, string? SegmentId) x,
            (TranscriptEvidenceFlagCode Code, string? SegmentId) y) =>
            x.Code == y.Code &&
            string.Equals(x.SegmentId, y.SegmentId, StringComparison.Ordinal);

        public int GetHashCode(
            (TranscriptEvidenceFlagCode Code, string? SegmentId) obj) =>
            HashCode.Combine(
                obj.Code,
                obj.SegmentId is null
                    ? 0
                    : StringComparer.Ordinal.GetHashCode(obj.SegmentId));
    }
}

internal enum TranscriptTextKind
{
    Lexical,
    NonSpeechToken,
}

internal static class TranscriptTextClassifier
{
    public static TranscriptTextKind Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Transcript text cannot be blank.",
                nameof(text));
        }

        string value = text.Trim();
        bool wrapped =
            value.Length >= 2 &&
            (
                value[0] == '[' && value[^1] == ']' ||
                value[0] == '(' && value[^1] == ')'
            );

        return wrapped
            ? TranscriptTextKind.NonSpeechToken
            : TranscriptTextKind.Lexical;
    }
}
