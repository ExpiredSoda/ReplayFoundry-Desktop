using System.Windows;
using ReplayFoundry.Desktop.Shell.Windowing;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public partial class CompositionReviewWindow :
    Window
{
    private readonly CompositionReviewViewModel
        _viewModel;
    private bool _dialogCompletionRequested;
    private bool _isClosed;

    public CompositionReviewWindow(
        CompositionReviewViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.CancelRequested +=
            ViewModel_CancelRequested;

        _viewModel.FinishRequested +=
            ViewModel_FinishRequested;

        Loaded +=
            CompositionReviewWindow_Loaded;

        Closed +=
            CompositionReviewWindow_Closed;
    }

    public GenerationCompositionReviewResult?
        Result
    {
        get;
        private set;
    }

    private async void CompositionReviewWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        DialogWindowSizing.FitToOwnerWorkArea(this);

        CompositionReviewInitializationOutcome outcome =
            await _viewModel.InitializeAsync();

        if (outcome ==
            CompositionReviewInitializationOutcome
                .LifecycleCancelled)
        {
            CompleteLifecycleCancellation();
        }
    }

    private void ViewModel_CancelRequested(
        object? sender,
        EventArgs e)
    {
        CompleteDialog(
            result: null,
            dialogResult: false);
    }

    private void ViewModel_FinishRequested(
        object? sender,
        CompositionReviewCompletedEventArgs e)
    {
        CompleteDialog(
            e.Result,
            dialogResult: true);
    }

    private void CompositionReviewWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;

        Loaded -=
            CompositionReviewWindow_Loaded;

        Closed -=
            CompositionReviewWindow_Closed;

        _viewModel.CancelRequested -=
            ViewModel_CancelRequested;

        _viewModel.FinishRequested -=
            ViewModel_FinishRequested;

        _viewModel.Dispose();
    }

    private void CompleteLifecycleCancellation()
    {
        if (_isClosed)
        {
            return;
        }

        CompleteDialog(
            result: null,
            dialogResult: false);
    }

    private void CompleteDialog(
        GenerationCompositionReviewResult? result,
        bool dialogResult)
    {
        if (_dialogCompletionRequested || _isClosed)
        {
            return;
        }

        _dialogCompletionRequested = true;
        Result = result;
        DialogResult = dialogResult;
    }
}
