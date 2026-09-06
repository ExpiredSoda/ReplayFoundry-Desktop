using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Platform.Transcription;

public static class WhisperGgmlLanguageCapabilities
{
    // GGML magic + eleven little-endian int32 hyperparameters. This reads
    // metadata only; the provider still hashes and validates the complete model.
    // Header/vocabulary contract: github.com/ggml-org/whisper.cpp/blob/v1.7.5/src/whisper.cpp
    // Turbo translation limitation: github.com/openai/whisper#available-models-and-languages
    private const uint Magic = 0x67676d6c;
    private const int HeaderBytes = 48;

    public static AudioTranscriptionModelLanguageCapabilities Resolve(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !Path.IsPathFullyQualified(modelPath) || !File.Exists(modelPath))
            return AudioTranscriptionModelLanguageCapabilities.Missing;
        try
        {
            var before = new FileInfo(modelPath);
            before.Refresh();
            using var input = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(input);
            if (input.Length <= HeaderBytes || reader.ReadUInt32() != Magic)
                return Unknown("The selected speech model has no supported Whisper GGML header. Its language support could not be verified.");
            int vocabulary = reader.ReadInt32();
            int audioContext = reader.ReadInt32();
            int audioState = reader.ReadInt32();
            int audioHeads = reader.ReadInt32();
            int audioLayers = reader.ReadInt32();
            int textContext = reader.ReadInt32();
            int textState = reader.ReadInt32();
            int textHeads = reader.ReadInt32();
            int textLayers = reader.ReadInt32();
            int melBands = reader.ReadInt32();
            int weightType = reader.ReadInt32();
            bool knownEncoder = (audioState, audioHeads, audioLayers) is
                (384, 6, 4) or (512, 8, 6) or (768, 12, 12) or (1024, 16, 24) or (1280, 20, 32);
            bool turbo = vocabulary == 51866 && audioLayers == 32 && textLayers == 4 && melBands == 128;
            if (!knownEncoder || audioContext != 1500 || textContext != 448 ||
                textState != audioState || textHeads != audioHeads ||
                textLayers != audioLayers && !turbo ||
                vocabulary is not (51864 or 51865 or 51866) ||
                melBands != (vocabulary == 51866 ? 128 : 80) ||
                weightType < 0 || weightType > 9999)
                return Unknown("The speech model's GGML architecture or vocabulary is unrecognized. Its language support could not be verified.");
            // Reject a partial header/metadata-only file without loading weights.
            // All supported Whisper architectures are much larger than this.
            if (input.Length < 1024 * 1024)
                return Unknown("The speech model appears incomplete. Repair Advanced AI before creating spoken captions.");
            input.Position = 0;
            string headerHash = Convert.ToHexString(SHA256.HashData(reader.ReadBytes(HeaderBytes)));
            var after = new FileInfo(modelPath);
            if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
                return Unknown("The speech model changed while its language metadata was being inspected. Reopen the setup.");
            string identity = string.Join("|", Path.GetFullPath(modelPath).ToUpperInvariant(),
                before.Length.ToString(CultureInfo.InvariantCulture), before.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), headerHash);
            bool englishOnly = vocabulary == 51864;
            int count = englishOnly ? 1 : vocabulary - 51766;
            string description = englishOnly
                ? "The installed GGML model declares English-only speech transcription."
                : $"The installed GGML model declares {count} transcription languages." +
                    (turbo ? " Its Turbo decoder does not support translation to English." : " Translation to English is available.");
            return new(englishOnly ? AudioTranscriptionModelLanguageKind.EnglishOnly : AudioTranscriptionModelLanguageKind.Multilingual,
                count, !englishOnly && !turbo, description, identity);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Unknown("The speech model's language metadata could not be read. Check the model file or repair Advanced AI.");
        }
    }

    private static AudioTranscriptionModelLanguageCapabilities Unknown(string reason) =>
        new(AudioTranscriptionModelLanguageKind.Unknown, 0, false, reason);
}
