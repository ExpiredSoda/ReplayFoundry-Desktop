using System;

namespace ReplayFoundry.Desktop.Media.Composition;

public enum CompositionWarningCode
{
    UnknownRegionRole,
    LowGeometryConfidence,
    LowRoleConfidence,
    DefaultAssumptionApplied,
    ProfileCompatibilityNotVerified,
    CoverageIncomplete,
}

/// <summary>
/// A non-fatal composition concern retained for review and provenance.
/// </summary>
public sealed class CompositionWarning
{
    public CompositionWarning(
        CompositionWarningCode code,
        string message,
        string? regionId = null)
    {
        if (!Enum.IsDefined(
                typeof(CompositionWarningCode),
                code))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "The composition warning code is not defined.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A composition warning requires a message.",
                nameof(message));
        }

        if (regionId is not null &&
            string.IsNullOrWhiteSpace(regionId))
        {
            throw new ArgumentException(
                "A supplied composition region identifier cannot be blank.",
                nameof(regionId));
        }

        Code = code;
        Message = message.Trim();
        RegionId = regionId?.Trim();
    }

    public CompositionWarningCode Code { get; }

    public string Message { get; }

    public string? RegionId { get; }
}
