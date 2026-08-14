namespace ReplayFoundry.Desktop.Features.Settings;

public interface IOutputFolderPicker
{
    string? PickOutputFolder(string currentRootDirectory);
}
