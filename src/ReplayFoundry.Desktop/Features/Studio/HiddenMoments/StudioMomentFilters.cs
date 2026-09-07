using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Studio.HiddenMoments;

public sealed class StudioMomentFilters(Action changed) : ObservableObject
{
    private bool _gameplay, _commentary, _funny, _unclassified;
    public bool All { get => !_gameplay && !_commentary && !_funny && !_unclassified; set { if (value) Reset(); else Notify(); } }
    public bool Gameplay { get => _gameplay; set { _gameplay = value; _unclassified = false; Notify(); } }
    public bool Commentary { get => _commentary; set { _commentary = value; _unclassified = false; Notify(); } }
    public bool Funny { get => _funny; set { _funny = value; _unclassified = false; Notify(); } }
    public bool Unclassified
    {
        get => _unclassified;
        set { _unclassified = value; if (value) _gameplay = _commentary = _funny = false; Notify(); }
    }
    public void Reset() { _gameplay = _commentary = _funny = _unclassified = false; Notify(); }
    public bool Matches(ClipPreferenceFeatureVector? features) => Matches(Profile(features));
    public bool Matches(MomentContentProfile profile) => _unclassified ? !profile.HasLabels && !profile.VisualReviewCompleted :
        (!_gameplay || profile.Gameplay) && (!_commentary || profile.Commentary) && (!_funny || profile.Funny);

    public static MomentContentProfile Profile(ClipPreferenceFeatureVector? features) => features?.DetectedContent ??
        new(Commentary: features?.Find(ClipPreferenceFeatureCode.CreatorSpeech) >= 0.15);
    public static string Label(MomentContentProfile profile)
    {
        string[] labels = [.. profile.Gameplay ? new[] { "Gameplay" } : [],
            .. profile.Commentary ? new[] { "Commentary" } : [], .. profile.Funny ? new[] { "Funny" } : []];
        return labels.Length > 0 ? string.Join(" · ", labels) : profile.VisualReviewCompleted ? "Other content" : "Not yet classified";
    }
    private void Notify()
    {
        foreach (string property in new[] { nameof(All), nameof(Gameplay), nameof(Commentary), nameof(Funny), nameof(Unclassified) })
            OnPropertyChanged(property);
        changed();
    }
}
