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
    public const string SchemaVersion = "2.0";

    public static string Create(MediaProbeResult media)
    {
        ArgumentNullException.ThrowIfNull(media);
        var builder = new StringBuilder(SchemaVersion);
        foreach (AudioStreamInfo stream in media.AudioStreams
                     .OrderBy(static value => value.Index))
        {
            builder.Append('|');
            AppendComponent(
                builder,
                stream.Index.ToString(CultureInfo.InvariantCulture));
            AppendComponent(builder, stream.CodecName);
            AppendComponent(
                builder,
                stream.SampleRate?.ToString(CultureInfo.InvariantCulture));
            AppendComponent(
                builder,
                stream.Channels?.ToString(CultureInfo.InvariantCulture));
            AppendComponent(builder, stream.ChannelLayout);
            AppendComponent(builder, stream.Title);
            AppendComponent(builder, stream.Language);
            AppendComponent(
                builder,
                stream.IsDefault ? "DEFAULT" : "SECONDARY");
        }
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendComponent(
        StringBuilder builder,
        string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "?"
            : string.Join(
                    ' ',
                    value.Normalize(NormalizationForm.FormKC)
                        .Split(
                            (char[]?)null,
                            StringSplitOptions.RemoveEmptyEntries))
                .ToUpperInvariant();
        builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture))
            .Append('#')
            .Append(normalized);
    }
}
