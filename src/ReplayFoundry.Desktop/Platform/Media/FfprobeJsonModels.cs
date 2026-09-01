using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfprobeDocument
{
    [JsonPropertyName("streams")]
    public List<FfprobeStream>? Streams { get; set; }

    [JsonPropertyName("format")]
    public FfprobeFormat? Format { get; set; }

    [JsonPropertyName("error")]
    public FfprobeError? Error { get; set; }
}

internal sealed class FfprobeFormat
{
    [JsonPropertyName("filename")]
    public string? FileName { get; set; }

    [JsonPropertyName("format_name")]
    public string? FormatName { get; set; }

    [JsonPropertyName("format_long_name")]
    public string? FormatLongName { get; set; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; set; }

    [JsonPropertyName("probe_score")]
    public int? ProbeScore { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

internal sealed class FfprobeStream
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("codec_name")]
    public string? CodecName { get; set; }

    [JsonPropertyName("codec_long_name")]
    public string? CodecLongName { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("codec_type")]
    public string? CodecType { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("coded_width")]
    public int? CodedWidth { get; set; }

    [JsonPropertyName("coded_height")]
    public int? CodedHeight { get; set; }

    [JsonPropertyName("sample_aspect_ratio")]
    public string? SampleAspectRatio { get; set; }

    [JsonPropertyName("display_aspect_ratio")]
    public string? DisplayAspectRatio { get; set; }

    [JsonPropertyName("pix_fmt")]
    public string? PixelFormat { get; set; }

    [JsonPropertyName("bits_per_raw_sample")]
    public string? BitsPerRawSample { get; set; }

    [JsonPropertyName("avg_frame_rate")]
    public string? AverageFrameRate { get; set; }

    [JsonPropertyName("r_frame_rate")]
    public string? RealFrameRate { get; set; }

    [JsonPropertyName("field_order")]
    public string? FieldOrder { get; set; }

    [JsonPropertyName("color_range")]
    public string? ColorRange { get; set; }

    [JsonPropertyName("color_space")]
    public string? ColorSpace { get; set; }

    [JsonPropertyName("color_transfer")]
    public string? ColorTransfer { get; set; }

    [JsonPropertyName("color_primaries")]
    public string? ColorPrimaries { get; set; }

    [JsonPropertyName("chroma_location")]
    public string? ChromaLocation { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("bit_rate")]
    public string? BitRate { get; set; }

    [JsonPropertyName("sample_rate")]
    public string? SampleRate { get; set; }

    [JsonPropertyName("channels")]
    public int? Channels { get; set; }

    [JsonPropertyName("channel_layout")]
    public string? ChannelLayout { get; set; }

    [JsonPropertyName("bits_per_sample")]
    public int? BitsPerSample { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }

    [JsonPropertyName("disposition")]
    public FfprobeDisposition? Disposition { get; set; }

    [JsonPropertyName("side_data_list")]
    public List<FfprobeSideData>? SideDataList { get; set; }
}

internal sealed class FfprobeDisposition
{
    [JsonPropertyName("default")]
    public int Default { get; set; }
}

internal sealed class FfprobeSideData
{
    [JsonPropertyName("side_data_type")]
    public string? SideDataType { get; set; }

    [JsonPropertyName("rotation")]
    public double? Rotation { get; set; }
}

internal sealed class FfprobeError
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("string")]
    public string? Message { get; set; }
}
