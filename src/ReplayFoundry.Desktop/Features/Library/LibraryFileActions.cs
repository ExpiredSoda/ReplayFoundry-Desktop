namespace ReplayFoundry.Desktop.Features.Library;

public interface ILibraryMediaFilePicker
{
    string? PickReplacementMedia(LibraryMediaAsset asset);
}

public interface ILocalFolderLauncher
{
    void OpenFolder(string fullPath);
}

public interface ILibraryRemovalConfirmation
{
    bool ConfirmRemoveFromLibrary(IReadOnlyList<LibraryMediaAsset> assets);
}
