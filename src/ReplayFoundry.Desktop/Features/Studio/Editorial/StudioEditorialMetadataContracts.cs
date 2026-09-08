namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

internal sealed record StudioPendingEditorialDraft(
    string Title,
    string Description,
    string Tags,
    string CorrectionReason = "Unspecified",
    string CorrectedEvent = "");

internal sealed record StudioPendingEditorialProfileDraft(
    string AudienceAddress,
    string NamingGuidance,
    string DescriptionSignature);

public enum StudioEditorialVariant
{
    DirectAction,
    SpecificCuriosity,
    OutcomeFocused,
    ConcreteDetail,
    CommentaryLed,
}

public sealed record StudioEditorialVariantChoice(
    StudioEditorialVariant Value,
    string Name,
    string Description);
