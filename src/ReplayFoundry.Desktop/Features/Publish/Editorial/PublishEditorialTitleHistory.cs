using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Publish.Editorial;

/// <summary>
/// Keeps Publish presentation code on a feature-facing title-history contract
/// while delegating canonical normalization and retention limits to the shared
/// editorial policy.
/// </summary>
internal static class PublishEditorialTitleHistory
{
    public static IReadOnlyList<string> Merge(
        IEnumerable<string>? history,
        string? currentTitle = null) =>
        ClipEditorialPriorTitleExclusion.MergeTitleHistory(
            history,
            currentTitle);
}
