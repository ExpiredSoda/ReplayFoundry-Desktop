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
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Preview;
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
        IVideoPreviewFrameProvider? previewFrameProvider = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        _gameContextMemory = gameContextMemory;
        _audioRoleMemory = audioRoleMemory;
        GenerationRuntimeCapabilities effectiveCapabilities =
            runtimeCapabilities ??
            new GenerationRuntimeCapabilities(
                IsCaptionTranscriptionAvailable: true,
                IsSpeechActivityAvailable: true,
                IsVisualSemanticReviewAvailable: true);
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
                audioAuditionService);

        ClipGoalsStep =
            new ClipGoalsStepViewModel(Draft);

        GameContextStep =
            new GameContextStepViewModel(Draft);

        MomentGuidanceStep =
            new MomentGuidanceStepViewModel(Draft, previewFrameProvider);

        _stepViewModels =
        [
            DetectionStep,
            AudioStep,
            ClipGoalsStep,
            GameContextStep,
            MomentGuidanceStep,
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
        (GenerationSetupStep)_currentStepIndex;

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
            ? "1 source selected"
            : $"{SourceCount} sources selected";

    public string BatchWarning =>
        $"'{ReferenceSourceName}' is the explicit reference for initial " +
        "previews and defaults. Every selected source was inspected " +
        "independently. Incompatible audio or composition mappings will " +
        "require review before generation.";

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

        int targetIndex = (int)step;

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

        int targetIndex = (int)step;

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

        RaiseCommandStateChanged();
    }

    private bool IsStepValid(int index)
    {
        return index switch
        {
            0 => DetectionStep.IsValid,
            1 => AudioStep.IsValid,
            2 => ClipGoalsStep.IsValid,
            3 => GameContextStep.IsValid,
            4 => MomentGuidanceStep.IsValid,

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

    private ReadOnlyCollection<GenerationSetupStepItemViewModel>
        BuildStepItems()
    {
        GenerationSetupStepItemViewModel[] items =
        [
            CreateStepItem(
                GenerationSetupStep.Detection,
                number: 1,
                title: "Scan depth",
                index: 0),

            CreateStepItem(
                GenerationSetupStep.Audio,
                number: 2,
                title: "Audio",
                index: 1),

            CreateStepItem(
                GenerationSetupStep.ClipGoals,
                number: 3,
                title: "Clip Goals",
                index: 2),

            CreateStepItem(
                GenerationSetupStep.GameContext,
                number: 4,
                title: "Game Context",
                index: 3),

            CreateStepItem(
                GenerationSetupStep.MomentGuidance,
                number: 5,
                title: "Priority Moments",
                index: 4),
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
