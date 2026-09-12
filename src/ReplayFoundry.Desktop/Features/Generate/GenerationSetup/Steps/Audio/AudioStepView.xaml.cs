using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Audio;

public partial class AudioStepView : UserControl
{
    public AudioStepView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => (DataContext as AudioStepViewModel)?.StopAuditions();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) (DataContext as AudioStepViewModel)?.StopAuditions();
        };
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AudioStepViewModel viewModel)
        {
            await viewModel.PrepareAuditionsAsync();
        }
    }
}
