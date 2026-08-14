using ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;

namespace ReplayFoundry.Desktop.Features.Settings;

public sealed class EditorialMetadataPreferenceLearningConsentSnapshot
{
    public const string CurrentNoticeVersion =
        "editorial-metadata-preference-learning-notice-1.0";

    public EditorialMetadataPreferenceLearningConsentSnapshot(
        bool isEnabled,
        DateTimeOffset? enabledAtUtc,
        string noticeVersion = CurrentNoticeVersion)
    {
        if (string.IsNullOrWhiteSpace(noticeVersion) ||
            isEnabled != enabledAtUtc.HasValue ||
            enabledAtUtc is { } enabled && enabled.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Editorial metadata preference learning requires an explicit versioned UTC consent state.");
        }

        IsEnabled = isEnabled;
        EnabledAtUtc = enabledAtUtc;
        NoticeVersion = noticeVersion.Trim();
    }

    public static EditorialMetadataPreferenceLearningConsentSnapshot Disabled
    { get; } = new(false, null);

    public bool IsEnabled { get; }
    public DateTimeOffset? EnabledAtUtc { get; }
    public string NoticeVersion { get; }
}

public interface IEditorialMetadataPreferenceLearningConsentStore
{
    bool IsPersistent { get; }
    EditorialMetadataPreferenceLearningConsentSnapshot Current { get; }
    void Replace(
        EditorialMetadataPreferenceLearningConsentSnapshot value);
}

public sealed class InMemoryEditorialMetadataPreferenceLearningConsentStore :
    IEditorialMetadataPreferenceLearningConsentStore
{
    private EditorialMetadataPreferenceLearningConsentSnapshot _current;

    public InMemoryEditorialMetadataPreferenceLearningConsentStore(
        EditorialMetadataPreferenceLearningConsentSnapshot? initial = null)
    {
        _current = initial ??
            EditorialMetadataPreferenceLearningConsentSnapshot.Disabled;
    }

    public bool IsPersistent => false;
    public EditorialMetadataPreferenceLearningConsentSnapshot Current =>
        _current;

    public void Replace(
        EditorialMetadataPreferenceLearningConsentSnapshot value) =>
        _current = value ?? throw new ArgumentNullException(nameof(value));
}

public sealed class EditorialMetadataPreferenceLearningConsentState :
    IEditorialMetadataPreferenceLearningConsent
{
    private readonly IEditorialMetadataPreferenceLearningConsentStore _store;
    private EditorialMetadataPreferenceLearningConsentSnapshot _current;

    public EditorialMetadataPreferenceLearningConsentState(
        IEditorialMetadataPreferenceLearningConsentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = store.Current;
    }

    public event EventHandler? Changed;

    public bool IsPersistent => _store.IsPersistent;
    public bool IsEnabled => _current.IsEnabled;
    public DateTimeOffset? EnabledAtUtc => _current.EnabledAtUtc;
    public string NoticeVersion => _current.NoticeVersion;

    public void Enable(DateTimeOffset enabledAtUtc)
    {
        if (enabledAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The editorial metadata preference-learning consent time must use UTC.",
                nameof(enabledAtUtc));
        }
        if (IsEnabled)
        {
            return;
        }

        Replace(new EditorialMetadataPreferenceLearningConsentSnapshot(
            true,
            enabledAtUtc));
    }

    public void Disable()
    {
        if (!IsEnabled)
        {
            return;
        }

        Replace(
            EditorialMetadataPreferenceLearningConsentSnapshot.Disabled);
    }

    private void Replace(
        EditorialMetadataPreferenceLearningConsentSnapshot value)
    {
        _store.Replace(value);
        _current = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
