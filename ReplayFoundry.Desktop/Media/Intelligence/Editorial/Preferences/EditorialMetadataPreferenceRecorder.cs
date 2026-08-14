namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;

public sealed class EditorialMetadataPreferenceRecorder
{
    private readonly object _gate = new();
    private readonly IEditorialMetadataPreferenceLearningConsent _consent;
    private readonly Func<IEditorialMetadataPreferenceStore> _storeFactory;
    private IEditorialMetadataPreferenceStore? _store;

    public EditorialMetadataPreferenceRecorder(
        IEditorialMetadataPreferenceLearningConsent consent,
        Func<IEditorialMetadataPreferenceStore> storeFactory)
    {
        _consent = consent ??
            throw new ArgumentNullException(nameof(consent));
        _storeFactory = storeFactory ??
            throw new ArgumentNullException(nameof(storeFactory));
    }

    public bool IsEnabled => _consent.IsEnabled;

    public bool TryRecord(EditorialMetadataPreferenceEvidence evidence) =>
        TryUpdate(previous: null, evidence);

    public bool TryUpdate(
        EditorialMetadataPreferenceEvidence? previous,
        EditorialMetadataPreferenceEvidence current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!_consent.IsEnabled)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_consent.IsEnabled)
            {
                return false;
            }
            (_store ??= _storeFactory()).Update(previous, current);
            return true;
        }
    }
}
