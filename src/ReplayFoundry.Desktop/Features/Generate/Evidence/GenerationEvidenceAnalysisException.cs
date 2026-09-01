using System.IO;

namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

public class GenerationEvidenceAnalysisException :
    IOException
{
    public GenerationEvidenceAnalysisException(
        string sourcePath,
        int sourceNumber,
        int sourceCount,
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException(
                "An evidence-analysis failure requires a fully qualified source path.",
                nameof(sourcePath));
        }

        if (sourceCount <= 0 ||
            sourceNumber <= 0 ||
            sourceNumber > sourceCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceNumber),
                "The failed source position must be within the batch.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "An evidence-analysis failure requires a message.",
                nameof(message));
        }

        SourcePath = sourcePath;
        SourceNumber = sourceNumber;
        SourceCount = sourceCount;
        DiagnosticDetails =
            string.IsNullOrWhiteSpace(diagnosticDetails)
                ? null
                : diagnosticDetails.Trim();
    }

    public string SourcePath { get; }

    public int SourceNumber { get; }

    public int SourceCount { get; }

    public string? DiagnosticDetails { get; }
}

public sealed class GenerationEvidenceToolUnavailableException :
    GenerationEvidenceAnalysisException
{
    public GenerationEvidenceToolUnavailableException(
        string sourcePath,
        int sourceNumber,
        int sourceCount,
        string message,
        Exception innerException)
        : base(
            sourcePath,
            sourceNumber,
            sourceCount,
            message,
            innerException: innerException)
    {
    }
}
