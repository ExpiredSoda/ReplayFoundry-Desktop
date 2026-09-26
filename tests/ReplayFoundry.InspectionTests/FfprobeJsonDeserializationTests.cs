using System.Text.Json;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.InspectionTests;

internal static class FfprobeJsonDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
        };

    public static IEnumerable<TestCase> GetTests()
    {
        yield return new TestCase(
            "ffprobe JSON preserves missing channel layout and track titles",
            PreservesMissingChannelLayoutAndTrackTitles);
    }

    private static void PreservesMissingChannelLayoutAndTrackTitles()
    {
        const string json =
            """
            {
              "streams": [
                {
                  "index": 0,
                  "codec_name": "av1",
                  "codec_long_name": "Alliance for Open Media AV1",
                  "profile": "Main",
                  "codec_type": "video",
                  "width": 1080,
                  "height": 1920,
                  "coded_width": 1080,
                  "coded_height": 1920,
                  "pix_fmt": "yuv420p",
                  "r_frame_rate": "60/1",
                  "avg_frame_rate": "60/1",
                  "color_range": "tv",
                  "color_space": "bt709",
                  "color_transfer": "bt709",
                  "color_primaries": "bt709",
                  "chroma_location": "left",
                  "disposition": {
                    "default": 0
                  }
                },
                {
                  "index": 1,
                  "codec_name": "pcm_f32le",
                  "codec_long_name": "PCM 32-bit floating point little-endian",
                  "codec_type": "audio",
                  "sample_rate": "48000",
                  "channels": 2,
                  "bits_per_sample": 32,
                  "bit_rate": "3072000",
                  "tags": {
                    "title": "Microphone_vertical"
                  },
                  "disposition": {
                    "default": 0
                  }
                }
              ],
              "format": {
                "filename": "C:\\\\Fixtures\\\\portrait.mkv",
                "format_name": "matroska,webm",
                "format_long_name": "Matroska / WebM",
                "start_time": "0.000000",
                "duration": "4721.472000",
                "size": "8042322166",
                "bit_rate": "13618000",
                "probe_score": 100
              }
            }
            """;

        FfprobeDocument document =
            JsonSerializer.Deserialize<FfprobeDocument>(
                json,
                JsonOptions) ??
            throw new InvalidOperationException(
                "The ffprobe fixture did not deserialize.");

        TestAssert.Equal(
            2,
            document.Streams!.Count,
            "Both streams should deserialize.");

        FfprobeStream audio =
            document.Streams[1];

        TestAssert.Equal(
            2,
            audio.Channels!.Value,
            "The channel count should deserialize.");

        TestAssert.Null(
            audio.ChannelLayout,
            "An omitted channel layout must remain omitted.");

        TestAssert.Equal(
            "Microphone_vertical",
            audio.Tags!["title"],
            "The metadata title should deserialize unchanged.");
    }
}
