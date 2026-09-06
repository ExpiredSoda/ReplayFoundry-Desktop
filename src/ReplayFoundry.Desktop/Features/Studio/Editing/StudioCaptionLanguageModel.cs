using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

/// <summary>Feature-facing choices retain the verified speech model's policy.</summary>
public sealed class StudioCaptionLanguageModel(AudioTranscriptionModelLanguageCapabilities? capabilities)
{
    public string? Description => capabilities?.Description;
    public GenerationCaptionLanguagePolicy DefaultLanguage => GenerationCaptionLanguageCatalog.DefaultFor(capabilities);
    public IReadOnlyList<SelectionOption<GenerationCaptionLanguagePolicy>> GetChoices(GenerationCaptionLanguagePolicy selected) =>
        GenerationCaptionLanguageCatalog.GetChoices(capabilities, selected);
    public string? GetUnavailableReason(GenerationCaptionLanguagePolicy selected) =>
        GenerationCaptionLanguageCatalog.GetUnavailableReason(selected, capabilities);
}
