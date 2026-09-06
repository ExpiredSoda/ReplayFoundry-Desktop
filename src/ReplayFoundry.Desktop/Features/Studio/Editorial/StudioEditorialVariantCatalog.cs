namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

internal static class StudioEditorialVariantCatalog
{
    internal static IReadOnlyList<StudioEditorialVariantChoice> CreateChoices() =>
        [
            new(
                StudioEditorialVariant.DirectAction,
                "Straightforward",
                "Say clearly what happens in the clip."),
            new(
                StudioEditorialVariant.SpecificCuriosity,
                "Curiosity",
                "Create interest without giving away the result."),
            new(
                StudioEditorialVariant.OutcomeFocused,
                "Lead with the result",
                "Start with the clearest visible result."),
            new(
                StudioEditorialVariant.ConcreteDetail,
                "Standout detail",
                "Lead with a specific object, obstacle, or detail supported by the clip."),
            new(
                StudioEditorialVariant.CommentaryLed,
                "Creator commentary",
                "Use reviewed creator speech when available; otherwise keep the angle grounded in the action."),
        ];
}
