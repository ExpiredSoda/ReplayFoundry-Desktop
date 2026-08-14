using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

public static class MediaEvidenceAnalysisFactory
{
    public static IMediaEvidenceAnalyzer CreateDefault()
    {
        IProcessRunner processRunner =
            new WindowsProcessRunner();

        var toolLocator =
            new FfmpegToolLocator();

        return new FfmpegEvidenceAnalyzer(
            processRunner,
            toolLocator);
    }
}
