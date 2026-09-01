using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.RuntimePacks;
using ReplayFoundry.Desktop.Platform.SpeechActivity;
using ReplayFoundry.Desktop.Platform.Transcription;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record LocalSpeechServices(
    IGenerationCaptionPreparationService? CaptionPreparation,
    IGenerationSpeechActivityService? SpeechActivity);

internal sealed record LocalVisualReviewServices(
    Qwen3VlQualifiedEditorialRuntime? Runtime,
    IVisualSemanticReviewVideoMaterializer? Materializer,
    IGenerationVisualSemanticAnalysisService? Analysis,
    Qwen3VlGroundedMetadataGenerator? EditorialAiProvider,
    GenerationRuntimeCapabilities RuntimeCapabilities);

internal static class LocalIntelligenceComposition
{
    private static readonly TimeSpan QualifiedQwenProcessTimeout =
        TimeSpan.FromMinutes(30);

    public static LocalSpeechServices CreateSpeechServices(
        ReplayFoundryRuntimeEnvironment runtime) =>
        new(
            CreateCaptionPreparationService(runtime),
            CreateSpeechActivityService(runtime));

    public static LocalVisualReviewServices CreateVisualReviewServices(
        ReplayFoundryRuntimeEnvironment runtime,
        LocalSpeechServices speech)
    {
        Qwen3VlQualifiedEditorialRuntime? qwenRuntime =
            CreateQwenRuntime(
                runtime,
                out string? editorialAiUnavailableReason);
        IVisualSemanticReviewVideoMaterializer? materializer =
            qwenRuntime is null
                ? null
                : VisualSemanticReviewVideoMaterializerFactory.CreateDefault();
        IGenerationVisualSemanticAnalysisService? analysis =
            qwenRuntime is null
                ? null
                : CreateVisualSemanticAnalysisService(
                    qwenRuntime,
                    materializer!);
        Qwen3VlGroundedMetadataGenerator? editorialAiProvider =
            qwenRuntime is null
                ? null
                : EditorialComposition.TryCreateAiProvider(
                    qwenRuntime,
                    out editorialAiUnavailableReason);
        bool isEditorialAiAvailable =
            editorialAiProvider?.IsAvailable == true &&
            materializer is not null;
        if (!isEditorialAiAvailable &&
            string.IsNullOrWhiteSpace(editorialAiUnavailableReason))
        {
            editorialAiUnavailableReason =
                GenerationRuntimeCapabilities
                    .DefaultEditorialAiUnavailableReason;
        }

        var capabilities = new GenerationRuntimeCapabilities(
            IsCaptionTranscriptionAvailable:
                speech.CaptionPreparation is not null,
            IsSpeechActivityAvailable:
                speech.SpeechActivity is not null,
            IsVisualSemanticReviewAvailable:
                analysis is not null,
            IsEditorialAiAvailable:
                isEditorialAiAvailable,
            EditorialAiUnavailableReason:
                isEditorialAiAvailable
                    ? null
                    : editorialAiUnavailableReason);
        return new(
            qwenRuntime,
            materializer,
            analysis,
            editorialAiProvider,
            capabilities);
    }

    private static IGenerationCaptionPreparationService?
        CreateCaptionPreparationService(
            ReplayFoundryRuntimeEnvironment runtime)
    {
        string? executable = ExplicitRuntimeEnvironment.Read(
            "REPLAYFOUNDRY_WHISPER_EXE") ??
            runtime.WhisperExecutablePath;
        string? modelPath = ExplicitRuntimeEnvironment.Read(
            "REPLAYFOUNDRY_WHISPER_MODEL") ??
            runtime.WhisperModelPath;
        string? vadModelPath = ExplicitRuntimeEnvironment.Read(
            "REPLAYFOUNDRY_WHISPER_VAD_MODEL") ??
            runtime.WhisperVadModelPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            string.IsNullOrWhiteSpace(modelPath) ||
            !Path.IsPathFullyQualified(executable) ||
            !Path.IsPathFullyQualified(modelPath) ||
            !File.Exists(executable) ||
            !File.Exists(modelPath))
        {
            return null;
        }

