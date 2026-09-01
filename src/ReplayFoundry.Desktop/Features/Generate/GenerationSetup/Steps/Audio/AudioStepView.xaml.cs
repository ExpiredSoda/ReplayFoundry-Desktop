using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Audio;

public partial class AudioStepView : UserControl
{
    public AudioStepView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AudioStepViewModel viewModel)
        {
            await viewModel.PrepareAuditionsAsync();
        }
    }
}
