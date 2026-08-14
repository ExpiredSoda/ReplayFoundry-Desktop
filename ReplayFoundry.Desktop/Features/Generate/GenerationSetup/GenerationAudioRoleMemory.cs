using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public sealed record RememberedGenerationAudioRole(
    int AbsoluteAudioStreamIndex,
    CaptionAudioContentRole ContentRole,
    GenerationCaptionLanguagePolicy LanguagePolicy);

public interface IGenerationAudioRoleMemory
{
    RememberedGenerationAudioRole? Find(PreparedGenerationSource source);

    void Remember(
        IEnumerable<PreparedGenerationSource> sources,
        IEnumerable<GenerationCaptionSourceSelection> selections);
}

public static class GenerationAudioLayoutFingerprint
{
    public const string SchemaVersion = "1.0";

    public static string Create(MediaProbeResult media)
    {
        ArgumentNullException.ThrowIfNull(media);
        var builder = new StringBuilder(SchemaVersion);
        foreach (AudioStreamInfo stream in media.AudioStreams
                     .OrderBy(static value => value.Index))
        {
            builder.Append('|')
                .Append(stream.Index.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(stream.CodecName.ToUpperInvariant())
                .Append(':').Append(stream.SampleRate?.ToString(CultureInfo.InvariantCulture) ?? "?")
                .Append(':').Append(stream.Channels?.ToString(CultureInfo.InvariantCulture) ?? "?");
        }
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
