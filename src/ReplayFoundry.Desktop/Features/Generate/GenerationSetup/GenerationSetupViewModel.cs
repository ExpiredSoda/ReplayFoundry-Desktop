using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Audio;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.ClipGoals;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Detection;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.GameContext;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.MomentGuidance;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Platform.GameKnowledge;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

public sealed class GenerationSetupViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly DelegateCommand _backCommand;
    private readonly DelegateCommand _nextCommand;
    private readonly DelegateCommand _finishCommand;
    private readonly DelegateCommand _cancelCommand;
    private readonly DelegateCommand<GenerationSetupStep>
        _navigateToStepCommand;
    private readonly IGenerationGameContextMemory? _gameContextMemory;
    private readonly IGenerationAudioRoleMemory? _audioRoleMemory;

    private readonly object[] _stepViewModels;

    private int _currentStepIndex;
    private int _furthestVisitedStepIndex;

    private ReadOnlyCollection<GenerationSetupStepItemViewModel>
        _steps;

    public GenerationSetupViewModel(
        GenerationSetupRequest request,
        GenerationSetupOptions? initialOptions = null,
        GenerationRuntimeCapabilities? runtimeCapabilities = null,
        IGenerationGameContextMemory? gameContextMemory = null,
        IGenerationAudioRoleMemory? audioRoleMemory = null,
        IAudioStreamAuditionService? audioAuditionService = null,
        IVideoPreviewFrameProvider? previewFrameProvider = null,
        IGameIdentityCandidateProvider? gameIdentityCandidates = null,
        IGenerationGameKnowledgeService? gameKnowledge = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        _gameContextMemory = gameContextMemory;
        _audioRoleMemory = audioRoleMemory;
        GenerationRuntimeCapabilities effectiveCapabilities =
            runtimeCapabilities ??
            new GenerationRuntimeCapabilities(
                IsCaptionTranscriptionAvailable: true,
                IsSpeechActivityAvailable: true,
                IsVisualSemanticReviewAvailable: true,
                IsEditorialAiAvailable: true);
        Draft =
            new GenerationSetupDraft(
                request,
                initialOptions,
                gameContextMemory,
                defaultAnalysisDepth:
                    effectiveCapabilities.IsSpeechActivityAvailable
                        ? GenerationAnalysisDepth.Balanced
                        : GenerationAnalysisDepth.Fast);

        DetectionStep =
            new DetectionStepViewModel(
                Draft,
                effectiveCapabilities);

        AudioStep =
            new AudioStepViewModel(
                Draft,
                audioRoleMemory,
                audioAuditionService,
                languageCapabilities: effectiveCapabilities.CaptionLanguageCapabilities);

        ClipGoalsStep =
            new ClipGoalsStepViewModel(Draft);

        GameContextStep =
            new GameContextStepViewModel(
                Draft,
                gameIdentityCandidates,
                gameKnowledge);

        MomentGuidanceStep =
            new MomentGuidanceStepViewModel(Draft, previewFrameProvider);

        _stepViewModels =
        [
            new GenerationSourcePage(this),
            new GenerationGoalPage(this),
            new GenerationAudioPage(this),
        ];

        DetectionStep.PropertyChanged +=
            StepViewModel_PropertyChanged;

        AudioStep.PropertyChanged +=
            StepViewModel_PropertyChanged;

        ClipGoalsStep.PropertyChanged +=
            StepViewModel_PropertyChanged;

        GameContextStep.PropertyChanged +=
            StepViewModel_PropertyChanged;

        MomentGuidanceStep.PropertyChanged +=
            StepViewModel_PropertyChanged;

        _currentStepIndex = 0;
        _furthestVisitedStepIndex = 0;
        _steps = BuildStepItems();

        _backCommand =
            new DelegateCommand(
                GoBack,
                () => CanGoBack);

        _nextCommand =
            new DelegateCommand(
                GoNext,
                () => CanGoNext);

        _finishCommand =
            new DelegateCommand(
                Finish,
                () => CanFinish);

        _cancelCommand =
            new DelegateCommand(
                RequestCancel);

        _navigateToStepCommand =
            new DelegateCommand<GenerationSetupStep>(
                NavigateToStep,
                CanNavigateToStep);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CancelRequested;

    public event EventHandler<GenerationSetupCompletedEventArgs>?
        FinishRequested;

    public GenerationSetupDraft Draft { get; }

    public DetectionStepViewModel DetectionStep { get; }

    public AudioStepViewModel AudioStep { get; }

    public ClipGoalsStepViewModel ClipGoalsStep { get; }

    public GameContextStepViewModel GameContextStep { get; }

    public MomentGuidanceStepViewModel MomentGuidanceStep { get; }

    public IReadOnlyList<GenerationSetupStepItemViewModel>
        Steps =>
        _steps;

    public object CurrentStepViewModel =>
        _stepViewModels[_currentStepIndex];

    public GenerationSetupStep CurrentStep =>
        _currentStepIndex switch { 0 => GenerationSetupStep.GameContext, 1 => GenerationSetupStep.ClipGoals, _ => GenerationSetupStep.Audio };

    public string ReviewSummary => $"{SourceSummary} · {Draft.DesiredResultCount} " +
        $"{(Draft.Request.Mode == GenerationMode.Montage ? "montage segments" : "clips")} · up to {Draft.MaximumClipDuration.TotalSeconds:0} seconds each";
    public string ReviewDetail => $"{Draft.AnalysisDepth} scan · " +
        $"{(Draft.MetadataAuthoringMode == GenerationMetadataAuthoringMode.AiRequired ? "Local AI writing" : "Simple titles")} · " +
        $"{(Draft.CaptionSettings.IsEnabled ? "Captions enabled" : "Captions off")}. Completed compatible analysis is reused; new recordings take longer.";

    public bool IsFirstStep =>
        _currentStepIndex == 0;

    public bool IsLastStep =>
        _currentStepIndex ==
        _stepViewModels.Length - 1;

    public bool CanGoBack =>
        !IsFirstStep;

    public bool CanGoNext =>
        !IsLastStep &&
        IsStepValid(_currentStepIndex);

    public bool CanFinish =>
        IsLastStep &&
        AreStepsValidThrough(_stepViewModels.Length - 1);

    public ICommand BackCommand =>
        _backCommand;

    public ICommand NextCommand =>
        _nextCommand;

    public ICommand FinishCommand =>
        _finishCommand;

    public ICommand CancelCommand =>
        _cancelCommand;

    public ICommand NavigateToStepCommand =>
        _navigateToStepCommand;

    public string ModeDisplayName =>
        Draft.Request.Mode switch
        {
            GenerationMode.IndividualClips =>
                "Individual Clips",

            GenerationMode.Montage =>
                "Montage",

            _ => throw new InvalidOperationException(
                "The generation mode is not supported."),
        };

    public int SourceCount =>
        Draft.Request.SourceCount;

    public bool IsBatchSetup =>
        Draft.Request.IsBatch;

    public string ReferenceSourceName =>
        Draft.Request.ReferenceSource.FileName;

    public string SourceSummary =>
        SourceCount == 1
            ? "1 video selected"
            : $"{SourceCount} videos selected";

    public string BatchWarning =>
        $"'{ReferenceSourceName}' sets the first preview and default choices. " +
        "Replay Foundry checks every video separately and asks you to review any different audio or layouts before finding clips.";

    public string StepPositionText =>
        $"Step {_currentStepIndex + 1} of {_stepViewModels.Length}";

    private void GoBack()
    {
        if (!CanGoBack)
        {
            throw new InvalidOperationException(
                "The wizard cannot move back from the first step.");
        }

        MoveToStep(
            _currentStepIndex - 1);
    }

    private void GoNext()
    {
        if (!CanGoNext)
        {
            throw new InvalidOperationException(
                "The current step must be valid before continuing.");
        }

        MoveToStep(
            _currentStepIndex + 1);
    }

    private bool CanNavigateToStep(
        GenerationSetupStep step)
    {
        if (!Enum.IsDefined(
                typeof(GenerationSetupStep),
                step))
        {
            return false;
        }

        int targetIndex = PageIndex(step);

        if (targetIndex < 0 ||
            targetIndex >= _stepViewModels.Length)
        {
            return false;
        }

        if (targetIndex == _currentStepIndex)
        {
            return true;
        }

        return AreStepsValidThrough(
            targetIndex - 1);
    }

    private void NavigateToStep(
        GenerationSetupStep step)
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

        if (!CanNavigateToStep(step))
        {
            throw new InvalidOperationException(
                $"Generation Setup cannot navigate to '{step}' " +
                "until every preceding step is valid.");
        }

        int targetIndex = PageIndex(step);

        if (targetIndex == _currentStepIndex)
        {
            return;
        }

        MoveToStep(targetIndex);
    }

    private void MoveToStep(int targetIndex)
    {
        if (targetIndex < 0 ||
            targetIndex >= _stepViewModels.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetIndex),
                targetIndex,
                "The wizard step index is invalid.");
        }

        _currentStepIndex = targetIndex;
        _furthestVisitedStepIndex = Math.Max(
            _furthestVisitedStepIndex,
            targetIndex);

        RefreshCurrentStepState();
    }

    private void Finish()
    {
        if (!CanFinish)
        {
            throw new InvalidOperationException(
                "Generation Setup cannot finish until every step is valid.");
        }

        GenerationSetupOptions options =
            Draft.CreateOptions();

        _gameContextMemory?.Remember(
            options.GameContextSettings.Sources);
        if (options.CaptionSettings.IsEnabled)
        {
            _audioRoleMemory?.Remember(
                Draft.Request.PreparedSources,
                options.CaptionSettings.SourceSelections);
        }

        FinishRequested?.Invoke(
            this,
            new GenerationSetupCompletedEventArgs(
                options));
    }

    private void RequestCancel()
    {
        CancelRequested?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void StepViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        _steps = BuildStepItems();

        OnPropertyChanged(
            nameof(Steps));
        OnPropertyChanged(nameof(ReviewSummary));
        OnPropertyChanged(nameof(ReviewDetail));

        RaiseCommandStateChanged();
    }

    private bool IsStepValid(int index)
    {
        return index switch
        {
            0 => GameContextStep.IsValid,
            1 => DetectionStep.IsValid && ClipGoalsStep.IsValid && MomentGuidanceStep.IsValid,
            2 => AudioStep.IsValid,

            _ => throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                "The wizard step index is invalid."),
        };
    }


    private bool AreStepsValidThrough(int lastIndex)
    {
        if (lastIndex < 0)
        {
            return true;
        }

        if (lastIndex >= _stepViewModels.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastIndex),
                lastIndex,
                "The wizard step index is invalid.");
        }

        for (int index = 0;
             index <= lastIndex;
             index++)
        {
            if (!IsStepValid(index))
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshCurrentStepState()
    {
        _steps = BuildStepItems();

        OnPropertyChanged(
            nameof(Steps));

        OnPropertyChanged(
            nameof(CurrentStepViewModel));

        OnPropertyChanged(
            nameof(CurrentStep));

        OnPropertyChanged(
            nameof(IsFirstStep));

        OnPropertyChanged(
            nameof(IsLastStep));

        OnPropertyChanged(
            nameof(CanGoBack));

        OnPropertyChanged(
            nameof(CanGoNext));

        OnPropertyChanged(
            nameof(CanFinish));

        OnPropertyChanged(
            nameof(StepPositionText));

        RaiseCommandStateChanged();
    }

    private void RaiseCommandStateChanged()
    {
        _backCommand
            .RaiseCanExecuteChanged();

        _nextCommand
            .RaiseCanExecuteChanged();

        _finishCommand
            .RaiseCanExecuteChanged();

        _navigateToStepCommand
            .RaiseCanExecuteChanged();

        OnPropertyChanged(
            nameof(CanGoBack));

        OnPropertyChanged(
            nameof(CanGoNext));

        OnPropertyChanged(
            nameof(CanFinish));
    }

    private static int PageIndex(GenerationSetupStep step) => step switch
    {
        GenerationSetupStep.GameContext => 0,
        GenerationSetupStep.Audio => 2,
        _ => 1,
    };

    private ReadOnlyCollection<GenerationSetupStepItemViewModel>
        BuildStepItems()
    {
        GenerationSetupStepItemViewModel[] items =
        [
            CreateStepItem(
                GenerationSetupStep.GameContext,
                number: 1,
                title: "Source and game",
                index: 0),

            CreateStepItem(
                GenerationSetupStep.ClipGoals,
                number: 2,
                title: "Goal and style",
                index: 1),

            CreateStepItem(
                GenerationSetupStep.Audio,
                number: 3,
                title: "Audio and review",
                index: 2),
        ];

        return Array.AsReadOnly(items);
    }

    private GenerationSetupStepItemViewModel CreateStepItem(
        GenerationSetupStep step,
        int number,
        string title,
        int index)
    {
        return new GenerationSetupStepItemViewModel(
            step,
            number,
            title,
            isCurrent:
                index == _currentStepIndex,
            isCompleted:
                index < _furthestVisitedStepIndex &&
                AreStepsValidThrough(index),
            isAvailable:
                CanNavigateToStep(step));
    }

    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }

    public void Dispose()
    {
        DetectionStep.PropertyChanged -= StepViewModel_PropertyChanged;
        AudioStep.PropertyChanged -= StepViewModel_PropertyChanged;
        ClipGoalsStep.PropertyChanged -= StepViewModel_PropertyChanged;
        GameContextStep.PropertyChanged -= StepViewModel_PropertyChanged;
        MomentGuidanceStep.PropertyChanged -= StepViewModel_PropertyChanged;
        AudioStep.Dispose();
        MomentGuidanceStep.Dispose();
    }
}
