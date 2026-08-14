using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace ReplayFoundry.Desktop.Media.Inspection;

public sealed class MediaProbeResult
{
    private readonly ReadOnlyCollection<VideoStreamInfo> _videoStreams;
    private readonly ReadOnlyCollection<AudioStreamInfo> _audioStreams;
    private readonly ReadOnlyCollection<MediaInspectionWarning> _warnings;

    public MediaProbeResult(
        string fullPath,
        MediaContainerInfo container,
        IEnumerable<VideoStreamInfo> videoStreams,
        IEnumerable<AudioStreamInfo> audioStreams,
        MediaInspectionManifest manifest,
        IEnumerable<MediaInspectionWarning>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "A media probe result requires a source path.",
                nameof(fullPath));
        }

        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException(
                "The media source path must be fully qualified.",
                nameof(fullPath));
        }

        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(videoStreams);
        ArgumentNullException.ThrowIfNull(audioStreams);
        ArgumentNullException.ThrowIfNull(manifest);

        VideoStreamInfo[] videoSnapshot =
            videoStreams.ToArray();

        AudioStreamInfo[] audioSnapshot =
            audioStreams.ToArray();

        MediaInspectionWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (videoSnapshot.Length == 0)
        {
            throw new ArgumentException(
                "A media probe result requires at least one video stream.",
                nameof(videoStreams));
        }

        if (videoSnapshot.Any(static stream => stream is null))
        {
            throw new ArgumentException(
                "The video stream collection cannot contain null entries.",
                nameof(videoStreams));
        }

        if (audioSnapshot.Any(static stream => stream is null))
        {
            throw new ArgumentException(
                "The audio stream collection cannot contain null entries.",
                nameof(audioStreams));
        }

        if (warningSnapshot.Any(static warning => warning is null))
        {
            throw new ArgumentException(
                "The warning collection cannot contain null entries.",
                nameof(warnings));
        }

        FullPath = fullPath;
        Container = container;
        Manifest = manifest;

        _videoStreams =
            Array.AsReadOnly(videoSnapshot);

        _audioStreams =
            Array.AsReadOnly(audioSnapshot);

        VideoStreamInfo? defaultVideoStream =
            videoSnapshot.FirstOrDefault(
                static stream => stream.IsDefault);

        PrimaryVideoStream =
            defaultVideoStream ??
            videoSnapshot[0];

        if (defaultVideoStream is null &&
            !warningSnapshot.Any(
                static warning =>
                    warning.Code ==
                    MediaInspectionWarningCode.PrimaryVideoStreamNotMarked))
        {
            warningSnapshot =
            [
                .. warningSnapshot,
                new MediaInspectionWarning(
                    MediaInspectionWarningCode.PrimaryVideoStreamNotMarked,
                    "The container did not mark a default video stream. " +
                    "Replay Foundry selected the first video stream as primary.",
                    PrimaryVideoStream.Index),
            ];
        }

        _warnings =
            Array.AsReadOnly(warningSnapshot);
    }

    public string FullPath { get; }

    public MediaContainerInfo Container { get; }

    public MediaInspectionManifest Manifest { get; }

    public IReadOnlyList<VideoStreamInfo> VideoStreams =>
        _videoStreams;

    public IReadOnlyList<AudioStreamInfo> AudioStreams =>
        _audioStreams;

    public IReadOnlyList<MediaInspectionWarning> Warnings =>
        _warnings;

    public VideoStreamInfo PrimaryVideoStream { get; }

    public TimeSpan Duration =>
        Container.Duration;

    public bool HasAudio =>
        _audioStreams.Count > 0;
}
