using System;
using System.IO;

namespace ReplayFoundry.Desktop.Media.Inspection;

public class MediaProbeException : IOException
{
    public MediaProbeException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A media-probe failure requires a message.",
                nameof(message));
        }

        DiagnosticDetails = diagnosticDetails;
    }

    public string? DiagnosticDetails { get; }
}

public sealed class MediaToolNotFoundException :
    MediaProbeException
{
    public MediaToolNotFoundException(
        string message)
        : base(message)
    {
    }
}
