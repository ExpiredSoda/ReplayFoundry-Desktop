using System.ComponentModel;
using EditorialWordingFeedback = ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences.EditorialWordingFeedback;

namespace ReplayFoundry.Desktop.Features.Studio.Editorial;

public sealed record StudioWordingCorrectionChoice(string Code, string Name, bool NeedsFacts = false);

/// <summary>Optional learning feedback belongs to the current cut and survives draft saves.</summary>
public sealed class StudioWordingLearningViewModel(Func<bool> enabled) : INotifyPropertyChanged
{
    public IReadOnlyList<StudioWordingCorrectionChoice> CorrectionChoices { get; } =
    [
        new("Unspecified", "Just saving my changes"),
        new("Style", "Make it sound more like me"),
        new("TooGeneric", "Describe the moment more clearly"),
        new("WrongSpeaker", "Fix who is speaking or acting", true),
        new("WrongEvent", "Fix what happens", true),
        new("InventedOutcome", "Remove something that did not happen", true),
        new("WrongScope", "Match this cut or montage", true),
    ];
    private StudioWordingCorrectionChoice? _choice;
    private string _event = string.Empty;
    private (string?, long?, long?) _cut;
    public bool CanTeachWording => enabled();
    public void RefreshAvailability() => Changed(nameof(CanTeachWording));
    public StudioWordingCorrectionChoice CorrectionChoice
    {
        get => _choice ?? CorrectionChoices[0];
        set
        {
            if (value is null || !CorrectionChoices.Contains(value)) return;
            _choice = value;
            Changed(nameof(CorrectionChoice));
            Changed(nameof(CorrectionNeedsFacts));
        }
    }
    public bool CorrectionNeedsFacts => CorrectionChoice.NeedsFacts;
    public string CorrectedEvent
    {
        get => _event;
        set { _event = (value ?? string.Empty)[..Math.Min(value?.Length ?? 0, 600)]; Changed(nameof(CorrectedEvent)); }
    }
    internal EditorialWordingFeedback Snapshot() => new(CorrectionChoice.Code, CorrectionNeedsFacts ? CorrectedEvent : string.Empty);
    internal static EditorialWordingFeedback FeedbackFrom(StudioPendingEditorialDraft draft) => new(draft.CorrectionReason, draft.CorrectedEvent);
    internal StudioPendingEditorialDraft CaptureDraft(string title, string description, string tags) =>
        new(title, description, tags, CorrectionChoice.Code, Snapshot().CorrectedEvent);
    internal void Restore(StudioPendingEditorialDraft draft)
    {
        CorrectionChoice = CorrectionChoices.FirstOrDefault(choice => choice.Code == draft.CorrectionReason) ?? CorrectionChoices[0];
        CorrectedEvent = draft.CorrectedEvent;
    }
    internal void Bind(string? id, long? start, long? end)
    {
        if (_cut != (id, start, end))
        {
            CorrectionChoice = CorrectionChoices[0];
            CorrectedEvent = string.Empty;
            _cut = (id, start, end);
        }
        RefreshAvailability();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
