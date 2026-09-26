using System.IO;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.Desktop.Platform.SpeechActivity;

internal sealed class Pcm16MonoWaveReader : IDisposable
{
    private readonly PcmWaveFileReader _reader;

    public Pcm16MonoWaveReader(string path)
    {
        _reader = new PcmWaveFileReader(path);
        try
        {
            if (_reader.Format != 1 ||
                _reader.ChannelCount != 1 ||
                _reader.BitsPerSample != 16 ||
                _reader.DataByteLength <= 0 ||
                _reader.DataByteLength % 2 != 0)
            {
                throw new InvalidDataException(
                    "Speech-activity input must be non-empty mono signed 16-bit PCM WAV.");
            }

            SampleRate = _reader.SampleRate;
            TotalSamples = _reader.Pcm16SampleCount;
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
        => _reader.ReadNormalizedMonoSamples(destination);

    public void Dispose()
    {
        _reader.Dispose();
    }
}
