namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public interface IGenerationSetupDialogService
{
    GenerationSetupOptions? Show(
        GenerationSetupRequest request,
        GenerationSetupOptions? initialOptions);
}
