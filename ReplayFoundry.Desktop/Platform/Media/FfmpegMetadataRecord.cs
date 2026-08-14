using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegMetadataRecord
{
    public FfmpegMetadataRecord(
        TimeSpan? timestamp,
        IReadOnlyDictionary<string, string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        Timestamp = timestamp;
        Tags = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                tags,
                StringComparer.OrdinalIgnoreCase));
    }

    public TimeSpan? Timestamp { get; }

    public IReadOnlyDictionary<string, string> Tags { get; }
}
