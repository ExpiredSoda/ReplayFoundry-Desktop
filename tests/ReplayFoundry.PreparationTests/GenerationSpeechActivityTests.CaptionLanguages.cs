using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Audio;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Transcription;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static Task CaptionModelCapabilitiesUseMetadata()
    {
        string directory = Path.Combine(Path.GetTempPath(), "caption-language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string englishNamedMultilingual = Path.Combine(directory, "large-multilingual.bin");
            WriteWhisperHeader(englishNamedMultilingual, 51864);
            AudioTranscriptionModelLanguageCapabilities english = WhisperGgmlLanguageCapabilities.Resolve(englishNamedMultilingual);
            TestAssert.Equal(AudioTranscriptionModelLanguageKind.EnglishOnly, english.Kind,
                "An English-only GGML vocabulary remains English-only after renaming.");
            TestAssert.True(english.SupportsLanguage("en") && !english.SupportsLanguage("es") &&
                !english.SupportsAutomaticLanguage && !english.SupportsTranslationToEnglish,
                "English-only metadata cannot advertise language detection or translation.");

            string multilingualNamedEnglish = Path.Combine(directory, "tiny.en.bin");
            WriteWhisperHeader(multilingualNamedEnglish, 51865);
            AudioTranscriptionModelLanguageCapabilities multilingual = WhisperGgmlLanguageCapabilities.Resolve(multilingualNamedEnglish);
            TestAssert.Equal(99, multilingual.LanguageCount, "The older multilingual vocabulary contains 99 language tokens.");
            TestAssert.True(multilingual.SupportsLanguage("cy") && multilingual.SupportsTranslationToEnglish &&
                !multilingual.SupportsLanguage("yue"), "The catalog must distinguish supported Welsh from unsupported Cantonese in the older vocabulary.");
            TestAssert.True(multilingual.SourceIdentity is not null, "Metadata must retain its bounded model-file identity.");

            string v3Path = Path.Combine(directory, "renamed-model.bin");
            WriteWhisperHeader(v3Path, 51866);
            AudioTranscriptionModelLanguageCapabilities v3 = WhisperGgmlLanguageCapabilities.Resolve(v3Path);
            TestAssert.True(v3.SupportsLanguage("yue") && v3.SupportsTranslationToEnglish,
                "A complete v3 vocabulary includes Cantonese and standard translation support.");
            WriteWhisperHeader(v3Path, 51866, turbo: true);
            AudioTranscriptionModelLanguageCapabilities turbo = WhisperGgmlLanguageCapabilities.Resolve(v3Path);
            TestAssert.True(turbo.SupportsLanguage("yue") && !turbo.SupportsTranslationToEnglish,
                "The Turbo decoder remains multilingual without promising unsupported English translation.");
            TestAssert.False(v3.SourceIdentity == turbo.SourceIdentity, "A changed model header must change capability provenance.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        return Task.CompletedTask;
    }

    private static Task CaptionModelUnknownIsExplicit()
    {
        string directory = Path.Combine(Path.GetTempPath(), "caption-language-unknown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            TestAssert.Equal(AudioTranscriptionModelLanguageKind.Missing,
                WhisperGgmlLanguageCapabilities.Resolve(Path.Combine(directory, "missing-multilingual.bin")).Kind,
                "A missing model must not inherit language capability from its file name.");
            string malformed = Path.Combine(directory, "ggml-base.bin");
            File.WriteAllBytes(malformed, [1, 2, 3]);
            TestAssert.Equal(AudioTranscriptionModelLanguageKind.Unknown, WhisperGgmlLanguageCapabilities.Resolve(malformed).Kind,
                "A truncated model must expose unknown capabilities without throwing.");
            WriteWhisperHeader(malformed, 12345);
            AudioTranscriptionModelLanguageCapabilities unknown = WhisperGgmlLanguageCapabilities.Resolve(malformed);
            TestAssert.False(unknown.IsKnown || unknown.SupportsTranslationToEnglish, "Unknown vocabulary IDs must not imply multilingual support.");
            var choices = GenerationCaptionLanguageCatalog.GetChoices(unknown, GenerationCaptionLanguagePolicy.Spanish);
            TestAssert.Equal(1, choices.Count, "An unknown model keeps the saved choice visible without advertising unverified alternatives.");
            TestAssert.False(choices.Single().IsAvailable, "An unsupported saved choice must visibly block generation.");
            TestAssert.True(!string.IsNullOrWhiteSpace(choices.Single().UnavailableReason), "Unavailable language choices need a usable explanation.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        return Task.CompletedTask;
    }

    private static Task CaptionLanguageSelectionPreservesUnsupportedChoice()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Balanced, [("caption-language-selection.mkv", 1)]);
        var english = new AudioTranscriptionModelLanguageCapabilities(AudioTranscriptionModelLanguageKind.EnglishOnly,
            1, false, "English-only test model.");
        var retained = new GenerationCaptionSourceSelection(request.ReferenceSource.FullPath, 1,
            CaptionAudioContentRole.CreatorCommentary, GenerationCaptionLanguagePolicy.Spanish);
        using var selection = new CaptionAudioSelectionViewModel(request.ReferencePreparedSource, retained,
            languageCapabilities: english);
        TestAssert.Equal(GenerationCaptionLanguagePolicy.Spanish, selection.SelectedLanguage.Value,
            "Loading an unsupported saved choice must not silently replace it with English or Auto.");
        TestAssert.False(selection.IsValid, "Unsupported saved language selection must block progression.");
        TestAssert.True(selection.LanguageValidationMessage?.Contains("does not support", StringComparison.Ordinal) == true,
            "The source selection must explain the actual model-language mismatch.");
        selection.SelectedLanguage = selection.Languages.Single(language => language.Value == GenerationCaptionLanguagePolicy.English);
        TestAssert.True(selection.IsValid, "Choosing supported English must recover the existing stream and role selection.");

        using var fresh = new CaptionAudioSelectionViewModel(request.ReferencePreparedSource, null, languageCapabilities: english);
        TestAssert.Equal(GenerationCaptionLanguagePolicy.English, fresh.SelectedLanguage.Value,
            "A new English-only setup defaults explicitly to English.");
        using var savedAuto = new CaptionAudioSelectionViewModel(request.ReferencePreparedSource,
            new GenerationCaptionSourceSelection(request.ReferenceSource.FullPath, 1,
                CaptionAudioContentRole.CreatorCommentary, GenerationCaptionLanguagePolicy.Auto), languageCapabilities: english);
        TestAssert.False(savedAuto.IsValid, "Previously explicit automatic detection must not silently become English-only detection.");
        return Task.CompletedTask;
    }

    private static Task CaptionLanguageCatalogAndOptionsAreComplete()
    {
        var multilingual = new AudioTranscriptionModelLanguageCapabilities(AudioTranscriptionModelLanguageKind.Multilingual,
            100, true, "Multilingual test model.");
        var options = AudioTranscriptionOptions.CreateDefaults().WithTranslationToEnglish(true);
        var choices = GenerationCaptionLanguageCatalog.GetChoices(multilingual);
        TestAssert.Equal(102, choices.Count, "The complete catalog contains 100 languages, automatic detection, and English translation.");
        TestAssert.Equal(29, (int)GenerationCaptionLanguagePolicy.EnglishTranslation, "Existing persisted enum values must not shift.");
        foreach (GenerationCaptionLanguagePolicy policy in Enum.GetValues<GenerationCaptionLanguagePolicy>())
        {
            AudioTranscriptionOptions resolved = GenerationCaptionLanguageCatalog.ResolveOptions(options, policy, multilingual);
            TestAssert.Equal(policy == GenerationCaptionLanguagePolicy.EnglishTranslation, resolved.TranslateToEnglish,
                "Only an explicit translation choice may retain translation mode.");
        }
        AudioTranscriptionOptions welsh = GenerationCaptionLanguageCatalog.ResolveOptions(options, GenerationCaptionLanguagePolicy.Welsh, multilingual);
        TestAssert.Equal("cy", welsh.RequestedLanguage!.Code, "New catalog languages must use the provider's exact supported code.");
        var english = new AudioTranscriptionModelLanguageCapabilities(AudioTranscriptionModelLanguageKind.EnglishOnly,
            1, false, "English-only test model.");
        TestAssert.Throws<InvalidOperationException>(() =>
                GenerationCaptionLanguageCatalog.ResolveOptions(options, GenerationCaptionLanguagePolicy.EnglishTranslation, english),
            "Unsupported translation must fail explicitly before invoking the provider.");
        var unknown = new AudioTranscriptionModelLanguageCapabilities(AudioTranscriptionModelLanguageKind.Unknown,
            0, false, "Model capabilities are unknown.");
        TestAssert.Throws<InvalidOperationException>(() =>
                GenerationCaptionLanguageCatalog.ResolveOptions(options, GenerationCaptionLanguagePolicy.English, unknown),
            "An unknown model cannot silently claim English support.");
        return Task.CompletedTask;
    }

    private static async Task CaptionProviderRejectsUnsupportedAndChangedModels()
    {
        string directory = Path.Combine(Path.GetTempPath(), "caption-language-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string modelPath = Path.Combine(directory, "speech.bin");
            WriteWhisperHeader(modelPath, 51864);
            var model = new AudioTranscriptionModelSettings(modelPath, "test", "GGML",
                languageCapabilities: WhisperGgmlLanguageCapabilities.Resolve(modelPath));
            // The executable intentionally does not exist: validation must
            // reject unsupported options before probing or starting any process.
            var provider = new WhisperCppTranscriptionProvider(new WhisperCppProviderSettings(
                Path.Combine(directory, "must-not-run.exe"), model));
            var options = AudioTranscriptionOptions.CreateDefaults().WithLanguage(
                AudioTranscriptionLanguageMode.Explicit, new AudioTranscriptionLanguage("es", "Spanish"));
            AudioTranscriptionRequest Request(AudioTranscriptionOptions selected, AudioTranscriptionModelSettings selectedModel) =>
                new("capability-check", Path.Combine(directory, "unused.wav"), TimeSpan.FromSeconds(1),
                    TimeSpan.Zero, TimeSpan.FromSeconds(1), 1, selected, selectedModel);
            WhisperCppTranscriptionException unsupported = await TestAssert.ThrowsAsync<WhisperCppTranscriptionException>(
                () => provider.TranscribeAsync(Request(options, model), CancellationToken.None),
                "An unsupported saved language must fail before process initialization.");
            TestAssert.True(unsupported.Message.Contains("does not support Spanish", StringComparison.Ordinal),
                "The provider should explain the language mismatch instead of silently accepting the CLI's English fallback.");

            WriteWhisperHeader(modelPath, 51865);
            var multilingual = new AudioTranscriptionModelSettings(modelPath, "test", "GGML",
                languageCapabilities: WhisperGgmlLanguageCapabilities.Resolve(modelPath));
            var changedProvider = new WhisperCppTranscriptionProvider(new WhisperCppProviderSettings(
                Path.Combine(directory, "must-not-run.exe"), multilingual));
            WriteWhisperHeader(modelPath, 51864);
            WhisperCppTranscriptionException changed = await TestAssert.ThrowsAsync<WhisperCppTranscriptionException>(
                () => changedProvider.TranscribeAsync(Request(options, multilingual), CancellationToken.None),
                "Replacing a model after setup must invalidate its retained capability identity.");
            TestAssert.True(changed.Message.Contains("model changed", StringComparison.Ordinal),
                "Model replacement must require fresh setup rather than silently changing language behavior.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void WriteWhisperHeader(string path, int vocabulary, bool turbo = false)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        bool v3 = vocabulary == 51866;
        writer.Write(0x67676d6cU);
        writer.Write(vocabulary);
        writer.Write(1500);
        writer.Write(v3 ? 1280 : 512);
        writer.Write(v3 ? 20 : 8);
        writer.Write(v3 ? 32 : 6);
        writer.Write(448);
        writer.Write(v3 ? 1280 : 512);
        writer.Write(v3 ? 20 : 8);
        writer.Write(v3 ? turbo ? 4 : 32 : 6);
        writer.Write(v3 ? 128 : 80);
        writer.Write(1);
        stream.SetLength(1024 * 1024);
    }
}
