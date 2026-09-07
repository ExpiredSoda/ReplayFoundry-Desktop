using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record CaptionLookChoice(
    string Name,
    GenerationCaptionStylePreset Style,
    string Description,
    StudioNamedCaptionLook? Saved = null)
{
    public StudioCaptionLook? Look => Saved?.Look;
    public bool IsSaved => Saved is not null;
    public string Kind => IsSaved ? "Saved look" : "Built in";
    public StudioCaptionTypography Typography => Look?.CaptionTypography ?? StudioCaptionTypography.Default;

    public static IReadOnlyList<CaptionLookChoice> Create(IEnumerable<StudioNamedCaptionLook> saved) =>
        StudioSurfaceCatalog.CaptionStyles.Select(option =>
            new CaptionLookChoice(option.Name, option.Value, option.Description))
        .Concat(saved.Select(look => new CaptionLookChoice(look.Name, look.Look.CaptionStyle,
            "Your saved font, colors, animation and placement.", look))).ToArray();
}
