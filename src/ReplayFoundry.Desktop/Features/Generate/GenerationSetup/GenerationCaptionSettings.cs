using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public enum CaptionAudioContentRole
{
    CreatorCommentary,
    GameDialogue,
    MixedSpeech,
    OtherKnownSpeech,
}

public enum GenerationCaptionStylePreset
{
    Clean,
    WordFocus,
    KaraokeSweep,
    Pop,
    HighContrast,
}

public enum GenerationCaptionLanguagePolicy
{
    Auto,
    English,
    Spanish,
    French,
    German,
    Italian,
    Portuguese,
    Japanese,
    Korean,
    Chinese,
    Arabic,
    Hindi,
    Russian,
    Ukrainian,
    Polish,
    Dutch,
    Turkish,
    Vietnamese,
    Indonesian,
    Thai,
    Swedish,
    Danish,
    Norwegian,
    Finnish,
    Greek,
    Hebrew,
    Czech,
    Romanian,
    Hungarian,
    EnglishTranslation,
    // Append only: existing persisted numeric language values must remain stable.
    Catalan,
    Malay,
    Tamil,
    Urdu,
    Croatian,
    Bulgarian,
    Lithuanian,
    Latin,
    Maori,
    Malayalam,
    Welsh,
    Slovak,
    Telugu,
    Persian,
    Latvian,
    Bengali,
    Serbian,
    Azerbaijani,
    Slovenian,
    Kannada,
    Estonian,
    Macedonian,
    Breton,
    Basque,
    Icelandic,
    Armenian,
    Nepali,
    Mongolian,
    Bosnian,
    Kazakh,
    Albanian,
    Swahili,
    Galician,
    Marathi,
    Punjabi,
    Sinhala,
    Khmer,
    Shona,
    Yoruba,
    Somali,
    Afrikaans,
    Occitan,
    Georgian,
    Belarusian,
    Tajik,
    Sindhi,
    Gujarati,
    Amharic,
    Yiddish,
    Lao,
    Uzbek,
    Faroese,
    HaitianCreole,
    Pashto,
    Turkmen,
    Nynorsk,
    Maltese,
    Sanskrit,
    Luxembourgish,
    Burmese,
    Tibetan,
    Tagalog,
    Malagasy,
    Assamese,
    Tatar,
    Hawaiian,
    Lingala,
    Hausa,
    Bashkir,
    Javanese,
    Sundanese,
    Cantonese,
}

public sealed class GenerationCaptionSourceSelection
{
    public GenerationCaptionSourceSelection(
        string sourceFullPath,
        int absoluteAudioStreamIndex,
        CaptionAudioContentRole contentRole,
        GenerationCaptionLanguagePolicy languagePolicy =
            GenerationCaptionLanguagePolicy.Auto)
    {
        if (string.IsNullOrWhiteSpace(sourceFullPath) ||
            !Path.IsPathFullyQualified(sourceFullPath))
        {
            throw new ArgumentException(
                "A caption-audio selection requires a fully qualified source path.",
                nameof(sourceFullPath));
        }
        if (absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(absoluteAudioStreamIndex));
        }
        if (!Enum.IsDefined(contentRole) ||
            !Enum.IsDefined(languagePolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(languagePolicy),
                "Caption source roles and language policies must be defined.");
        }

        SourceFullPath = Path.GetFullPath(sourceFullPath);
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        ContentRole = contentRole;
        LanguagePolicy = languagePolicy;
    }

    public string SourceFullPath { get; }
    public int AbsoluteAudioStreamIndex { get; }
    public CaptionAudioContentRole ContentRole { get; }
    public GenerationCaptionLanguagePolicy LanguagePolicy { get; }
}

public sealed class GenerationCaptionSettings
{
    private readonly ReadOnlyCollection<GenerationCaptionSourceSelection>
        _sourceSelections;

    public GenerationCaptionSettings(
        bool isEnabled,
        GenerationCaptionStylePreset style,
        IEnumerable<GenerationCaptionSourceSelection>? sourceSelections = null,
        StudioCaptionLook? savedLook = null,
        string? savedLookName = null)
    {
        if (!Enum.IsDefined(style))
        {
            throw new ArgumentOutOfRangeException(nameof(style));
        }
        GenerationCaptionSourceSelection[] snapshot =
            sourceSelections?.ToArray() ?? [];
        if (snapshot.Any(static selection => selection is null) ||
            snapshot
                .Select(static selection => selection.SourceFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != snapshot.Length ||
            isEnabled && snapshot.Length == 0 ||
            !isEnabled && snapshot.Length != 0)
        {
            throw new ArgumentException(
                "Enabled captions require one explicit audio-stream selection per participating source; disabled captions retain none.",
                nameof(sourceSelections));
        }

        IsEnabled = isEnabled;
        if (savedLookName is not null && (savedLook is null || string.IsNullOrWhiteSpace(savedLookName) || savedLookName.Trim().Length > 80))
            throw new ArgumentException("A named caption look requires its complete saved settings and a valid name.", nameof(savedLookName));
        Style = savedLook?.CaptionStyle ?? style;
        SavedLook = savedLook;
        SavedLookName = savedLookName?.Trim();
        _sourceSelections = Array.AsReadOnly(snapshot);
    }

    public bool IsEnabled { get; }
    public GenerationCaptionStylePreset Style { get; }
    public StudioCaptionLook? SavedLook { get; }
    public string? SavedLookName { get; }
    public IReadOnlyList<GenerationCaptionSourceSelection>
        SourceSelections => _sourceSelections;

    public GenerationCaptionSourceSelection? FindForSource(
        string sourceFullPath)
    {
        if (string.IsNullOrWhiteSpace(sourceFullPath))
        {
            throw new ArgumentException(
                "A source path is required.",
                nameof(sourceFullPath));
        }

        return _sourceSelections.SingleOrDefault(
            selection =>
                selection.SourceFullPath.Equals(
                    sourceFullPath,
                    StringComparison.OrdinalIgnoreCase));
    }

    public static GenerationCaptionSettings Disabled { get; } =
        new(
            isEnabled: false,
            GenerationCaptionStylePreset.Clean);
}
