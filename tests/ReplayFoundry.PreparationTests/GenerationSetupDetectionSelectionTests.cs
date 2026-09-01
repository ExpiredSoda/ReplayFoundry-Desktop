using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;

namespace ReplayFoundry.PreparationTests;

internal static class GenerationSetupDetectionSelectionTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Unavailable Local AI starts unselected without changing the draft",
            UnavailableAiStartsUnselected),
        new(
            "Simple titles require an explicit selection when Local AI is unavailable",
            ExplicitSimpleSelectionEnablesNext),
        new(
            "Unavailable Local AI cannot be assigned as the selected writer",
            UnavailableAiCannotBeAssigned),
        new(
            "Available Local AI remains selected and selectable",
            AvailableAiRemainsSelectable),
    ];

    private static Task UnavailableAiStartsUnselected()
    {
        const string unavailableReason =
            "Advanced AI 0.8.22 / 4.0.18 with editorial prompt 1.40 is required.";
        using GenerationSetupViewModel setup =
            CreateSetup(
                isEditorialAiAvailable: false,
                editorialAiUnavailableReason: unavailableReason);

        TestAssert.Null(
            setup.DetectionStep.SelectedMetadataOption,
            "An unavailable Local AI card must not appear selected.");
        TestAssert.Equal(
            GenerationMetadataAuthoringMode.AiRequired,
            setup.Draft.MetadataAuthoringMode,
            "Opening setup must not silently replace the AI-required draft with Simple titles.");
        TestAssert.False(
            setup.DetectionStep.IsValid,
            "The detection step must remain blocked until an available title writer is explicitly selected.");
        TestAssert.False(
            setup.CanGoNext,
            "Next must remain blocked while the retained AI-required choice is unavailable.");
        TestAssert.Equal(
            unavailableReason,
            setup.DetectionStep.ValidationMessage,
            "The setup must preserve the precise Advanced AI incompatibility guidance.");
        TestAssert.Equal(
            unavailableReason,
            setup.DetectionStep.SelectedMetadataDescription,
            "The current-choice summary must keep the precise unavailable guidance visible.");

        return Task.CompletedTask;
    }

    private static Task ExplicitSimpleSelectionEnablesNext()
    {
        using GenerationSetupViewModel setup =
            CreateSetup(
                isEditorialAiAvailable: false,
                editorialAiUnavailableReason:
                    "Install the matching Advanced AI pack.");
        SelectionOption<GenerationMetadataAuthoringMode> simpleOption =
            setup.DetectionStep.MetadataOptions.Single(
                option =>
                    option.Value ==
                    GenerationMetadataAuthoringMode.HeuristicOnly);

        setup.DetectionStep.SelectedMetadataOption = simpleOption;

        TestAssert.Same(
            simpleOption,
            setup.DetectionStep.SelectedMetadataOption!,
            "The explicit Simple selection must be presented as selected.");
        TestAssert.Equal(
            GenerationMetadataAuthoringMode.HeuristicOnly,
            setup.Draft.MetadataAuthoringMode,
            "Only the explicit Simple selection may change the draft to heuristic authoring.");
        TestAssert.True(
            setup.DetectionStep.IsValid,
            "Simple titles must satisfy the title-writer choice without Local AI.");
        TestAssert.True(
            setup.CanGoNext,
            "Next must become available after the user explicitly selects Simple titles.");
        TestAssert.Null(
            setup.DetectionStep.ValidationMessage,
            "The unavailable-AI guidance must stop blocking after an explicit Simple selection.");

        return Task.CompletedTask;
    }

    private static Task UnavailableAiCannotBeAssigned()
    {
        using GenerationSetupViewModel setup =
            CreateSetup(
                isEditorialAiAvailable: false,
                editorialAiUnavailableReason:
                    "Install the matching Advanced AI pack.");
        SelectionOption<GenerationMetadataAuthoringMode> aiOption =
            setup.DetectionStep.MetadataOptions.Single(
                option =>
                    option.Value ==
                    GenerationMetadataAuthoringMode.AiRequired);

        TestAssert.Throws<ArgumentException>(
            () => setup.DetectionStep.SelectedMetadataOption = aiOption,
            "A disabled Local AI option must not be assignable through the public selection boundary.");
        TestAssert.Null(
            setup.DetectionStep.SelectedMetadataOption,
            "Rejecting unavailable Local AI must leave the visible selection empty.");
        TestAssert.Equal(
            GenerationMetadataAuthoringMode.AiRequired,
            setup.Draft.MetadataAuthoringMode,
            "Rejecting unavailable Local AI must not silently change the retained AI-required draft.");

        return Task.CompletedTask;
    }

    private static Task AvailableAiRemainsSelectable()
    {
        GenerationSetupOptions retainedAiOptions =
            PreparedGenerationWorkflowTests.CreateOptions(
                metadataAuthoringMode:
                    GenerationMetadataAuthoringMode.AiRequired);
        using GenerationSetupViewModel setup =
            CreateSetup(
                isEditorialAiAvailable: true,
                initialOptions: retainedAiOptions);
        SelectionOption<GenerationMetadataAuthoringMode> aiOption =
            setup.DetectionStep.MetadataOptions.Single(
                option =>
                    option.Value ==
                    GenerationMetadataAuthoringMode.AiRequired);
        SelectionOption<GenerationMetadataAuthoringMode> simpleOption =
            setup.DetectionStep.MetadataOptions.Single(
                option =>
                    option.Value ==
                    GenerationMetadataAuthoringMode.HeuristicOnly);

        TestAssert.Same(
            aiOption,
            setup.DetectionStep.SelectedMetadataOption!,
            "A retained AI-required choice must stay selected when Local AI is available.");
        TestAssert.True(
            setup.CanGoNext,
            "Available Local AI must satisfy the initial setup step.");

        setup.DetectionStep.SelectedMetadataOption = simpleOption;
        setup.DetectionStep.SelectedMetadataOption = aiOption;

        TestAssert.Same(
            aiOption,
            setup.DetectionStep.SelectedMetadataOption!,
            "The available Local AI card must remain selectable after choosing Simple.");
        TestAssert.Equal(
            GenerationMetadataAuthoringMode.AiRequired,
            setup.Draft.MetadataAuthoringMode,
            "Selecting available Local AI must restore AI-required authoring in the draft.");
        TestAssert.True(
            setup.DetectionStep.IsValid,
            "The available AI-required choice must be valid.");

        return Task.CompletedTask;
    }

    private static GenerationSetupViewModel CreateSetup(
        bool isEditorialAiAvailable,
        string? editorialAiUnavailableReason = null,
        GenerationSetupOptions? initialOptions = null)
    {
        var request = new GenerationSetupRequest(
            GenerationMode.IndividualClips,
            PreparedGenerationWorkflowTests.CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "generation-setup-title-writing.mkv"),
                    true,
                    true),
            ]));
        var capabilities = new GenerationRuntimeCapabilities(
            IsCaptionTranscriptionAvailable: true,
            IsSpeechActivityAvailable: true,
            IsVisualSemanticReviewAvailable: true,
            IsEditorialAiAvailable: isEditorialAiAvailable,
            EditorialAiUnavailableReason:
                editorialAiUnavailableReason);

        return new GenerationSetupViewModel(
            request,
            initialOptions,
            capabilities);
    }
}
