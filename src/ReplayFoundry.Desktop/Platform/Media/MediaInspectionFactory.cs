using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

public static class MediaInspectionFactory
{
    public static IMediaProbe CreateDefault()
    {
        IProcessRunner processRunner =
            new WindowsProcessRunner();

        var toolLocator =
            new FfmpegToolLocator();

        return new FfprobeMediaProbe(
            processRunner,
            toolLocator);
    }
}
