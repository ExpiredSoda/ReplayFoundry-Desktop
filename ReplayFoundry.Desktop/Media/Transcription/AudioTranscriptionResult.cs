using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Intelligence;

namespace ReplayFoundry.Desktop.Media.Transcription;

public sealed class AudioTranscriptionResult
{
    private readonly ReadOnlyCollection<AudioTranscriptionSegment>
        _segments;

    private readonly ReadOnlyCollection<AudioTranscriptionWarning>
        _warnings;

    public AudioTranscriptionResult(
        string neighborhoodId,
        int absoluteAudioStreamIndex,
        IEnumerable<AudioTranscriptionSegment> segments,
        AudioTranscriptionManifest manifest,
        AudioTranscriptionLanguage? detectedLanguage = null,
        IEnumerable<AudioTranscriptionWarning>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(neighborhoodId) ||
            absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentException(
                "Transcription results require a neighborhood and valid stream index.");
        }

        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(manifest);

        AudioTranscriptionSegment[] segmentSnapshot =
            segments.ToArray();
        AudioTranscriptionWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (segmentSnapshot.Any(static segment => segment is null) ||
            warningSnapshot.Any(static warning => warning is null) ||
            segmentSnapshot.Any(
                segment =>
                    !string.Equals(
                        segment.NeighborhoodId,
                        neighborhoodId,
                        StringComparison.Ordinal)) ||
            segmentSnapshot.Zip(
                    segmentSnapshot.Skip(1),
                    static (left, right) =>
                        right.RelativeStart < left.RelativeEnd)
                .Any(static overlaps => overlaps) ||
            segmentSnapshot.Any(
                segment =>
                    segment.RelativeEnd > manifest.InputDuration ||
                    segment.AbsoluteSourceStart <
                    manifest.AbsoluteSourceOffset ||
                    segment.AbsoluteSourceEnd >
                    manifest.AbsoluteSourceOffset +
                    manifest.InputDuration))
        {
            throw new ArgumentException(
                "Transcription segments must be ordered, bounded, and owned by the result neighborhood.",
                nameof(segments));
        }

        if (!string.Equals(
                manifest.NeighborhoodId,
                neighborhoodId,
                StringComparison.Ordinal) ||
            manifest.AbsoluteAudioStreamIndex !=
            absoluteAudioStreamIndex)
        {
            throw new ArgumentException(
                "The transcription manifest does not match its result.");
        }

        NeighborhoodId = neighborhoodId.Trim();
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        DetectedLanguage = detectedLanguage;
        Manifest = manifest;
        _segments = Array.AsReadOnly(segmentSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string NeighborhoodId { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public IReadOnlyList<AudioTranscriptionSegment> Segments =>
        _segments;

    public AudioTranscriptionLanguage? DetectedLanguage { get; }

    public AudioTranscriptionManifest Manifest { get; }

    public IReadOnlyList<AudioTranscriptionWarning> Warnings =>
        _warnings;
}

public interface IAudioTranscriptionProvider
{
    InferenceProviderIdentity Identity { get; }

    Task<AudioTranscriptionProviderCapabilities>
        GetCapabilitiesAsync(
            CancellationToken cancellationToken);

    Task<AudioTranscriptionResult> TranscribeAsync(
        AudioTranscriptionRequest request,
        CancellationToken cancellationToken);
}

internal static class TranscriptionStableId
{
    public static string Create(
        string prefix,
        params string[] values)
    {
        string material =
            string.Join(
                "\u001F",
                values);
        string hash =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(material)));

        return $"{prefix}-{hash[..16].ToLowerInvariant()}";
    }
}
