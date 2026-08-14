using System.Windows;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

internal sealed class GenerationCompositionReviewDialogService :
    IGenerationCompositionReviewDialogService
{
    private readonly Func<
        GenerationCompositionReviewRequest,
        GenerationCompositionReviewResult?,
        CompositionReviewViewModel> _viewModelFactory;

    public GenerationCompositionReviewDialogService(
        Func<
            GenerationCompositionReviewRequest,
            GenerationCompositionReviewResult?,
            CompositionReviewViewModel> viewModelFactory)
    {
        ArgumentNullException.ThrowIfNull(
            viewModelFactory);

        _viewModelFactory =
            viewModelFactory;
    }

    public GenerationCompositionReviewResult? Show(
        GenerationCompositionReviewRequest request,
        GenerationCompositionReviewResult? initialResult)
    {
        ArgumentNullException.ThrowIfNull(request);

        CompositionReviewViewModel viewModel =
            _viewModelFactory(
                request,
                initialResult) ??
            throw new InvalidOperationException(
                "The composition-review ViewModel factory returned null.");

        var window =
            new CompositionReviewWindow(
                viewModel);

        Window? owner =
            Application.Current?.MainWindow;

        if (owner is not null)
        {
            window.Owner = owner;
        }

        bool? dialogResult =
            window.ShowDialog();

        return dialogResult == true
            ? window.Result
            : null;
    }
}
