using System;

namespace ReplayFoundry.Desktop.Media.Inspection;

public enum MediaInspectionWarningCode
{
    PrimaryVideoStreamNotMarked,
    BitDepthDerived,
    SampleAspectRatioAssumed,
    DisplayAspectRatioDerived,
    AudioChannelLayoutNotReported,
}

public sealed class MediaInspectionWarning
{
    public MediaInspectionWarning(
        MediaInspectionWarningCode code,
        string message,
        int? streamIndex = null)
    {
        if (!Enum.IsDefined(
                typeof(MediaInspectionWarningCode),
                code))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "The media inspection warning code is not defined.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A media inspection warning requires a message.",
                nameof(message));
        }

        if (streamIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(streamIndex),
                streamIndex,
                "A stream index cannot be negative.");
        }

        Code = code;
        Message = message.Trim();
        StreamIndex = streamIndex;
    }

    public MediaInspectionWarningCode Code { get; }

    public string Message { get; }

    public int? StreamIndex { get; }
}
