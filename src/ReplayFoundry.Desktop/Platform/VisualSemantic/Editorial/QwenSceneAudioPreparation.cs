using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class QwenSceneAudioPreparation
{
    internal static async Task<object> PrepareAsync(VisualSemanticRequest request, string directory,
        string ffmpeg, CancellationToken cancellationToken)
    {
        var context = request.SceneContext;
        if (context is null) return new { status = "Unavailable", tracks = Array.Empty<object>() };
        VerifySource(context);
        if (context.SourceEnd - context.SourceStart != request.CandidateEndRelative - request.CandidateStartRelative ||
            context.AudioTracks.Count > 4 || context.AudioTracks.Select(track => track.StreamIndex).Distinct().Count() != context.AudioTracks.Count)
            throw new InvalidDataException("Audio evidence does not belong to this bounded review.");
        var tracks = new List<object>();
        foreach (var track in context.AudioTracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (track.StreamIndex < 0) throw new InvalidDataException("Audio evidence requires absolute stream indices.");
            string wavePath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wav");
            var result = await new WindowsProcessRunner().RunAsync(new ProcessRunRequest(ffmpeg,
                ["-nostdin", "-v", "error", "-ss", context.SourceStart.TotalSeconds.ToString("R", CultureInfo.InvariantCulture),
                    "-i", context.SourcePath, "-t", (context.SourceEnd - context.SourceStart).TotalSeconds.ToString("R", CultureInfo.InvariantCulture),
                    "-map", "0:" + track.StreamIndex.ToString(CultureInfo.InvariantCulture), "-vn", "-sn", "-dn",
                    "-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le", wavePath], TimeSpan.FromMinutes(3)), cancellationToken);
            if (!result.Succeeded) throw new InvalidDataException("A selected audio evidence stream could not be decoded.");
            string hash;
            await using (var input = File.OpenRead(wavePath))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
            tracks.Add(new { streamIndex = track.StreamIndex, path = wavePath, sha256 = hash,
                role = track.Role.Role.ToString(), roleSource = track.Role.Source.ToString(),
                speech = track.Speech.Select(span => new { id = $"speech-{track.StreamIndex}-{span.Id}",
                    start = span.ReviewRelativeStart.TotalSeconds, end = span.ReviewRelativeEnd.TotalSeconds, text = span.Text }) });
        }
        VerifySource(context);
        return new { status = tracks.Count == 0 ? "NoAudio" : "Prepared", tracks };
    }

    internal static void VerifySource(SceneReviewContext context)
    {
        var file = new FileInfo(context.SourcePath);
        if (!file.Exists || file.Length != context.SourceLength || file.LastWriteTimeUtc.Ticks != context.SourceModifiedUtcTicks ||
            context.SourceStart < TimeSpan.Zero || context.SourceEnd <= context.SourceStart ||
            context.SourceEnd - context.SourceStart > TimeSpan.FromMinutes(20))
            throw new InvalidDataException("The recording changed after its audio context was selected.");
    }
}
