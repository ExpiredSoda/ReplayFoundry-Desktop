using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ReplayFoundry.Desktop.Features.Generate.SourceSelection;

public partial class SourceSelectionView : UserControl
{
    public static readonly DependencyProperty IsFileDragOverProperty = DependencyProperty.Register(
        nameof(IsFileDragOver), typeof(bool), typeof(SourceSelectionView), new PropertyMetadata(false));
    public static readonly DependencyProperty IsShortLayoutProperty = DependencyProperty.Register(
        nameof(IsShortLayout), typeof(bool), typeof(SourceSelectionView), new PropertyMetadata(false));

    public bool IsShortLayout
    {
        get => (bool)GetValue(IsShortLayoutProperty);
        private set => SetValue(IsShortLayoutProperty, value);
    }

    public bool IsFileDragOver
    {
        get => (bool)GetValue(IsFileDragOverProperty);
        private set => SetValue(IsFileDragOverProperty, value);
    }

    public SourceSelectionView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => IsShortLayout = e.NewSize.Height is > 0 and < 660;
    }

    private void DropZone_PreviewDragOver(
        object sender,
        DragEventArgs e)
    {
        e.Effects =
            e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        e.Handled = true;
        IsFileDragOver = e.Effects == DragDropEffects.Copy;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement target &&
            new Rect(new Point(), target.RenderSize).Contains(e.GetPosition(target)))
            return;
        IsFileDragOver = false;
    }

    private async void DropZone_Drop(
        object sender,
        DragEventArgs e)
    {
        e.Handled = true;
        IsFileDragOver = false;

        if (DataContext is not GenerateViewModel viewModel)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop)
            is not string[] droppedPaths)
        {
            return;
        }

        await viewModel.AddDroppedPathsAsync(droppedPaths);
    }
}
