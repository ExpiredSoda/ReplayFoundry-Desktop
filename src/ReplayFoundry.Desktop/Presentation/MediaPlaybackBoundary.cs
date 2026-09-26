namespace ReplayFoundry.Desktop.Presentation;

/// <summary>
/// Keeps end-of-media replay behavior consistent across native preview players.
/// </summary>
internal static class MediaPlaybackBoundary
{
    internal const double EndToleranceSeconds = 0.05;

    internal static bool HasReachedEnd(
        double positionSeconds,
        double endSeconds)
    {
        if (!double.IsFinite(positionSeconds) ||
            !double.IsFinite(endSeconds) ||
            endSeconds <= 0)
        {
            return false;
        }

        return positionSeconds >=
            Math.Max(0, endSeconds - EndToleranceSeconds);
    }
}
