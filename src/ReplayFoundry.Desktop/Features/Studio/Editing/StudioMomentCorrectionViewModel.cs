using System.ComponentModel;
using TasteMomentCorrection = ReplayFoundry.Desktop.Media.Intelligence.Learning.TasteMomentCorrection;
using TasteCorrectionReason = ReplayFoundry.Desktop.Media.Intelligence.Learning.TasteCorrectionReason;
using SceneMomentCategory = ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic.SceneMomentCategory;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioCorrectionChoice(TasteCorrectionReason? Value, string Label);
public sealed record StudioCorrectionCategory(SceneMomentCategory? Value, string Label);

public sealed class StudioMomentCorrectionViewModel(Action<TasteMomentCorrection?> save) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<StudioCorrectionChoice> Reasons { get; } = new StudioCorrectionChoice[]
    {
        new(null, "Optional feedback"), new(TasteCorrectionReason.WrongCategory, "Wrong kind of moment"),
        new(TasteCorrectionReason.WrongSpeaker, "Wrong speaker"), new(TasteCorrectionReason.MissingSetup, "Missing the setup"),
        new(TasteCorrectionReason.MissingPayoff, "Missing the ending"), new(TasteCorrectionReason.Repetitive, "Too repetitive"),
        new(TasteCorrectionReason.NotInteresting, "Not interesting to me"),
        new(TasteCorrectionReason.WrongCaption, "Caption problem (keep moment feedback separate)"),
        new(TasteCorrectionReason.WrongWording, "Title or description problem"),
    };
    public IReadOnlyList<StudioCorrectionCategory> Categories { get; } = new[] { new StudioCorrectionCategory(null, "Correct category (optional)") }
        .Concat(Enum.GetValues<SceneMomentCategory>().Select(category => new StudioCorrectionCategory(category,
            category == SceneMomentCategory.Humor ? "Funny" : category.ToString()))).ToArray();
    private StudioCorrectionChoice? _reason;
    private StudioCorrectionCategory? _category;
    public bool IsEnabled { get; private set; }
    public StudioCorrectionChoice SelectedReason
    {
        get => _reason ?? Reasons[0];
        set
        {
            if (!IsEnabled || value is null || !Reasons.Contains(value) || value == SelectedReason) return;
            _reason = value;
            if (value.Value != TasteCorrectionReason.WrongCategory) _category = Categories[0];
            Notify(); save(Current);
        }
    }
    public StudioCorrectionCategory SelectedCategory
    {
        get => _category ?? Categories[0];
        set
        {
            if (!IsEnabled || !NeedsCategory || value is null || !Categories.Contains(value) || value == SelectedCategory) return;
            _category = value; Notify(); save(Current);
        }
    }
    public bool NeedsCategory => SelectedReason.Value == TasteCorrectionReason.WrongCategory;
    public TasteMomentCorrection? Current => SelectedReason.Value is { } reason ? new(reason, SelectedCategory.Value) : null;
    internal void Bind(TasteMomentCorrection? correction, bool enabled)
    {
        _reason = Reasons.Single(choice => choice.Value == correction?.Reason);
        _category = Categories.Single(choice => choice.Value == correction?.CorrectCategory);
        IsEnabled = enabled; Notify();
    }
    private void Notify()
    {
        foreach (string name in new[] { nameof(SelectedReason), nameof(SelectedCategory), nameof(NeedsCategory), nameof(IsEnabled) })
            PropertyChanged?.Invoke(this, new(name));
    }
}
