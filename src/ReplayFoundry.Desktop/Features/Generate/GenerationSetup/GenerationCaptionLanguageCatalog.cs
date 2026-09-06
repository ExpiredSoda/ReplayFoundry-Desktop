using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public static class GenerationCaptionLanguageCatalog
{
    private static readonly IReadOnlyDictionary<GenerationCaptionLanguagePolicy, WhisperLanguageDefinition> Definitions =
        WhisperLanguageCatalog.Languages.ToDictionary(language => Enum.Parse<GenerationCaptionLanguagePolicy>(language.PolicyName));

    public static GenerationCaptionLanguagePolicy DefaultFor(AudioTranscriptionModelLanguageCapabilities? capabilities) =>
        capabilities?.Kind == AudioTranscriptionModelLanguageKind.EnglishOnly
            ? GenerationCaptionLanguagePolicy.English : GenerationCaptionLanguagePolicy.Auto;

    public static IReadOnlyList<SelectionOption<GenerationCaptionLanguagePolicy>> GetChoices(
        AudioTranscriptionModelLanguageCapabilities? capabilities, GenerationCaptionLanguagePolicy? retainedSelection = null)
    {
        GenerationCaptionLanguagePolicy selected = retainedSelection ?? DefaultFor(capabilities);
        return Enum.GetValues<GenerationCaptionLanguagePolicy>()
            .Select(policy => CreateChoice(policy, capabilities))
            .Where(choice => choice.IsAvailable || choice.Value == selected)
            .OrderBy(static choice => choice.Value is GenerationCaptionLanguagePolicy.Auto ? 0 :
                choice.Value is GenerationCaptionLanguagePolicy.EnglishTranslation ? 2 : 1)
            .ThenBy(static choice => choice.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static string? GetUnavailableReason(GenerationCaptionLanguagePolicy policy,
        AudioTranscriptionModelLanguageCapabilities? capabilities)
    {
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        // Unspecified capabilities are retained for generic injected providers.
        // Production composition always supplies a resolved known/unknown state.
        if (capabilities is null) return null;
        AudioTranscriptionOptions options = ApplyPolicy(AudioTranscriptionOptions.CreateDefaults(), policy);
        return capabilities.GetBlockingReason(options);
    }

    public static AudioTranscriptionOptions ResolveOptions(AudioTranscriptionOptions defaults,
        GenerationCaptionLanguagePolicy policy, AudioTranscriptionModelLanguageCapabilities? capabilities)
    {
        string? reason = GetUnavailableReason(policy, capabilities);
        if (reason is not null) throw new InvalidOperationException(reason);
        return ApplyPolicy(defaults, policy);
    }

    public static string GetDisplayName(GenerationCaptionLanguagePolicy policy) => policy switch
    {
        GenerationCaptionLanguagePolicy.Auto => "Detect automatically",
        GenerationCaptionLanguagePolicy.EnglishTranslation => "Translate speech to English",
        _ => Definitions[policy].DisplayName,
    };

    private static SelectionOption<GenerationCaptionLanguagePolicy> CreateChoice(
        GenerationCaptionLanguagePolicy policy, AudioTranscriptionModelLanguageCapabilities? capabilities)
    {
        string? reason = GetUnavailableReason(policy, capabilities);
        string description = policy switch
        {
            GenerationCaptionLanguagePolicy.Auto => "Detect the spoken language using the installed multilingual model.",
            GenerationCaptionLanguagePolicy.EnglishTranslation => "Translate recognized speech into English using the installed multilingual model.",
            _ => $"Transcribe speech in {GetDisplayName(policy)}.",
        };
        return new(policy, GetDisplayName(policy), reason ?? description, reason is null, reason);
    }

    private static AudioTranscriptionOptions ApplyPolicy(AudioTranscriptionOptions defaults, GenerationCaptionLanguagePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        // An old translation setting must never leak into an explicit transcription choice.
        AudioTranscriptionOptions options = defaults.WithTranslationToEnglish(policy == GenerationCaptionLanguagePolicy.EnglishTranslation);
        return policy is GenerationCaptionLanguagePolicy.Auto or GenerationCaptionLanguagePolicy.EnglishTranslation
            ? options.WithLanguage(AudioTranscriptionLanguageMode.Auto, null)
            : options.WithLanguage(AudioTranscriptionLanguageMode.Explicit,
                new AudioTranscriptionLanguage(Definitions[policy].Code, Definitions[policy].DisplayName));
    }
}
