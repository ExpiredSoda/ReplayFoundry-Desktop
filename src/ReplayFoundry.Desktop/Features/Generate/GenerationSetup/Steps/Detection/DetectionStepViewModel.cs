using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Workflow;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Detection;

public sealed class DetectionStepViewModel : INotifyPropertyChanged
{
    private readonly GenerationSetupDraft _draft;
    private readonly GenerationRuntimeCapabilities _runtime;
    public string GpuReadiness { get; private set; } = "";
    public ICommand RefreshGpuReadinessCommand { get; }
    private void RefreshGpuReadiness()
    {
        GpuReadiness = !_runtime.IsEditorialAiAvailable ? _runtime.EditorialAiBlockingReason :
            _runtime.EditorialGpuReadiness?.Invoke() ?? _runtime.EditorialGpuAdmissionCheck?.Invoke() ??
            "The local AI runtime checks memory before loading its model.";
        OnPropertyChanged(nameof(GpuReadiness));
    }

    private readonly SelectionOption<GenerationAnalysisDepth>[]
        _options;

    private readonly SelectionOption<GenerationMetadataAuthoringMode>[]
        _metadataOptions;

    private SelectionOption<GenerationAnalysisDepth>
        _selectedOption;

    private SelectionOption<GenerationMetadataAuthoringMode>?
        _selectedMetadataOption;

    public DetectionStepViewModel(
        GenerationSetupDraft draft,
        GenerationRuntimeCapabilities runtimeCapabilities)
    {
        ArgumentNullException.ThrowIfNull(draft);

        _draft = draft;
        ArgumentNullException.ThrowIfNull(runtimeCapabilities);
        _runtime = runtimeCapabilities;
        RefreshGpuReadinessCommand = new DelegateCommand(RefreshGpuReadiness);
        RefreshGpuReadiness();

        _options =
        [
            new SelectionOption<GenerationAnalysisDepth>(
                GenerationAnalysisDepth.Fast,
                "Fast",
                "Looks for clear changes in the gameplay picture. Best for a quick first pass and lower-end PCs."),

            new SelectionOption<GenerationAnalysisDepth>(
                GenerationAnalysisDepth.Balanced,
                "Balanced",
                "Uses picture changes and speech timing. With AI writing, maps the recording and checks shortlisted moments.",
                runtimeCapabilities.IsSpeechActivityAvailable,
                "Balanced needs speech analysis. Add Advanced AI in Settings or choose Fast."),

            new SelectionOption<GenerationAnalysisDepth>(
                GenerationAnalysisDepth.Thorough,
                "Thorough",
                "Maps the whole recording, searches spoken moments, and reviews shortlisted footage with local AI.",
                runtimeCapabilities.IsSpeechActivityAvailable &&
                runtimeCapabilities.IsVisualSemanticReviewAvailable,
                !runtimeCapabilities.IsSpeechActivityAvailable
                    ? "Thorough needs speech analysis. Add Advanced AI in Settings."
                    : "Thorough needs visual AI. Repair or update Advanced AI in Settings."),
        ];

        _selectedOption =
            _options.Single(
                option =>
                    option.Value ==
                    draft.AnalysisDepth);

        _metadataOptions =
        [
            new SelectionOption<GenerationMetadataAuthoringMode>(
                GenerationMetadataAuthoringMode.AiRequired,
                "Local AI · Recommended",
                "Let local AI write specific titles and descriptions from what happens in each clip. If it cannot finish, Replay Foundry stops and tells you.",
                runtimeCapabilities.IsEditorialAiAvailable,
                runtimeCapabilities.EditorialAiBlockingReason),
            new SelectionOption<GenerationMetadataAuthoringMode>(
                GenerationMetadataAuthoringMode.HeuristicOnly,
                "Simple titles · No AI",
                "Write straightforward titles and descriptions on this PC without loading an AI model."),
        ];
        SelectionOption<GenerationMetadataAuthoringMode>
            retainedMetadataOption = _metadataOptions.Single(
                option => option.Value == draft.MetadataAuthoringMode);
        _selectedMetadataOption = retainedMetadataOption.IsAvailable
            ? retainedMetadataOption
            : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<SelectionOption<GenerationAnalysisDepth>>
        Options =>
        _options;

    public IReadOnlyList<SelectionOption<GenerationMetadataAuthoringMode>>
        MetadataOptions =>
        _metadataOptions;

    public SelectionOption<GenerationAnalysisDepth> SelectedOption
    {
        get => _selectedOption;

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!_options.Contains(value))
            {
                throw new ArgumentException(
                    "The selected detection option is not available in this step.",
                    nameof(value));
            }

            if (ReferenceEquals(
                    _selectedOption,
                    value))
            {
                return;
            }

            _selectedOption = value;

            _draft.UpdateAnalysisDepth(
                value.Value);

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(SelectedDescription));

            OnPropertyChanged(
                nameof(IsValid));

            OnPropertyChanged(
                nameof(ValidationMessage));
        }
    }

    public SelectionOption<GenerationMetadataAuthoringMode>?
        SelectedMetadataOption
    {
        get => _selectedMetadataOption;

        set
        {
            if (value is not null &&
                (!_metadataOptions.Contains(value) ||
                 !value.IsAvailable))
            {
                throw new ArgumentException(
                    "The selected metadata writer is not available in this step.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedMetadataOption, value))
            {
                return;
            }

            _selectedMetadataOption = value;
            if (value is not null)
            {
                _draft.UpdateMetadataAuthoringMode(value.Value);
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMetadataName));
            OnPropertyChanged(nameof(SelectedMetadataDescription));
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(ValidationMessage));
        }
    }

    public string SelectedDescription =>
        SelectedOption.Description;

    public string SelectedMetadataName =>
        SelectedMetadataOption?.Name ??
        "Selection required";

    public string SelectedMetadataDescription =>
        SelectedMetadataOption?.Description ??
        MetadataValidationMessage;

    public bool IsValid =>
        SelectedOption.IsAvailable &&
        SelectedMetadataOption?.IsAvailable == true;

    public string? ValidationMessage =>
        !SelectedOption.IsAvailable
            ? SelectedOption.UnavailableReason
            : SelectedMetadataOption?.IsAvailable == true
                ? null
                : MetadataValidationMessage;

    public string OptionalIntelligenceStatus =>
        "Fast looks at picture changes. Balanced adds speech timing and, with AI writing, a visual recording map. " +
        "Thorough also searches full-recording transcripts. Choose title writing separately; " +
        "Replay Foundry never switches methods without telling you.";

    private string MetadataValidationMessage
    {
        get
        {
            SelectionOption<GenerationMetadataAuthoringMode>
                retainedMetadataOption = _metadataOptions.Single(
                    option =>
                        option.Value ==
                        _draft.MetadataAuthoringMode);

            return retainedMetadataOption.IsAvailable
                ? "Choose a title-writing method to continue."
                : retainedMetadataOption.UnavailableReason!;
        }
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
}
