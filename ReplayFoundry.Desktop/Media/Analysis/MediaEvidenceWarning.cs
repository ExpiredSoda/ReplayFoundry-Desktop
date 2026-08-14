using System;

namespace ReplayFoundry.Desktop.Media.Analysis;

public enum MediaEvidenceWarningCode
{
    InvalidMetadataValue,
    EvidenceOutsideSourceDuration,
    DuplicateSceneBoundary,
    UnmatchedIntervalStart,
    UnmatchedIntervalEnd,
    OverlappingIntervalStart,
    OpenIntervalClosedAtSourceEnd,
    MissingVisualTargetKey,
    UnknownVisualTargetKey,
    EvidenceOutsideTargetInterval,
    OpenIntervalClosedAtTargetEnd,
    DuplicateTargetMetadata,
    MissingRecordKind,
    UnknownRecordKind,
    DuplicateVisualSignalSample,
    MissingVisualSignalSamples,
    IrregularVisualSignalCadence,
    MissingAudioStreamIndex,
    UnknownAudioStreamIndex,
    DuplicateAudioSignalWindow,
    OverlappingAudioSignalWindow,
    MissingAudioSignalWindows,
    IrregularAudioSignalCadence,
}

public sealed class MediaEvidenceWarning
{
    public MediaEvidenceWarning(
        MediaEvidenceWarningCode code,
        string message,
        int? streamIndex = null,
        string? targetKey = null)
    {
        if (!Enum.IsDefined(
                typeof(MediaEvidenceWarningCode),
                code))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "The evidence warning code is not defined.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "An evidence warning requires a message.",
                nameof(message));
        }

        if (streamIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(streamIndex),
                streamIndex,
                "Stream index cannot be negative.");
        }

        if (targetKey is not null &&
            string.IsNullOrWhiteSpace(targetKey))
        {
            throw new ArgumentException(
                "A visual target key cannot be blank.",
                nameof(targetKey));
        }

        if (streamIndex is not null &&
            targetKey is not null)
        {
            throw new ArgumentException(
                "An evidence warning cannot identify both an audio stream and a visual target.");
        }

        Code = code;
        Message = message;
        StreamIndex = streamIndex;
        TargetKey = targetKey?.Trim();
    }

    public MediaEvidenceWarningCode Code { get; }

    public string Message { get; }

    public int? StreamIndex { get; }

    public string? TargetKey { get; }
}
