using System.IO;

namespace ReplayFoundry.Desktop.Media.Preview;

public sealed class VideoPreviewFrameManifest
{
    public VideoPreviewFrameManifest(
        string providerName,
        string providerVersion,
        string toolName,
        string toolVersion,
        string toolPath,
        DateTimeOffset extractedAtUtc,
        TimeSpan processDuration)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException(
                "A preview manifest requires a provider name.",
                nameof(providerName));
        }

        if (string.IsNullOrWhiteSpace(providerVersion))
        {
            throw new ArgumentException(
                "A preview manifest requires a provider version.",
                nameof(providerVersion));
        }

        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException(
                "A preview manifest requires a tool name.",
                nameof(toolName));
        }

        if (string.IsNullOrWhiteSpace(toolVersion))
        {
            throw new ArgumentException(
                "A preview manifest requires a tool version.",
                nameof(toolVersion));
        }

        if (string.IsNullOrWhiteSpace(toolPath) ||
            !Path.IsPathFullyQualified(toolPath))
        {
            throw new ArgumentException(
                "A preview manifest requires a fully qualified tool path.",
                nameof(toolPath));
        }

        if (extractedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The preview extraction timestamp must use UTC.",
                nameof(extractedAtUtc));
        }

        if (processDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processDuration),
                processDuration,
                "Preview extraction duration cannot be negative.");
        }

        ProviderName = providerName.Trim();
        ProviderVersion = providerVersion.Trim();
        ToolName = toolName.Trim();
        ToolVersion = toolVersion.Trim();
        ToolPath = toolPath;
        ExtractedAtUtc = extractedAtUtc;
        ProcessDuration = processDuration;
    }

    public string ProviderName { get; }

    public string ProviderVersion { get; }

    public string ToolName { get; }

    public string ToolVersion { get; }

    public string ToolPath { get; }

    public DateTimeOffset ExtractedAtUtc { get; }

    public TimeSpan ProcessDuration { get; }
}
