using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

public static class VideoPreviewFrameFactory
{
    public static IVideoPreviewFrameProvider CreateDefault()
    {
        IProcessRunner processRunner =
            new WindowsProcessRunner();

        var toolLocator =
            new FfmpegToolLocator();

        IPreviewWorkspaceFactory workspaceFactory =
            new SystemPreviewWorkspaceFactory();

        return new FfmpegVideoPreviewFrameProvider(
            processRunner,
            toolLocator,
            workspaceFactory);
    }
}
