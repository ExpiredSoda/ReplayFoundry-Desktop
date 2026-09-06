using System.Windows;
using System.Windows.Controls;
using ReplayFoundry.Desktop.Shell.Windowing;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public partial class CompositionReviewWindow :
    Window
{
    private readonly CompositionReviewViewModel
        _viewModel;
    private bool _dialogCompletionRequested;
    private bool _isClosed;
    private bool? _isCompactLayout;

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
        UpdateResponsiveLayout();

        CompositionReviewInitializationOutcome outcome =
            await _viewModel.InitializeAsync();

        if (outcome ==
            CompositionReviewInitializationOutcome
                .LifecycleCancelled)
        {
            CompleteLifecycleCancellation();
        }
    }

    private void CompositionReviewWindow_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();

    private void UpdateResponsiveLayout()
    {
        if (ActualWidth <= 0) return;
        bool compact = ActualWidth < 1120d;
        if (_isCompactLayout == compact) return;
        _isCompactLayout = compact;

        ReviewScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ReviewScrollViewer.VerticalScrollBarVisibility = compact
            ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ReviewScrollViewer.PanningMode = compact ? PanningMode.VerticalOnly : PanningMode.None;
        ReviewPanels.MinWidth = compact ? 0d : 1014d;
        double[] columnWidths = compact ? [1d, 0d, 0d, 0d, 0d] : [270d, 12d, 1d, 12d, 300d];
        for (int index = 0; index < columnWidths.Length; index++)
        {
            ReviewPanels.ColumnDefinitions[index].MinWidth = !compact && index == 2 ? 420d : 0d;
            ReviewPanels.ColumnDefinitions[index].Width =
                new GridLength(columnWidths[index], index == (compact ? 0 : 2) ? GridUnitType.Star : GridUnitType.Pixel);
        }
        for (int index = 0; index < ReviewPanels.RowDefinitions.Count; index++)
        {
            ReviewPanels.RowDefinitions[index].Height = compact
                ? GridLength.Auto : index == 0 ? new GridLength(1d, GridUnitType.Star) : new GridLength(0d);
        }
        SetPanelPosition(SourcePanel, 0, 0);
        SetPanelPosition(PreviewPanel, compact ? 1 : 0, compact ? 0 : 2);
        SetPanelPosition(AreaPanel, compact ? 2 : 0, compact ? 0 : 4);
        PreviewPanel.Height = compact ? 420d : double.NaN;
        PreviewPanel.Margin = compact ? new Thickness(0, 12, 0, 12) : new Thickness(0);
        ReviewScrollViewer.ScrollToTop();
    }

    private static void SetPanelPosition(Border panel, int row, int column)
    {
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
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
