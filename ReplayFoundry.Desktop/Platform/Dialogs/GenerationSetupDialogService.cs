using System;
using System.Windows;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

internal sealed class GenerationSetupDialogService :
    IGenerationSetupDialogService
{
    private readonly Func<
        GenerationSetupRequest,
        GenerationSetupOptions?,
        GenerationSetupViewModel> _viewModelFactory;

    public GenerationSetupDialogService(
        Func<
            GenerationSetupRequest,
            GenerationSetupOptions?,
            GenerationSetupViewModel> viewModelFactory)
    {
        ArgumentNullException.ThrowIfNull(
            viewModelFactory);

        _viewModelFactory =
            viewModelFactory;
    }

    public GenerationSetupOptions? Show(
        GenerationSetupRequest request,
        GenerationSetupOptions? initialOptions)
    {
        ArgumentNullException.ThrowIfNull(request);

        GenerationSetupViewModel viewModel =
            _viewModelFactory(
                request,
                initialOptions) ??
            throw new InvalidOperationException(
                "The Generation Setup ViewModel factory returned null.");

        var window =
            new GenerationSetupWindow(
                viewModel);

        Window? owner =
            Application.Current?.MainWindow;

        if (owner is not null)
        {
            window.Owner = owner;
        }

        bool? result =
            window.ShowDialog();

        return result == true
            ? window.Result
            : null;
    }
}
