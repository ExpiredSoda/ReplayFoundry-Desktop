using System.IO;

namespace ReplayFoundry.PreparationTests;

internal sealed class TasteScratch : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ReplayFoundry-TasteTests", Guid.NewGuid().ToString("N"));
    internal TasteScratch() => Directory.CreateDirectory(Path);
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
}
