using System.IO;

namespace ReplayFoundry.Desktop.Media.Preview;

public sealed class VideoPreviewFrameException :
    IOException
{
    public VideoPreviewFrameException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A preview-frame failure requires a message.",
                nameof(message));
        }

        DiagnosticDetails =
            string.IsNullOrWhiteSpace(diagnosticDetails)
                ? null
                : diagnosticDetails.Trim();
    }

    public string? DiagnosticDetails { get; }
}
