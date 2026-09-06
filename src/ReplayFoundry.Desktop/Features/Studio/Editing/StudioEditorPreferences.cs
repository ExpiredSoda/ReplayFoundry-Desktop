using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioNamedCaptionLook(string Name, StudioCaptionLook Look);

public interface IStudioCaptionLookStore
{
    IReadOnlyList<StudioNamedCaptionLook> Load();
    void Save(string name, StudioCaptionLook look);
}

public interface IStudioLayoutPresetStore
{
    IReadOnlyList<StudioLayoutPreset> Load();
    void Save(StudioLayoutPreset preset);
}

internal static class StudioEditorPreferences
{
    internal static IStudioCaptionLookStore CreateCaptionLooks() => new JsonStudioCaptionLookStore();
    internal static IStudioLayoutPresetStore CreateLayouts() => new JsonStudioLayoutPresetStore();
}
