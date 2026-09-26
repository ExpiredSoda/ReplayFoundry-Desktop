namespace ReplayFoundry.Desktop.Media.Transcription;

public enum AudioTranscriptionModelLanguageKind
{
    Missing,
    Unknown,
    EnglishOnly,
    Multilingual,
}

public sealed class AudioTranscriptionModelLanguageCapabilities
{
    public AudioTranscriptionModelLanguageCapabilities(AudioTranscriptionModelLanguageKind kind,
        int languageCount, bool supportsTranslationToEnglish, string description, string? sourceIdentity = null)
    {
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(description) ||
            kind == AudioTranscriptionModelLanguageKind.EnglishOnly && languageCount != 1 ||
            kind == AudioTranscriptionModelLanguageKind.Multilingual && languageCount is not (99 or 100) ||
            kind is AudioTranscriptionModelLanguageKind.Missing or AudioTranscriptionModelLanguageKind.Unknown && languageCount != 0 ||
            supportsTranslationToEnglish && kind != AudioTranscriptionModelLanguageKind.Multilingual)
        {
            throw new ArgumentException("Transcription language capabilities must describe a known bounded model vocabulary or an explicit unavailable state.");
        }
        Kind = kind;
        LanguageCount = languageCount;
        SupportsTranslationToEnglish = supportsTranslationToEnglish;
        Description = description.Trim();
        SourceIdentity = sourceIdentity;
    }

    public AudioTranscriptionModelLanguageKind Kind { get; }
    public int LanguageCount { get; }
    public bool SupportsTranslationToEnglish { get; }
    public bool SupportsAutomaticLanguage => Kind == AudioTranscriptionModelLanguageKind.Multilingual;
    public bool IsKnown => Kind is AudioTranscriptionModelLanguageKind.EnglishOnly or AudioTranscriptionModelLanguageKind.Multilingual;
    public string Description { get; }
    public string? SourceIdentity { get; }

    public bool SupportsLanguage(string code) => Kind == AudioTranscriptionModelLanguageKind.EnglishOnly
        ? code.Equals("en", StringComparison.OrdinalIgnoreCase)
        : Kind == AudioTranscriptionModelLanguageKind.Multilingual && WhisperLanguageCatalog.Languages.Any(language =>
            language.TokenIndex < LanguageCount && language.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public string? GetBlockingReason(AudioTranscriptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!IsKnown) return Description;
        if (options.TranslateToEnglish && !SupportsTranslationToEnglish)
            return "The installed speech model does not support translation to English. Choose transcription or install a translation-capable multilingual model.";
        if (options.LanguageMode == AudioTranscriptionLanguageMode.Auto && !SupportsAutomaticLanguage)
            return "The installed speech model is English-only. Choose English instead of automatic language detection.";
        if (options.LanguageMode == AudioTranscriptionLanguageMode.Explicit)
        {
            AudioTranscriptionLanguage language = options.RequestedLanguage!;
            if (!SupportsLanguage(language.Code))
                return $"The installed speech model does not support {language.DisplayName ?? language.Code}. Choose a supported language or install a compatible multilingual model.";
        }
        return null;
    }

    public static AudioTranscriptionModelLanguageCapabilities Missing { get; } = new(
        AudioTranscriptionModelLanguageKind.Missing, 0, false,
        "The speech model is unavailable. Install or repair Advanced AI before creating spoken captions.");
}
