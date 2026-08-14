using System.IO;

namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public sealed class GenerationSourcePreparationException :
    IOException
{
    public GenerationSourcePreparationException(
        string sourcePath,
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException(
                "A preparation failure requires a source path.",
                nameof(sourcePath));
        }

        if (!Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException(
                "The failed source path must be fully qualified.",
                nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A preparation failure requires a message.",
                nameof(message));
        }

        SourcePath = sourcePath;
        DiagnosticDetails =
            string.IsNullOrWhiteSpace(diagnosticDetails)
                ? null
                : diagnosticDetails.Trim();
    }

    public string SourcePath { get; }

    public string? DiagnosticDetails { get; }
}
