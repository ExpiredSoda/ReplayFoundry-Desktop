using System;
using System.Collections.Generic;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public enum GenerationSetupStep
{
    Detection,
    Audio,
    ClipGoals,
    GameContext,
    MomentGuidance,
}

public enum DetectionMethod
{
    Heuristics,
    LocalAi,
    Hybrid,
}

public enum AudioSelectionMode
{
    Auto,
    Manual,
}

public enum ContentEmphasis
{
    GameplayFocused,
    Balanced,
    CommentaryFocused,
}

public enum ClipFulfillmentPreference
{
    FillRequestedCount,
    QualityFirst,
}

public enum GenerationResultCountMode
{
    Exact,
    Auto,
}

public enum GenerationAnalysisDepth
{
    Fast,
    Balanced,
    Thorough,
}

public sealed class SelectionOption<TValue>
    where TValue : struct, Enum
{
    public SelectionOption(
        TValue value,
        string name,
        string description,
        bool isAvailable = true,
        string? unavailableReason = null)
    {
        if (!Enum.IsDefined(typeof(TValue), value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "The option value is not defined.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "An option requires a display name.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "An option requires a description.",
                nameof(description));
        }

        if (!isAvailable &&
            string.IsNullOrWhiteSpace(unavailableReason))
        {
            throw new ArgumentException(
                "An unavailable option requires an explanation.",
                nameof(unavailableReason));
        }

        Value = value;
        Name = name;
        Description = description;
        IsAvailable = isAvailable;
        UnavailableReason = unavailableReason;
    }

    public TValue Value { get; }

    public string Name { get; }

    public string Description { get; }

    public bool IsAvailable { get; }

    public string? UnavailableReason { get; }

    public override string ToString()
    {
        return Name;
    }
}

public sealed class GenerationSetupRequest
{
    public GenerationSetupRequest(
        GenerationMode mode,
        GenerationSourcePreparationResult preparation)
    {
        if (!Enum.IsDefined(typeof(GenerationMode), mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The generation mode is not defined.");
        }

        ArgumentNullException.ThrowIfNull(preparation);

        Mode = mode;
        Preparation = preparation;
    }

    public GenerationMode Mode { get; }

    public GenerationSourcePreparationResult Preparation { get; }

    public IReadOnlyList<SelectedVideoSource> Sources =>
        Preparation.Request.Sources;

    public SelectedVideoSource ReferenceSource =>
        Preparation.Request.ReferenceSource;

    public IReadOnlyList<PreparedGenerationSource> PreparedSources =>
        Preparation.Sources;

    public PreparedGenerationSource ReferencePreparedSource =>
        Preparation.ReferenceSource;

    public MediaProbeResult ReferenceMedia =>
        ReferencePreparedSource.Media;

    public int SourceCount =>
        Preparation.Request.SourceCount;

    public bool IsBatch =>
        SourceCount > 1;
}

public sealed class GenerationSetupOptions
{
    public static readonly TimeSpan MinimumMaximumClipDuration =
        TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaximumMaximumClipDuration =
        TimeSpan.FromMinutes(3);

    public GenerationSetupOptions(
        GenerationMode mode,
        DetectionMethod detectionMethod,
        AudioSelectionMode audioSelectionMode,
        int desiredResultCount,
        double qualityThreshold,
        ContentEmphasis contentEmphasis,
        ClipFulfillmentPreference clipFulfillmentPreference =
            ClipFulfillmentPreference.FillRequestedCount,
        GenerationMomentGuidance? momentGuidance = null,
        GenerationCaptionSettings? captionSettings = null,
        GenerationResultCountMode resultCountMode =
            GenerationResultCountMode.Exact,
        GenerationAnalysisDepth analysisDepth =
            GenerationAnalysisDepth.Balanced,
        GenerationGameContextSettings? gameContextSettings = null,
        TimeSpan? maximumClipDuration = null)
    {
        if (!Enum.IsDefined(typeof(GenerationMode), mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The generation mode is not defined.");
        }

        if (!Enum.IsDefined(
                typeof(DetectionMethod),
                detectionMethod))
        {
            throw new ArgumentOutOfRangeException(
                nameof(detectionMethod),
                detectionMethod,
                "The detection method is not defined.");
        }

        if (!Enum.IsDefined(
                typeof(AudioSelectionMode),
                audioSelectionMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioSelectionMode),
                audioSelectionMode,
                "The audio-selection mode is not defined.");
        }

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
            throw new ArgumentOutOfRangeException(
                nameof(resultCountMode));
        }
        if (!Enum.IsDefined(analysisDepth))
        {
            throw new ArgumentOutOfRangeException(
                nameof(analysisDepth));
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
        if (resultCountMode == GenerationResultCountMode.Auto &&
            (desiredResultCount != 30 ||
             clipFulfillmentPreference !=
                ClipFulfillmentPreference.QualityFirst))
        {
            throw new ArgumentException(
                "Auto amount uses the 30-clip safety cap and returns only clips meeting the selected quality target.");
        }

        TimeSpan resolvedMaximumClipDuration =
            maximumClipDuration ?? mode switch
            {
                GenerationMode.IndividualClips => TimeSpan.FromSeconds(60),
                GenerationMode.Montage => TimeSpan.FromSeconds(12),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
        if (resolvedMaximumClipDuration < MinimumMaximumClipDuration ||
            resolvedMaximumClipDuration > MaximumMaximumClipDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumClipDuration),
                "The maximum candidate length must be between 10 seconds and 3 minutes.");
        }

