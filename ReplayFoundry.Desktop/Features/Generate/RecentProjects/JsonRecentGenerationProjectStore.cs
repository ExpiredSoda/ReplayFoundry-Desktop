using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;

namespace ReplayFoundry.Desktop.Features.Generate.RecentProjects;

public sealed class JsonRecentGenerationProjectStore
{
    private const string SchemaVersion = "2.0";
    private readonly string _path;

    public JsonRecentGenerationProjectStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReplayFoundry",
            "RecentGenerationProjects.json");
    }

    public IReadOnlyList<RecentGenerationProject> Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }
            StoreDocument? document = JsonSerializer.Deserialize<StoreDocument>(
                File.ReadAllText(_path));
            if (document is null ||
                document.SchemaVersion is not ("1.0" or SchemaVersion) ||
                document.Items is null)
            {
                return [];
            }
            return document.Items
                .Select(static item => new RecentGenerationProject(
                    item.ProjectId,
                    item.Mode,
                    item.SourcePaths,
                    item.ClipCount,
                    item.CreatedAtUtc,
                    item.IsFinalized,
                    item.IsStudioReady))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or ArgumentException)
        {
            return [];
        }
    }

    public void Write(IEnumerable<RecentGenerationProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        string? directory = Path.GetDirectoryName(_path);
        Directory.CreateDirectory(directory!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var document = new StoreDocument(
                SchemaVersion,
                projects.Select(static value => new StoreItem(
                    value.ProjectId,
                    value.Mode,
                    value.SourcePaths.ToArray(),
                    value.ClipCount,
                    value.CreatedAtUtc,
                    value.IsFinalized,
                    IsStudioReady: false)).ToArray());
            File.WriteAllText(temporary, JsonSerializer.Serialize(document));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private sealed record StoreDocument(string SchemaVersion, StoreItem[] Items);
    private sealed record StoreItem(
        string ProjectId,
        GenerationMode Mode,
        string[] SourcePaths,
        int ClipCount,
        DateTimeOffset CreatedAtUtc,
        bool IsFinalized,
        bool IsStudioReady = false);
}
