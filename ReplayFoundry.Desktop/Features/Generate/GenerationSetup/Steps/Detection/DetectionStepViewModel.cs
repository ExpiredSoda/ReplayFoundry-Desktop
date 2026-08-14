using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Workflow;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Detection;

public sealed class DetectionStepViewModel : INotifyPropertyChanged
{
    private readonly GenerationSetupDraft _draft;

    private readonly SelectionOption<GenerationAnalysisDepth>[]
        _options;

    private SelectionOption<GenerationAnalysisDepth>
        _selectedOption;

    public DetectionStepViewModel(
        GenerationSetupDraft draft,
        GenerationRuntimeCapabilities runtimeCapabilities)
    {
        ArgumentNullException.ThrowIfNull(draft);

        _draft = draft;
        ArgumentNullException.ThrowIfNull(runtimeCapabilities);

        _options =
        [
            new SelectionOption<GenerationAnalysisDepth>(
                GenerationAnalysisDepth.Fast,
                "Fast",
                "Use the coarsest deterministic signal cadence and Gameplay regions only. Best for quick drafts and lower-end PCs."),

            new SelectionOption<GenerationAnalysisDepth>(
                GenerationAnalysisDepth.Balanced,
                "Balanced",
                "Use deterministic evidence plus local speech-activity timing. This is the recommended everyday scan and does not require visual AI.",
                runtimeCapabilities.IsSpeechActivityAvailable,
                "Balanced needs the approved local speech-activity model. Configure it explicitly or choose Fast."),

            new SelectionOption<GenerationAnalysisDepth>(
                GenerationAnalysisDepth.Thorough,
                "Thorough",
                "Add bounded, schema-qualified visual review to deterministic evidence and speech timing for the strongest local analysis.",
                runtimeCapabilities.IsSpeechActivityAvailable &&
                runtimeCapabilities.IsVisualSemanticReviewAvailable,
                !runtimeCapabilities.IsSpeechActivityAvailable
                    ? "Thorough needs the approved local speech-activity model."
                    : "Thorough remains unavailable until the bounded visual provider passes and is configured."),
        ];

        _selectedOption =
            _options.Single(
                option =>
                    option.Value ==
                    draft.AnalysisDepth);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<SelectionOption<GenerationAnalysisDepth>>
        Options =>
        _options;

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

    public string SelectedDescription =>
        SelectedOption.Description;

    public bool IsValid => SelectedOption.IsAvailable;

    public string? ValidationMessage =>
        SelectedOption.IsAvailable
            ? null
            : SelectedOption.UnavailableReason;

    public string OptionalIntelligenceStatus =>
        "Fast uses deterministic evidence only. Balanced adds local speech " +
        "timing. Thorough also requires qualified bounded visual review. " +
        "No profile silently substitutes a missing provider.";

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
