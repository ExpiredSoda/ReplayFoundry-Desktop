using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

public static class StudioProjectRenderingFactory
{
    public static IStudioProjectRenderingService CreateDefault() =>
        new FfmpegStudioProjectRenderingService(
            new WindowsProcessRunner(),
            new FfmpegToolLocator());
}
