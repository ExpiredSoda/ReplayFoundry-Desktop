using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public interface IClipEditorialProfileStore
{
    ClipEditorialProfile Load();
    void Save(ClipEditorialProfile profile);
}
