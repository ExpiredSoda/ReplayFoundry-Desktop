using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Features.Publish;

internal static class PublishChecklistProjector
{
    public static IReadOnlyList<PublishChecklistItem> Build(
        PublishViewModel viewModel,
        bool isScheduleReady)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        string thumbnailValidation =
            viewModel.ThumbnailValidationMessage;
        return
        [
            new(
                "Final Studio video",
                viewModel.HasAsset
                    ? viewModel.AssetTitle
                    : "Complete Studio rendering",
                viewModel.HasAsset ? "Ready" : "Waiting"),
            new(
                "YouTube channel",
                viewModel.Connection?.ChannelTitle ??
                    viewModel.ConnectionStatus,
                viewModel.IsConnected ? "Ready" : "Waiting"),
            new(
                "Title and description",
                viewModel.PresentationValidationMessage,
                viewModel.IsMetadataWithinLimits ? "Ready" : "Waiting"),
            new(
                "Audience",
                viewModel.Audience == YouTubeAudience.MadeForKids
                    ? "Made for kids"
                    : "Not made for kids",
                "Ready"),
            new(
                "Category",
                viewModel.Categories.FirstOrDefault(category =>
                    category.Id == viewModel.SelectedCategoryId)?.Title ??
                    "Choose a category",
                viewModel.SelectedCategoryId is null
                    ? "Waiting"
                    : "Ready"),
            new(
                "Release",
                viewModel.ScheduleSummary,
                isScheduleReady ? "Ready" : "Waiting"),
            new(
                "Thumbnail",
                thumbnailValidation,
                thumbnailValidation is
                    "Thumbnail ready." or
                    "YouTube will choose a thumbnail."
                    ? "Ready"
                    : "Waiting"),
        ];
    }
}
