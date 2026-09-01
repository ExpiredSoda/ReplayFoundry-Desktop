using System.Buffers.Binary;
using System.IO;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class PcmWaveFileReader : IDisposable
{
    private const int BufferSize = 16 * 1024;
    private readonly FileStream _stream;
    private readonly WaveDataChunk[] _dataChunks;
    private readonly byte[] _ioBuffer = new byte[BufferSize];
    private short[] _sampleBuffer = [];
    private int _chunkIndex = -1;
    private long _chunkBytesRemaining;

    internal PcmWaveFileReader(string path)
    {
        _stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);
        try
        {
            PcmWaveHeader header = ReadHeader(_stream);
            Format = header.Format;
            ChannelCount = header.ChannelCount;
            SampleRate = header.SampleRate;
            BlockAlign = header.BlockAlign;
            BitsPerSample = header.BitsPerSample;
            _dataChunks = header.DataChunks;
            DataByteLength = header.DataByteLength;
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    internal ushort Format { get; }
    internal int ChannelCount { get; }
    internal int SampleRate { get; }
    internal int BlockAlign { get; }
    internal int BitsPerSample { get; }
    internal long DataByteLength { get; }
    internal long Pcm16SampleCount => DataByteLength / 2;

    internal int ReadNormalizedMonoSamples(float[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (Format != 1 || ChannelCount != 1 || BitsPerSample != 16)
        {
            throw new InvalidDataException(
                "Normalized samples require mono signed 16-bit PCM WAV audio.");
        }

        if (_sampleBuffer.Length < destination.Length)
        {
            _sampleBuffer = new short[destination.Length];
        }
        int count = ReadPcm16Samples(
            _sampleBuffer.AsSpan(0, destination.Length));
        for (int index = 0; index < count; index++)
        {
            destination[index] = _sampleBuffer[index] / 32768f;
        }
        return count;
    }

    internal double[] ReadPeakEnvelope(int binCount)
    {
        if (binCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binCount));
        }
        if (Format != 1 || BitsPerSample != 16 || Pcm16SampleCount <= 0)
        {
            throw new InvalidDataException(
                "The audition WAV contains no signed 16-bit PCM samples.");
        }

        ResetData();
        return Pcm16SampleCount < binCount
            ? ReadSmallPeakEnvelope(binCount)
            : ReadStreamingPeakEnvelope(binCount);
    }

    public void Dispose() => _stream.Dispose();

    private double[] ReadStreamingPeakEnvelope(int binCount)
    {
        var peaks = new int[binCount];
        var samples = new short[BufferSize / 2];
        long sampleIndex = 0;
        int count;
        while ((count = ReadPcm16Samples(samples)) > 0)
        {
            for (int index = 0; index < count; index++, sampleIndex++)
            {
                int bin = Math.Min(
                    binCount - 1,
                    (int)(sampleIndex * (double)binCount /
                        Pcm16SampleCount));
                peaks[bin] = Math.Max(
                    peaks[bin],
                    Math.Abs((int)samples[index]));
            }
        }
        return NormalizePeaks(peaks);
    }

    private double[] ReadSmallPeakEnvelope(int binCount)
    {
        var samples = new short[checked((int)Pcm16SampleCount)];
        int count = ReadPcm16Samples(samples);
        var peaks = new int[binCount];
        for (int bin = 0; bin < binCount; bin++)
        {
            int start = bin * count / binCount;
            int end = Math.Max(
                start + 1,
                (bin + 1) * count / binCount);
            for (int index = start; index < Math.Min(end, count); index++)
            {
                peaks[bin] = Math.Max(
                    peaks[bin],
                    Math.Abs((int)samples[index]));
            }
        }
        return NormalizePeaks(peaks);
    }

    private int ReadPcm16Samples(Span<short> destination)
    {
        if (BitsPerSample != 16 || DataByteLength % 2 != 0)
        {
            throw new InvalidDataException(
                "The WAV PCM data is not aligned to signed 16-bit samples.");
        }

        int written = 0;
        while (written < destination.Length && MoveToReadableChunk())
        {
            int byteCount = checked((int)Math.Min(
                Math.Min(_ioBuffer.Length, _chunkBytesRemaining),
                (destination.Length - written) * 2L));
            byteCount -= byteCount & 1;
            if (byteCount == 0)
            {
                break;
            }
            _stream.ReadExactly(_ioBuffer.AsSpan(0, byteCount));
            _chunkBytesRemaining -= byteCount;
            int sampleCount = byteCount / 2;
            for (int index = 0; index < sampleCount; index++)
            {
                destination[written++] =
                    BinaryPrimitives.ReadInt16LittleEndian(
                        _ioBuffer.AsSpan(index * 2, 2));
            }
        }
        return written;
    }

    private bool MoveToReadableChunk()
    {
        while (_chunkBytesRemaining == 0)
        {
            if (++_chunkIndex >= _dataChunks.Length)
            {
                return false;
            }
            WaveDataChunk chunk = _dataChunks[_chunkIndex];
            _stream.Position = chunk.Offset;
            _chunkBytesRemaining = chunk.Length;
        }
        return true;
    }

    private void ResetData()
    {
        _chunkIndex = -1;
        _chunkBytesRemaining = 0;
    }

    private static PcmWaveHeader ReadHeader(Stream stream)
    {
        try
        {
            return ReadHeaderCore(stream);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException(
                "The WAV header or one of its chunks is truncated.",
                exception);
        }
    }

    private static PcmWaveHeader ReadHeaderCore(Stream stream)
    {
        Span<byte> riff = stackalloc byte[12];
        stream.ReadExactly(riff);
        long riffEnd = checked(
            8L + BinaryPrimitives.ReadUInt32LittleEndian(riff[4..8]));
        if (!riff[..4].SequenceEqual("RIFF"u8) ||
            !riff[8..12].SequenceEqual("WAVE"u8) ||
            riffEnd < 12 || riffEnd > stream.Length)
        {
            throw new InvalidDataException(
                "The audio source is not a valid RIFF/WAVE file.");
        }

        ushort? format = null;
        ushort? channels = null;
        int? sampleRate = null;
        ushort? blockAlign = null;
        ushort? bits = null;
        var dataChunks = new List<WaveDataChunk>();
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> formatDetails = stackalloc byte[16];
        while (stream.Position + 8 <= riffEnd)
        {
            stream.ReadExactly(chunkHeader);
            long length = BinaryPrimitives.ReadUInt32LittleEndian(
                chunkHeader[4..]);
            long offset = stream.Position;
            long next = checked(offset + length + (length & 1));
            if (next > riffEnd)
            {
                throw new InvalidDataException(
                    "The WAV contains a truncated chunk.");
            }

            if (chunkHeader[..4].SequenceEqual("fmt "u8))
            {
                if (length < 16)
                {
                    throw new InvalidDataException(
                        "The WAV format chunk is incomplete.");
                }
                stream.ReadExactly(formatDetails);
                format = BinaryPrimitives.ReadUInt16LittleEndian(formatDetails);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(formatDetails[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(formatDetails[4..]);
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(formatDetails[12..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(formatDetails[14..]);
            }
            else if (chunkHeader[..4].SequenceEqual("data"u8) && length > 0)
            {
                dataChunks.Add(new WaveDataChunk(offset, length));
            }
            stream.Position = next;
        }

        if (format is null || channels is null or 0 ||
            sampleRate is null or <= 0 || blockAlign is null or 0 ||
            bits is null or 0 || dataChunks.Count == 0)
        {
            throw new InvalidDataException(
                "The WAV is missing required format or PCM data.");
        }
        if (dataChunks.Any(chunk =>
                chunk.Length % blockAlign.Value != 0 ||
                bits.Value == 16 && chunk.Length % 2 != 0))
        {
            throw new InvalidDataException(
                "The WAV PCM data chunks are not aligned to complete sample frames.");
        }
        long dataLength = dataChunks.Sum(static chunk => chunk.Length);
        return new PcmWaveHeader(
            format.Value,
            channels.Value,
            sampleRate.Value,
            blockAlign.Value,
            bits.Value,
            dataChunks.ToArray(),
            dataLength);
    }

    private static double[] NormalizePeaks(int[] peaks) =>
        peaks.Select(static peak =>
                Math.Clamp(peak / 32768d, 0, 1))
            .ToArray();

    private readonly record struct WaveDataChunk(long Offset, long Length);

    private sealed record PcmWaveHeader(
        ushort Format,
        ushort ChannelCount,
        int SampleRate,
        ushort BlockAlign,
        ushort BitsPerSample,
        WaveDataChunk[] DataChunks,
        long DataByteLength);
}
