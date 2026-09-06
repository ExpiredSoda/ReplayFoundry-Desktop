using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

public static class FfprobeChapterMarkerReader
{
    public static async Task<IReadOnlyList<UserMomentGuidance>> ReadAsync(string recordingPath, TimeSpan duration, CancellationToken cancellationToken)
    {
        var result = await new WindowsProcessRunner().RunAsync(new ProcessRunRequest(new FfmpegToolLocator().LocateFfprobe(),
            ["-v", "error", "-show_chapters", "-of", "json", recordingPath], TimeSpan.FromSeconds(30), maxStandardOutputCharacters: 1_000_000), cancellationToken);
        if (!result.Succeeded) throw new InvalidOperationException("The recording's chapter markers could not be read.");
        return MomentMarkerImporter.ParseChapterJson(result.StandardOutput, recordingPath, duration);
    }
}
