using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ReplayFoundry.Desktop.Features.Generate.SourceSelection;

public partial class SourceSelectionView : UserControl
{
    public SourceSelectionView()
    {
        InitializeComponent();
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
    }

    private void DropZone_Drop(
        object sender,
        DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not GenerateViewModel viewModel)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop)
            is not string[] droppedPaths)
        {
            return;
        }

        viewModel.AddDroppedFiles(droppedPaths);
    }
}
