namespace ReplayFoundry.Desktop.Features.Generate.SourceSelection;

public interface IMediaRightsConfirmation
{
    bool Confirm(IReadOnlyList<SelectedVideoSource> sources);
}
