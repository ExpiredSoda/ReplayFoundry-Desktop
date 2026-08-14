using System.Buffers.Binary;
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
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 ||
            !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException(
                "The audition waveform source is not a valid RIFF/WAVE file.");
        }

        var samples = new List<short>();
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> chunkId = bytes.AsSpan(offset, 4);
            int chunkLength = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(offset + 4, 4));
            if (chunkLength < 0 || offset + 8L + chunkLength > bytes.Length)
            {
                throw new InvalidDataException(
                    "The audition WAV contains a truncated chunk.");
            }
            int dataOffset = offset + 8;
            if (chunkId.SequenceEqual("data"u8))
            {
                for (int index = 0; index + 1 < chunkLength; index += 2)
                {
                    samples.Add(BinaryPrimitives.ReadInt16LittleEndian(
                        bytes.AsSpan(dataOffset + index, 2)));
                }
            }
            offset = dataOffset + chunkLength + (chunkLength & 1);
        }
        if (samples.Count == 0)
        {
            throw new InvalidDataException(
                "The audition WAV contains no PCM samples.");
        }

        var peaks = new double[binCount];
        for (int bin = 0; bin < binCount; bin++)
        {
            int start = bin * samples.Count / binCount;
            int end = Math.Max(start + 1, (bin + 1) * samples.Count / binCount);
            int peak = 0;
            for (int index = start; index < Math.Min(end, samples.Count); index++)
            {
                peak = Math.Max(peak, Math.Abs((int)samples[index]));
            }
            peaks[bin] = Math.Clamp(peak / 32768d, 0, 1);
        }
        return peaks;
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

        byte[] bytes =
            File.ReadAllBytes(path);

        if (bytes.Length < 44 ||
            !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException(
                "The extracted file is not a valid RIFF/WAVE file.");
        }

        ushort? format = null;
        ushort? channels = null;
        int? sampleRate = null;
        ushort? bits = null;
        int? blockAlign = null;
        long dataLength = 0;

        int offset = 12;

        while (offset + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> chunkId =
                bytes.AsSpan(offset, 4);
            int chunkLength =
                BinaryPrimitives.ReadInt32LittleEndian(
                    bytes.AsSpan(offset + 4, 4));

            if (chunkLength < 0 ||
                offset + 8L + chunkLength > bytes.Length)
            {
                throw new InvalidDataException(
                    "The extracted WAV contains a truncated chunk.");
            }

            int dataOffset = offset + 8;

            if (chunkId.SequenceEqual("fmt "u8))
            {
                if (chunkLength < 16)
                {
                    throw new InvalidDataException(
                        "The extracted WAV format chunk is incomplete.");
                }

                format =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(dataOffset, 2));
                channels =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(dataOffset + 2, 2));
                sampleRate =
                    BinaryPrimitives.ReadInt32LittleEndian(
                        bytes.AsSpan(dataOffset + 4, 4));
                blockAlign =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(dataOffset + 12, 2));
                bits =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(dataOffset + 14, 2));
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                dataLength += chunkLength;
            }

            offset =
                dataOffset +
                chunkLength +
                (chunkLength & 1);
        }

        if (format != 1 ||
            channels != expectedChannels ||
            sampleRate != expectedSampleRate ||
            bits != expectedBitsPerSample ||
            blockAlign is null or <= 0 ||
            dataLength <= 0)
        {
            throw new InvalidDataException(
                "The extracted WAV must be nonempty 16-kHz mono signed 16-bit PCM.");
        }

        TimeSpan duration =
            TimeSpan.FromSeconds(
                dataLength /
                (double)(sampleRate.Value * blockAlign.Value));
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
            sampleRate.Value,
            channels.Value,
            bits.Value,
            dataLength,
            duration);
    }
}
