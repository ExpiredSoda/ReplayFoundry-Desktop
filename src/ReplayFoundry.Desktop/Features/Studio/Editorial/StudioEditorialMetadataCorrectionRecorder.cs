using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
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
    bool TryRecordCorrection(GenerationOutputAsset asset, string beforeTitle, string beforeDescription,
        string beforeTags, string afterTitle, string afterDescription, string afterTags) =>
        TryRecordCorrection(beforeTitle, beforeDescription, beforeTags, afterTitle, afterDescription, afterTags);
    bool TryRecordApproval(GenerationOutputAsset asset) => false;
}

/// <summary>
/// Keeps structural-learning contracts behind the Studio application
/// boundary so presentation models never depend on intelligence internals.
/// </summary>
public sealed class StudioEditorialMetadataCorrectionRecorder :
    IStudioEditorialMetadataCorrectionRecorder
{
    private readonly EditorialMetadataPreferenceRecorder _recorder;
    private readonly IEditorialWriterLearningStore? _writer;

    public StudioEditorialMetadataCorrectionRecorder(
        EditorialMetadataPreferenceRecorder recorder,
        IEditorialWriterLearningStore? writer = null)
    {
        _recorder = recorder ?? throw new ArgumentNullException(
            nameof(recorder));
        _writer = writer;
    }

    public bool TryRecordCorrection(GenerationOutputAsset asset, string beforeTitle, string beforeDescription,
        string beforeTags, string afterTitle, string afterDescription, string afterTags)
    {
        bool structural = TryRecordCorrection(beforeTitle, beforeDescription, beforeTags, afterTitle, afterDescription, afterTags);
        try
        {
            bool wording = _writer?.Record(asset.CreateCurrentCutEditorialContext().PrepareForEditorialGeneration(),
                beforeTitle, beforeDescription, ClipEditorialProfileTags.Parse(beforeTags),
                afterTitle, afterDescription, ClipEditorialProfileTags.Parse(afterTags)) == true;
            return structural || wording;
        }
        catch (Exception exception)
        {
            SafeDiagnosticTrace.Write("Local wording example could not be saved", exception);
            return structural;
        }
    }

    public bool TryRecordApproval(GenerationOutputAsset asset)
    {
        if (asset.EditorialMetadata is not { } wording) return false;
        try
        {
            return _writer?.Record(asset.CreateCurrentCutEditorialContext().PrepareForEditorialGeneration(),
                wording.Title, wording.Description, wording.Tags, wording.Title, wording.Description,
                wording.Tags, explicitApproval: true) == true;
        }
        catch (Exception exception)
        {
            SafeDiagnosticTrace.Write("Local wording approval could not be saved", exception);
            return false;
        }
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
