namespace ReplayFoundry.Desktop.Features.Settings;

public sealed record EditorialRerollPreferenceSnapshot(
    bool UseLocalAi);

public interface IEditorialRerollPreferenceStore
{
    bool IsPersistent { get; }
    EditorialRerollPreferenceSnapshot Current { get; }
    void Replace(EditorialRerollPreferenceSnapshot preference);
}

public sealed class InMemoryEditorialRerollPreferenceStore :
    IEditorialRerollPreferenceStore
{
    private EditorialRerollPreferenceSnapshot _current;

    public InMemoryEditorialRerollPreferenceStore(
        EditorialRerollPreferenceSnapshot? initial = null)
    {
        _current = initial ?? new EditorialRerollPreferenceSnapshot(
            UseLocalAi: false);
    }

    public bool IsPersistent => false;

    public EditorialRerollPreferenceSnapshot Current => _current;

    public void Replace(EditorialRerollPreferenceSnapshot preference)
    {
        ArgumentNullException.ThrowIfNull(preference);
        _current = preference;
    }
}

public interface IEditorialRerollPreference
{
    event EventHandler? Changed;

    bool UseLocalAi { get; }
    bool IsPersistent { get; }
}

/// <summary>
/// Owns the user's provider choice for explicit Studio and Publish metadata
/// rerolls. The choice is deliberately binary: local AI is required when on,
/// and the deterministic grounded generator is required when off. It never
/// selects the optional-AI fallback mode.
/// </summary>
public sealed class EditorialRerollPreferenceState :
    IEditorialRerollPreference
{
    private readonly IEditorialRerollPreferenceStore _store;
    private EditorialRerollPreferenceSnapshot _current;

    public EditorialRerollPreferenceState(
        IEditorialRerollPreferenceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = store.Current;
    }

    public event EventHandler? Changed;

    public bool UseLocalAi => _current.UseLocalAi;

    public bool IsPersistent => _store.IsPersistent;

    public void SetUseLocalAi(bool useLocalAi)
    {
        if (UseLocalAi == useLocalAi)
        {
            return;
        }

        var replacement = new EditorialRerollPreferenceSnapshot(useLocalAi);
        _store.Replace(replacement);
        _current = replacement;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
