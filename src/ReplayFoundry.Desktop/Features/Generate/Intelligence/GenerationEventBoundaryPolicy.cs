using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Features.Generate.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationEventBoundaryPolicy
{
    public static MomentCandidate IncludeRecovery(MomentCandidate candidate, TimeSpan maximumDuration)
    {
        if (candidate.Episode is not { } episode ||
            !GenerationGameplayEventCoveragePolicy.IsDeterministicGameplayEvent(candidate)) return candidate;
        // Preserve the measured recovery of this same episode, not arbitrary
        // extra footage. Never cut away its setup to make an extension fit.
        TimeSpan end = episode.End + TimeSpan.FromMilliseconds(750);
        if (end > candidate.Window.SourceDuration) end = candidate.Window.SourceDuration;
        if (end <= candidate.Window.End || end - candidate.Window.End > TimeSpan.FromSeconds(12) ||
            end - candidate.Window.Start > maximumDuration) return candidate;
        return candidate.WithWindow(new(candidate.Window.Start, end, candidate.Window.SourceDuration));
    }
}