        var model = new AudioTranscriptionModelSettings(
            modelPath,
            Path.GetFileNameWithoutExtension(modelPath),
            "whisper.cpp GGML",
            sourceUrlOrNote:
                "Explicit local model path selected by the Replay Foundry user.",
            languageCapabilityDescription:
                "Multilingual capability is declared by the selected model, not inferred from audio metadata.");
        var provider = new WhisperCppTranscriptionProvider(
            new WhisperCppProviderSettings(
                executable,
                model,
                vadModelPath: vadModelPath));
        var options = new AudioTranscriptionOptions(
            AudioTranscriptionLanguageMode.Auto,
            requestedLanguage: null,
            translateToEnglish: false,
            requireSegmentTimestamps: true,
            requestWordTimestamps: true,
            temperature: 0,
            threadCount: null,
            AudioTranscriptionProcessorHint.Auto,
            TimeSpan.FromMinutes(10),
            AudioTranscriptionOutputFormatPolicy.StructuredJson);
        return new GenerationCaptionPreparationService(
            AudioSegmentExtractionFactory.CreateDefault(),
            provider,
            options,
            model);
    }

    private static IGenerationSpeechActivityService?
        CreateSpeechActivityService(
            ReplayFoundryRuntimeEnvironment runtime)
    {
        string? modelPath = ExplicitRuntimeEnvironment.Read(
            "REPLAYFOUNDRY_SILERO_VAD_MODEL") ??
            runtime.SileroModelPath;
        if (string.IsNullOrWhiteSpace(modelPath) ||
            !Path.IsPathFullyQualified(modelPath) ||
            !File.Exists(modelPath))
        {
            return null;
        }

        var file = new FileInfo(modelPath);
        var model = new ModelArtifactManifest(
            "Silero VAD v6.2.1",
            file.FullName,
            ModelArtifactManifest.ComputeSha256(file.FullName),
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            "ONNX",
            "MIT",
            "https://github.com/snakers4/silero-vad/tree/v6.2.1",
            "Speech activity only; no language or semantic classification.");
        return new GenerationSpeechActivityService(
            AudioSegmentExtractionFactory.CreateDefault(),
            new SileroOnnxSpeechActivityProvider(modelPath),
            new GenerationSpeechActivitySettings(
                SpeechActivityOptions.CreateBalancedDefaults(),
                model));
    }

    private static Qwen3VlQualifiedEditorialRuntime? CreateQwenRuntime(
        ReplayFoundryRuntimeEnvironment runtime,
        out string? unavailableReason)
    {
        QwenRuntimeSelection? selection = QwenRuntimeResolver.Resolve(
            runtime.Qwen);
        if (selection is null)
        {
            unavailableReason = runtime.QwenUnavailableReason ??
                GenerationRuntimeCapabilities
                    .DefaultEditorialAiUnavailableReason;
            return null;
        }

        try
        {
            Qwen3VlQualifiedEditorialRuntime result =
                Qwen3VlQualifiedEditorialRuntimeLoader.Load(
                selection.PythonExecutablePath,
                selection.HostScriptPath,
                selection.FfmpegSharedDirectoryPath,
                selection.ModelManifestPath,
                selection.PromptManifestPath,
                selection.QualificationLockPath,
                QualifiedQwenProcessTimeout,
                selection.ModelDirectoryOverride,
                selection.EnvironmentVariables);
            unavailableReason = null;
            return result;
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                IOException or
                InvalidDataException or
                JsonException or
                UnauthorizedAccessException)
        {
            SafeDiagnosticTrace.Write(
                "Qualified local AI runtime is unavailable",
                exception);
            unavailableReason =
                "Local AI title writing could not start because Advanced AI " +
                "is for a different version of Replay Foundry. Update or " +
                "repair Advanced AI, restart Replay Foundry, and try again.";
            return null;
        }
    }

    private static IGenerationVisualSemanticAnalysisService
        CreateVisualSemanticAnalysisService(
            Qwen3VlQualifiedEditorialRuntime runtime,
            IVisualSemanticReviewVideoMaterializer materializer) =>
        new GenerationVisualSemanticAnalysisService(
            runtime.Provider,
            materializer,
            new GenerationVisualSemanticSettings(
                runtime.Prompt,
                runtime.Model,
                runtime.VideoPolicy));
}
