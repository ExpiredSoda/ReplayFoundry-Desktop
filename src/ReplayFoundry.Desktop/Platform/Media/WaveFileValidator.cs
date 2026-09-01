using System.IO;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed record WaveFileInformation(
    int SampleRate,
    int ChannelCount,
    int BitsPerSample,
    long DataByteLength,
    TimeSpan Duration);

internal static class WaveFileValidator
{
    public static double[] ReadPeakEnvelope(string path, int binCount)
    {
        if (binCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binCount));
        }
        using var wave = new PcmWaveFileReader(path);
        return wave.ReadPeakEnvelope(binCount);
    }

    public static WaveFileInformation Validate(
        string path,
        TimeSpan expectedDuration,
        int expectedSampleRate,
        int expectedChannels,
        int expectedBitsPerSample)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                "FFmpeg did not create the extracted WAV file.");
        }

        using var wave = new PcmWaveFileReader(path);
        if (wave.Format != 1 ||
            wave.ChannelCount != expectedChannels ||
            wave.SampleRate != expectedSampleRate ||
            wave.BitsPerSample != expectedBitsPerSample ||
            wave.BlockAlign <= 0 ||
            wave.DataByteLength <= 0)
        {
            throw new InvalidDataException(
                "The extracted WAV must be nonempty 16-kHz mono signed 16-bit PCM.");
        }

        TimeSpan duration =
            TimeSpan.FromSeconds(
                wave.DataByteLength /
                (double)(wave.SampleRate * wave.BlockAlign));
        TimeSpan tolerance =
            TimeSpan.FromSeconds(
                Math.Max(
                    0.25,
                    expectedDuration.TotalSeconds * 0.03));

        if ((duration - expectedDuration).Duration() >
            tolerance)
        {
            throw new InvalidDataException(
                $"The extracted WAV duration {duration:c} differs from the requested " +
                $"{expectedDuration:c} by more than {tolerance:c}.");
        }

        return new WaveFileInformation(
            wave.SampleRate,
            wave.ChannelCount,
            wave.BitsPerSample,
            wave.DataByteLength,
            duration);
    }
}
