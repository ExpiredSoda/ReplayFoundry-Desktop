using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Media.Intelligence.Learning;

public enum TasteCorrectionReason { WrongCategory, WrongSpeaker, MissingSetup, MissingPayoff, Repetitive, NotInteresting, WrongCaption, WrongWording }

/// <summary>A human correction to an immutable source cut, separate from its preference rating.</summary>
public sealed record TasteMomentCorrection(TasteCorrectionReason Reason, SceneMomentCategory? CorrectCategory = null)
{
    public bool IsContentOrBoundaryCorrection => Reason is TasteCorrectionReason.WrongCategory or TasteCorrectionReason.WrongSpeaker or
        TasteCorrectionReason.MissingSetup or TasteCorrectionReason.MissingPayoff or TasteCorrectionReason.WrongCaption or TasteCorrectionReason.WrongWording;
    public void Validate()
    {
        if (!Enum.IsDefined(Reason) || CorrectCategory is { } category &&
            (!Enum.IsDefined(category) || Reason != TasteCorrectionReason.WrongCategory))
            throw new ArgumentException("A correction requires a defined reason and an optional corrected category.");
    }
}
