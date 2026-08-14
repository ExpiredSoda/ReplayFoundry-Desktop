using System;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Guidance;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public sealed class GenerationSetupDraft
{
    public GenerationSetupDraft(
        GenerationSetupRequest request,
        GenerationSetupOptions? initialOptions = null,
        IGenerationGameContextMemory? gameContextMemory = null,
        GenerationAnalysisDepth? defaultAnalysisDepth = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (initialOptions is not null &&
            initialOptions.Mode != request.Mode)
        {
            throw new ArgumentException(
                "The existing setup does not match the current generation mode.",
                nameof(initialOptions));
        }

        Request = request;

        DetectionMethod =
            initialOptions?.DetectionMethod ??
            DetectionMethod.Heuristics;

        AudioSelectionMode =
            initialOptions?.AudioSelectionMode ??
            AudioSelectionMode.Auto;

        DesiredResultCount =
            initialOptions?.DesiredResultCount ??
            GetDefaultResultCount(request.Mode);

        QualityThreshold =
            initialOptions?.QualityThreshold ??
            70;

        ContentEmphasis =
            initialOptions?.ContentEmphasis ??
            ContentEmphasis.Balanced;

        ClipFulfillmentPreference =
            initialOptions?.ClipFulfillmentPreference ??
            ClipFulfillmentPreference.FillRequestedCount;

        ResultCountMode =
            initialOptions?.ResultCountMode ??
            GenerationResultCountMode.Exact;

        AnalysisDepth =
            initialOptions?.AnalysisDepth ??
            defaultAnalysisDepth ??
            GenerationAnalysisDepth.Balanced;

        MomentGuidance =
            initialOptions?.MomentGuidance ??
            GenerationMomentGuidance.Empty;

        CaptionSettings =
            initialOptions?.CaptionSettings ??
            GenerationCaptionSettings.Disabled;

        GameContextSettings =
            initialOptions?.GameContextSettings.Sources.Count > 0
                ? initialOptions.GameContextSettings
                : new GenerationGameContextSettings(
                    request.PreparedSources.Select(source =>
                        gameContextMemory?.Find(source.Media.FullPath) ??
                        GenerationSourceGameContext.CreatePathHint(
                            source.Media.FullPath)));

        MaximumClipDuration = initialOptions?.MaximumClipDuration ??
            (request.Mode == GenerationMode.Montage
                ? TimeSpan.FromSeconds(12)
                : TimeSpan.FromSeconds(60));
    }

    public GenerationSetupRequest Request { get; }

    public DetectionMethod DetectionMethod { get; private set; }

    public AudioSelectionMode AudioSelectionMode { get; private set; }

    public int DesiredResultCount { get; private set; }

    public double QualityThreshold { get; private set; }

    public ContentEmphasis ContentEmphasis { get; private set; }

    public ClipFulfillmentPreference ClipFulfillmentPreference
    {
        get;
        private set;
    }

    public GenerationResultCountMode ResultCountMode { get; private set; }

    public GenerationAnalysisDepth AnalysisDepth { get; private set; }

    public GenerationMomentGuidance MomentGuidance { get; private set; }

    public GenerationCaptionSettings CaptionSettings { get; private set; }

    public GenerationGameContextSettings GameContextSettings
    {
        get;
        private set;
    }

    public TimeSpan MaximumClipDuration { get; private set; }

    public void UpdateMaximumClipDuration(TimeSpan value)
    {
        if (value < GenerationSetupOptions.MinimumMaximumClipDuration ||
            value > GenerationSetupOptions.MaximumMaximumClipDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The maximum candidate length must be between 10 seconds and 3 minutes.");
        }

        MaximumClipDuration = value;
    }

    public void UpdateCaptionSettings(
        GenerationCaptionSettings captionSettings)
    {
        ArgumentNullException.ThrowIfNull(captionSettings);
        CaptionSettings = captionSettings;
    }

    public void UpdateMomentGuidance(
        GenerationMomentGuidance momentGuidance)
    {
        ArgumentNullException.ThrowIfNull(momentGuidance);
        MomentGuidance = momentGuidance;
    }

    public void UpdateGameContextSettings(
        GenerationGameContextSettings gameContextSettings)
    {
        ArgumentNullException.ThrowIfNull(gameContextSettings);
        if (gameContextSettings.Sources.Count != Request.PreparedSources.Count ||
            Request.PreparedSources.Where((source, index) =>
                    !source.Media.FullPath.Equals(
                        gameContextSettings.Sources[index].SourceFullPath,
                        StringComparison.OrdinalIgnoreCase))
                .Any())
        {
            throw new ArgumentException(
                "Game context must preserve every prepared source in order.",
                nameof(gameContextSettings));
        }
        GameContextSettings = gameContextSettings;
    }

    public void UpdateDetectionMethod(
        DetectionMethod detectionMethod)
    {
        if (!Enum.IsDefined(
                typeof(DetectionMethod),
                detectionMethod))
        {
            throw new ArgumentOutOfRangeException(
                nameof(detectionMethod),
                detectionMethod,
                "The detection method is not defined.");
        }

        DetectionMethod = detectionMethod;
    }

    public void UpdateAudioSelectionMode(
        AudioSelectionMode audioSelectionMode)
    {
        if (!Enum.IsDefined(
                typeof(AudioSelectionMode),
                audioSelectionMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioSelectionMode),
                audioSelectionMode,
                "The audio-selection mode is not defined.");
        }

        AudioSelectionMode = audioSelectionMode;
    }

    public void UpdateClipGoals(
        int desiredResultCount,
        double qualityThreshold,
        ContentEmphasis contentEmphasis,
        ClipFulfillmentPreference clipFulfillmentPreference,
        GenerationResultCountMode resultCountMode =
            GenerationResultCountMode.Exact)
    {
        if (desiredResultCount is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredResultCount),
                desiredResultCount,
                "The desired result count must be between 1 and 30.");
        }

        if (qualityThreshold is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(qualityThreshold),
                qualityThreshold,
                "The quality threshold must be between 0 and 100.");
        }

        if (!Enum.IsDefined(
                typeof(ContentEmphasis),
                contentEmphasis))
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentEmphasis),
                contentEmphasis,
                "The content emphasis is not defined.");
        }
        if (!Enum.IsDefined(resultCountMode))
        {
            throw new ArgumentOutOfRangeException(nameof(resultCountMode));
        }
        if (resultCountMode == GenerationResultCountMode.Auto)
        {
            desiredResultCount = 30;
            clipFulfillmentPreference =
                ClipFulfillmentPreference.QualityFirst;
        }

        if (!Enum.IsDefined(
                typeof(ClipFulfillmentPreference),
                clipFulfillmentPreference))
        {
            throw new ArgumentOutOfRangeException(
                nameof(clipFulfillmentPreference),
                clipFulfillmentPreference,
                "The clip-fulfillment preference is not defined.");
        }

        DesiredResultCount = desiredResultCount;
        QualityThreshold = qualityThreshold;
        ContentEmphasis = contentEmphasis;
        ClipFulfillmentPreference = clipFulfillmentPreference;
        ResultCountMode = resultCountMode;
    }

    public void UpdateAnalysisDepth(
        GenerationAnalysisDepth analysisDepth)
    {
        if (!Enum.IsDefined(analysisDepth))
        {
            throw new ArgumentOutOfRangeException(nameof(analysisDepth));
        }
        AnalysisDepth = analysisDepth;
    }

    public GenerationSetupOptions CreateOptions()
    {
        return new GenerationSetupOptions(
            Request.Mode,
            DetectionMethod,
            AudioSelectionMode,
            DesiredResultCount,
            QualityThreshold,
            ContentEmphasis,
            ClipFulfillmentPreference,
            MomentGuidance,
            CaptionSettings,
            ResultCountMode,
            AnalysisDepth,
            GameContextSettings,
            MaximumClipDuration);
    }

    private static int GetDefaultResultCount(
        GenerationMode mode)
    {
        return mode switch
        {
            GenerationMode.IndividualClips => 10,
            GenerationMode.Montage => 8,
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The generation mode is not defined."),
        };
    }
}
