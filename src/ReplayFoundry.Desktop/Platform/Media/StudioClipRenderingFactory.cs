using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Transcription;

namespace ReplayFoundry.Desktop.Platform.Media;

public static class StudioProjectRenderingFactory
{
    public static IStudioProjectRenderingService CreateDefault() =>
        new FfmpegStudioProjectRenderingService(
            new WindowsProcessRunner(),
            new FfmpegToolLocator(),
            verifyOutput: true,
            hardwareEncoding: true,
            resumeCompletedSegments: true,
            captionAlignment: new OnnxCorrectedCaptionAlignmentService(AudioSegmentExtractionFactory.CreateDefault()));
}
