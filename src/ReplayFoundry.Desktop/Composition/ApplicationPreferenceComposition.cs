using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record ApplicationPreferenceServices(
    GenerationOutputLocationState OutputLocation,
    EditorialRerollPreferenceState EditorialReroll,
    EditorialMetadataPreferenceLearningConsentState MetadataLearningConsent,
    StudioEditorialMetadataCorrectionRecorder MetadataCorrectionRecorder);

internal static class ApplicationPreferenceComposition
{
    public static ApplicationPreferenceServices Create()
    {
        GenerationOutputLocationState outputLocation =
            CreateGenerationOutputLocation();
        EditorialRerollPreferenceState editorialReroll =
            CreateEditorialRerollPreference();
        EditorialMetadataPreferenceLearningConsentState metadataConsent =
            CreateMetadataLearningConsent();
        var correctionRecorder = CreateMetadataCorrectionRecorder(
            metadataConsent);
        return new(
            outputLocation,
            editorialReroll,
            metadataConsent,
            correctionRecorder);
    }

    private static StudioEditorialMetadataCorrectionRecorder
        CreateMetadataCorrectionRecorder(
            EditorialMetadataPreferenceLearningConsentState consent) =>
        new(
            new EditorialMetadataPreferenceRecorder(
                consent,
                static () => new JsonEditorialMetadataPreferenceStore()),
            new JsonEditorialWriterLearningStore(enabled: () => consent.IsEnabled));

    private static GenerationOutputLocationState
        CreateGenerationOutputLocation()
    {
        try
        {
            return new GenerationOutputLocationState(
                new JsonGenerationOutputLocationStore());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException)
        {
            SafeDiagnosticTrace.Write(
                "Generation output-location storage is unavailable",
                exception);
            return new GenerationOutputLocationState(
                new InMemoryGenerationOutputLocationStore());
        }
    }

    private static EditorialRerollPreferenceState
        CreateEditorialRerollPreference()
    {
        try
        {
            return new EditorialRerollPreferenceState(
                new JsonEditorialRerollPreferenceStore());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Editorial reroll-preference storage is unavailable",
                exception);
            return new EditorialRerollPreferenceState(
                new InMemoryEditorialRerollPreferenceStore());
        }
    }

    private static EditorialMetadataPreferenceLearningConsentState
        CreateMetadataLearningConsent()
    {
        try
        {
            return new EditorialMetadataPreferenceLearningConsentState(
                new JsonEditorialMetadataPreferenceLearningConsentStore());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Editorial metadata preference-learning consent storage is unavailable",
                exception);
            return new EditorialMetadataPreferenceLearningConsentState(
                new InMemoryEditorialMetadataPreferenceLearningConsentStore());
        }
    }
}
