using System.IO;

namespace ReplayFoundry.Desktop.Platform.SpeechActivity;

internal sealed class Pcm16MonoWaveReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly long _dataEnd;

    public Pcm16MonoWaveReader(string path)
    {
        _stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        _reader = new BinaryReader(_stream);

        try
        {

            if (ReadFourCc() != "RIFF" ||
                _reader.ReadUInt32() > _stream.Length - 8 ||
                ReadFourCc() != "WAVE")
            {
                throw new InvalidDataException(
                    "Speech-activity input is not a valid RIFF/WAVE file.");
            }

            ushort? format = null;
            ushort? channels = null;
            int? sampleRate = null;
            ushort? bits = null;
            long? dataStart = null;
            long? dataLength = null;

            while (_stream.Position + 8 <= _stream.Length)
            {
                string chunkId = ReadFourCc();
                uint chunkLength = _reader.ReadUInt32();
                long chunkStart = _stream.Position;
                long next = checked(chunkStart + chunkLength + (chunkLength & 1));
                if (next > _stream.Length)
                {
                    throw new InvalidDataException(
                        "The speech-activity WAV contains a truncated chunk.");
                }

                if (chunkId == "fmt ")
                {
                    if (chunkLength < 16)
                    {
                        throw new InvalidDataException(
                            "The speech-activity WAV format chunk is incomplete.");
                    }

                    format = _reader.ReadUInt16();
                    channels = _reader.ReadUInt16();
                    sampleRate = _reader.ReadInt32();
                    _reader.ReadUInt32();
                    _reader.ReadUInt16();
                    bits = _reader.ReadUInt16();
                }
                else if (chunkId == "data")
                {
                    dataStart = chunkStart;
                    dataLength = chunkLength;
                }

                _stream.Position = next;
            }

            if (format != 1 ||
                channels != 1 ||
                bits != 16 ||
                sampleRate is null ||
                dataStart is null ||
                dataLength is null ||
                dataLength.Value <= 0 ||
                dataLength.Value % 2 != 0)
            {
                throw new InvalidDataException(
                    "Speech-activity input must be non-empty mono signed 16-bit PCM WAV.");
            }

            SampleRate = sampleRate.Value;
            TotalSamples = dataLength.Value / 2;
            _stream.Position = dataStart.Value;
            _dataEnd = dataStart.Value + dataLength.Value;
        }
        catch
        {
            _reader.Dispose();
            throw;
        }
    }

    public int SampleRate { get; }

    public long TotalSamples { get; }

    public int ReadNormalizedSamples(float[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        int count = 0;
        while (count < destination.Length && _stream.Position + 2 <= _dataEnd)
        {
            destination[count++] = _reader.ReadInt16() / 32768f;
        }

        return count;
    }

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }

    private string ReadFourCc() =>
        new(_reader.ReadChars(4));
}
