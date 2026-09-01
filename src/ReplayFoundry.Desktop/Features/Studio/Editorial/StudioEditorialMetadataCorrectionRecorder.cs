using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;
using ReplayFoundry.Desktop.Platform.Diagnostics;

namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

public interface IStudioEditorialMetadataCorrectionRecorder
{
    bool TryRecordCorrection(
        string beforeTitle,
        string beforeDescription,
        string beforeTags,
        string afterTitle,
        string afterDescription,
        string afterTags);
}

/// <summary>
/// Keeps structural-learning contracts behind the Studio application
/// boundary so presentation models never depend on intelligence internals.
/// </summary>
public sealed class StudioEditorialMetadataCorrectionRecorder :
    IStudioEditorialMetadataCorrectionRecorder
{
    private readonly EditorialMetadataPreferenceRecorder _recorder;

    public StudioEditorialMetadataCorrectionRecorder(
        EditorialMetadataPreferenceRecorder recorder)
    {
        _recorder = recorder ?? throw new ArgumentNullException(
            nameof(recorder));
    }

    public bool TryRecordCorrection(
        string beforeTitle,
        string beforeDescription,
        string beforeTags,
        string afterTitle,
        string afterDescription,
        string afterTags)
    {
        if (!_recorder.IsEnabled)
        {
            return false;
        }

        try
        {
            (string Title, string Description, string[] Tags) before =
                NormalizeStoredMetadata(
                    beforeTitle,
                    beforeDescription,
                    beforeTags);
            (string Title, string Description, string[] Tags) after =
                NormalizeStoredMetadata(
                    afterTitle,
                    afterDescription,
                    afterTags);
            if (before.Title.Equals(after.Title, StringComparison.Ordinal) &&
                before.Description.Equals(
                    after.Description,
                    StringComparison.Ordinal) &&
                before.Tags.SequenceEqual(
                    after.Tags,
                    StringComparer.Ordinal))
            {
                return false;
            }

            return _recorder.TryRecord(
                EditorialMetadataPreferenceEvidence.HumanCorrection(
                    EditorialMetadataStructuralFeatureExtractor.Extract(
                        before.Title,
                        before.Description,
                        before.Tags),
                    EditorialMetadataStructuralFeatureExtractor.Extract(
                        after.Title,
                        after.Description,
                        after.Tags)));
        }
        catch (Exception exception)
        {
            SafeDiagnosticTrace.Write(
                "Local editorial style learning was skipped",
                exception);
            return false;
        }
    }

    private static (string Title, string Description, string[] Tags)
        NormalizeStoredMetadata(
            string title,
            string description,
            string tags) =>
        (
            title.Trim(),
            description.Trim(),
            ClipEditorialProfileTags.Parse(tags)
                .Select(ClipEditorialProfile.NormalizeTag)
                .Where(static tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .ToArray());
}
