namespace ReplayFoundry.Desktop.Features.Library;

public interface IBrowsePreferencesStore
{
    string? Get(string key);
    void Set(string key, string value);
}
