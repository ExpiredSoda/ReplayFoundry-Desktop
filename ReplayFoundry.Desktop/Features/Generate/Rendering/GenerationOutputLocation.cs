using System.IO;

namespace ReplayFoundry.Desktop.Features.Generate.Rendering;

public interface IGenerationOutputLocationStore
{
    string? CustomRootDirectory { get; }
    bool IsPersistent { get; }
    void Save(string? customRootDirectory);
}

public sealed class InMemoryGenerationOutputLocationStore :
    IGenerationOutputLocationStore
{
    public string? CustomRootDirectory { get; private set; }
    public bool IsPersistent => false;

    public void Save(string? customRootDirectory)
    {
        CustomRootDirectory = customRootDirectory;
    }
}

public sealed class GenerationOutputLocationState
{
    private readonly IGenerationOutputLocationStore _store;
    private readonly string _defaultRootDirectory;
    private string? _customRootDirectory;

    public GenerationOutputLocationState(
        IGenerationOutputLocationStore store,
        string? defaultRootDirectory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _defaultRootDirectory = NormalizeRoot(
            defaultRootDirectory ?? ResolveDefaultRootDirectory());
        _customRootDirectory = string.IsNullOrWhiteSpace(
            store.CustomRootDirectory)
            ? null
            : NormalizeRoot(store.CustomRootDirectory);
    }

    public event EventHandler? Changed;

    public string OutputRootDirectory =>
        _customRootDirectory ?? _defaultRootDirectory;
    public string DefaultRootDirectory => _defaultRootDirectory;
    public bool UsesCustomRoot => _customRootDirectory is not null;
    public bool IsPersistent => _store.IsPersistent;

    public void SetCustomRoot(string rootDirectory)
    {
        string normalized = NormalizeRoot(rootDirectory);
        EnsureWritable(normalized);
        if (normalized.Equals(
            _defaultRootDirectory,
            StringComparison.OrdinalIgnoreCase))
        {
            UseDefaultRoot();
            return;
        }
        if (normalized.Equals(
            _customRootDirectory,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _store.Save(normalized);
        _customRootDirectory = normalized;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UseDefaultRoot()
    {
        EnsureWritable(_defaultRootDirectory);
        if (_customRootDirectory is null)
        {
            return;
        }

        _store.Save(null);
        _customRootDirectory = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void EnsureCurrentRootIsWritable() =>
        EnsureWritable(OutputRootDirectory);

    public static string ResolveDefaultRootDirectory()
    {
        string videos = Environment.GetFolderPath(
            Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videos))
        {
            videos = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
        }
        if (string.IsNullOrWhiteSpace(videos))
        {
            throw new InvalidOperationException(
                "Replay Foundry could not resolve a writable per-user output root.");
        }

        return Path.Combine(videos, "ReplayFoundry");
    }

    private static string NormalizeRoot(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException(
                "A Replay Foundry output root must be fully qualified.",
                nameof(value));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static void EnsureWritable(string rootDirectory)
    {
        if (File.Exists(rootDirectory))
        {
            throw new IOException(
                "The selected output location is a file, not a folder.");
        }

        Directory.CreateDirectory(rootDirectory);
        string probe = Path.Combine(
            rootDirectory,
            ".replayfoundry-write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using FileStream stream = new(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
    }
}
