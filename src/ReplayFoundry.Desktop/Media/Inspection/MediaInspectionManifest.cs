using System;
using System.IO;

namespace ReplayFoundry.Desktop.Media.Inspection;

public sealed class MediaInspectionManifest
{
    public MediaInspectionManifest(
        string inspectorName,
        string inspectorVersion,
        string toolName,
        string toolVersion,
        string toolPath,
        DateTimeOffset inspectedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(inspectorName))
        {
            throw new ArgumentException(
                "An inspection manifest requires an inspector name.",
                nameof(inspectorName));
        }

        if (string.IsNullOrWhiteSpace(inspectorVersion))
        {
            throw new ArgumentException(
                "An inspection manifest requires an inspector version.",
                nameof(inspectorVersion));
        }

        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException(
                "An inspection manifest requires a tool name.",
                nameof(toolName));
        }

        if (string.IsNullOrWhiteSpace(toolVersion))
        {
            throw new ArgumentException(
                "An inspection manifest requires a tool version.",
                nameof(toolVersion));
        }

        if (string.IsNullOrWhiteSpace(toolPath))
        {
            throw new ArgumentException(
                "An inspection manifest requires a tool path.",
                nameof(toolPath));
        }

        if (!Path.IsPathFullyQualified(toolPath))
        {
            throw new ArgumentException(
                "The media-inspection tool path must be fully qualified.",
                nameof(toolPath));
        }

        if (inspectedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The inspection timestamp must use UTC.",
                nameof(inspectedAtUtc));
        }

        InspectorName = inspectorName;
        InspectorVersion = inspectorVersion;
        ToolName = toolName;
        ToolVersion = toolVersion;
        ToolPath = toolPath;
        InspectedAtUtc = inspectedAtUtc;
    }

    public string InspectorName { get; }

    public string InspectorVersion { get; }

    public string ToolName { get; }

    public string ToolVersion { get; }

    public string ToolPath { get; }

    public DateTimeOffset InspectedAtUtc { get; }
}