        Mode = mode;
        DetectionMethod = detectionMethod;
        AudioSelectionMode = audioSelectionMode;
        DesiredResultCount = desiredResultCount;
        QualityThreshold = qualityThreshold;
        ContentEmphasis = contentEmphasis;
        ClipFulfillmentPreference = clipFulfillmentPreference;
        MomentGuidance = momentGuidance ?? GenerationMomentGuidance.Empty;
        CaptionSettings = captionSettings ?? GenerationCaptionSettings.Disabled;
        ResultCountMode = resultCountMode;
        AnalysisDepth = analysisDepth;
        GameContextSettings = gameContextSettings ??
            GenerationGameContextSettings.Empty;
        MaximumClipDuration = resolvedMaximumClipDuration;
    }

    public GenerationMode Mode { get; }

    public DetectionMethod DetectionMethod { get; }

    public AudioSelectionMode AudioSelectionMode { get; }

    public int DesiredResultCount { get; }

    public double QualityThreshold { get; }

    public ContentEmphasis ContentEmphasis { get; }

    public ClipFulfillmentPreference ClipFulfillmentPreference { get; }

    public GenerationMomentGuidance MomentGuidance { get; }

    public GenerationCaptionSettings CaptionSettings { get; }
    public GenerationResultCountMode ResultCountMode { get; }
    public bool IsAutomaticResultCount =>
        ResultCountMode == GenerationResultCountMode.Auto;
    public GenerationAnalysisDepth AnalysisDepth { get; }

    public GenerationGameContextSettings GameContextSettings { get; }

    public TimeSpan MaximumClipDuration { get; }

    public string MaximumClipDurationDisplayName =>
        MaximumClipDuration.TotalMinutes >= 1
            ? MediaTimeFormatter.Format(MaximumClipDuration)
            : $"{MaximumClipDuration.TotalSeconds:0} seconds";

    public string DetectionDisplayName =>
        DetectionMethod switch
        {
            DetectionMethod.Heuristics => "Heuristics",
            DetectionMethod.LocalAi => "Local AI",
            DetectionMethod.Hybrid => "Hybrid",
            _ => throw new InvalidOperationException(
                "The detection method is not supported."),
        };

    public string AudioDisplayName =>
        AudioSelectionMode switch
        {
            AudioSelectionMode.Auto => "Automatic audio selection",
            AudioSelectionMode.Manual => "Manual audio selection",
            _ => throw new InvalidOperationException(
                "The audio-selection mode is not supported."),
        };

    public string ContentEmphasisDisplayName =>
        ContentEmphasis switch
        {
            ContentEmphasis.GameplayFocused => "Gameplay focused",
            ContentEmphasis.Balanced => "Balanced",
            ContentEmphasis.CommentaryFocused => "Commentary focused",
            _ => throw new InvalidOperationException(
                "The content emphasis is not supported."),
        };

    public string ResultCountLabel =>
        IsAutomaticResultCount
            ? Mode == GenerationMode.Montage
                ? "Auto, up to 30 montage segments"
                : "Auto, up to 30 individual clips"
            : Mode == GenerationMode.Montage
            ? $"{DesiredResultCount} montage segments"
            : $"{DesiredResultCount} individual clips";

    public string AnalysisDepthDisplayName => AnalysisDepth switch
    {
        GenerationAnalysisDepth.Fast => "Fast scan",
        GenerationAnalysisDepth.Balanced => "Balanced scan",
        GenerationAnalysisDepth.Thorough => "Thorough scan",
        _ => throw new InvalidOperationException(
            "The analysis depth is not supported."),
    };

    public string ClipFulfillmentDisplayName =>
        ClipFulfillmentPreference switch
        {
            ClipFulfillmentPreference.FillRequestedCount =>
                "Fill requested count",
            ClipFulfillmentPreference.QualityFirst =>
                "Quality first",
            _ => throw new InvalidOperationException(
                "The clip-fulfillment preference is not supported."),
        };

    public string Summary =>
        $"{DetectionDisplayName} • {AudioDisplayName} • " +
        $"{ResultCountLabel} • {ClipFulfillmentDisplayName} • " +
        $"{QualityThreshold:0}% quality target • " +
        ContentEmphasisDisplayName + " • " +
        AnalysisDepthDisplayName +
        $" • up to {MaximumClipDurationDisplayName}" +
        (CaptionSettings.IsEnabled
            ? $" • {CaptionSettings.Style} captions"
            : string.Empty);
}

public sealed class GenerationSetupStepItemViewModel
{
    public GenerationSetupStepItemViewModel(
        GenerationSetupStep step,
        int number,
        string title,
        bool isCurrent,
        bool isCompleted,
        bool isAvailable)
    {
        if (!Enum.IsDefined(
                typeof(GenerationSetupStep),
                step))
        {
            throw new ArgumentOutOfRangeException(
                nameof(step),
                step,
                "The Generation Setup step is not defined.");
        }

        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(number),
                number,
                "A step number must be positive.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "A step requires a title.",
                nameof(title));
        }

        Step = step;
        Number = number;
        Title = title;
        IsCurrent = isCurrent;
        IsCompleted = isCompleted;
        IsAvailable = isAvailable;
    }

    public GenerationSetupStep Step { get; }

    public int Number { get; }

    public string Title { get; }

    public bool IsCurrent { get; }

    public bool IsCompleted { get; }

    public bool IsAvailable { get; }
}

public sealed class GenerationSetupCompletedEventArgs : EventArgs
{
    public GenerationSetupCompletedEventArgs(
        GenerationSetupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Options = options;
    }

    public GenerationSetupOptions Options { get; }
}
