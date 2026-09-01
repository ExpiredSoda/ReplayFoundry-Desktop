using System;
using System.IO;

namespace ReplayFoundry.Desktop.Media.Analysis;

public class MediaEvidenceAnalysisException : IOException
{
    public MediaEvidenceAnalysisException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "An evidence-analysis failure requires a message.",
                nameof(message));
        }

        DiagnosticDetails = diagnosticDetails;
    }

    public string? DiagnosticDetails { get; }
}
