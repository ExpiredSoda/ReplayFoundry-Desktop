using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioMomentSuggestion(string Id, double Start, double End, double Score,
    MomentContentProfile Content, string Explanation)
{
    public string Label => StudioMomentFilters.Label(Content);
    public string TimeText => $"{MediaTimeFormatter.Format(TimeSpan.FromSeconds(Start))}–{MediaTimeFormatter.Format(TimeSpan.FromSeconds(End))}";
}

public static class StudioMomentSuggestionCatalog
{
    public static StudioMomentSuggestion[] Create(GenerationOutputProject? project, string? path) =>
        project is null || path is null ? [] : project.HiddenMoments
            .Where(moment => string.Equals(moment.SourceFullPath, path, StringComparison.OrdinalIgnoreCase))
            .Select(moment => new StudioMomentSuggestion(moment.Id, moment.SourceStart.TotalSeconds, moment.SourceEnd.TotalSeconds,
                moment.FinalScore, StudioMomentFilters.Profile(moment.PreferenceFeatures), moment.ReviewReason))
            .Concat(project.Assets.Where(asset => string.Equals(asset.SourceFullPath, path, StringComparison.OrdinalIgnoreCase))
                .Select(asset => new StudioMomentSuggestion(asset.Id, asset.SourceStart.TotalSeconds, asset.SourceEnd.TotalSeconds,
                    asset.Score, StudioMomentFilters.Profile(asset.PreferenceFeatures), "This moment is already in your Studio clips.")))
            .GroupBy(item => (item.Start, item.End)).Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderBy(item => item.Start).ThenByDescending(item => item.Score).ToArray();
}
