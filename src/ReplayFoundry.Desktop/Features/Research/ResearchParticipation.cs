namespace ReplayFoundry.Desktop.Features.Research;

public sealed class ResearchParticipationSnapshot
{
    public const string CurrentNoticeVersion =
        "research-participation-notice-1.0";

    public ResearchParticipationSnapshot(
        bool isEnabled,
        DateTimeOffset? enabledAtUtc,
        string noticeVersion = CurrentNoticeVersion)
    {
        if (string.IsNullOrWhiteSpace(noticeVersion) ||
            isEnabled != enabledAtUtc.HasValue ||
            enabledAtUtc is { } enabled && enabled.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Research participation requires an explicit versioned UTC consent state.");
        }

        IsEnabled = isEnabled;
        EnabledAtUtc = enabledAtUtc;
        NoticeVersion = noticeVersion.Trim();
    }

    public static ResearchParticipationSnapshot Disabled { get; } =
        new(false, null);

    public bool IsEnabled { get; }
    public DateTimeOffset? EnabledAtUtc { get; }
    public string NoticeVersion { get; }
}

public interface IResearchParticipationStore
{
    bool IsPersistent { get; }
    ResearchParticipationSnapshot Current { get; }
    void Replace(ResearchParticipationSnapshot value);
}

public sealed class InMemoryResearchParticipationStore :
    IResearchParticipationStore
{
    private ResearchParticipationSnapshot _current;

    public InMemoryResearchParticipationStore(
        ResearchParticipationSnapshot? initial = null)
    {
        _current = initial ?? ResearchParticipationSnapshot.Disabled;
    }

    public bool IsPersistent => false;
    public ResearchParticipationSnapshot Current => _current;
    public void Replace(ResearchParticipationSnapshot value) =>
        _current = value ?? throw new ArgumentNullException(nameof(value));
}

public sealed class ResearchParticipationState
{
    private readonly IResearchParticipationStore _store;
    private ResearchParticipationSnapshot _current;

    public ResearchParticipationState(IResearchParticipationStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = store.Current;
    }

    public event EventHandler? Changed;

    public bool IsPersistent => _store.IsPersistent;
    public bool IsEnabled => _current.IsEnabled;
    public DateTimeOffset? EnabledAtUtc => _current.EnabledAtUtc;
    public string NoticeVersion => _current.NoticeVersion;

    public void Enable(DateTimeOffset enabledAtUtc) => Replace(
        new ResearchParticipationSnapshot(true, enabledAtUtc));

    public void Disable() => Replace(ResearchParticipationSnapshot.Disabled);

    private void Replace(ResearchParticipationSnapshot value)
    {
        _store.Replace(value);
        _current = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
