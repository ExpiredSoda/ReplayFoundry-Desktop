using System.ComponentModel;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioTimelineEditViewModel : INotifyPropertyChanged
{
    private readonly IGenerationOutputEditor? _editor;
    private readonly IGenerationTimelineEditor? _timeline;
    private readonly Func<bool> _hasNoPendingDraft;
    private readonly DelegateCommand _split, _earlier, _later;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private bool _busy;
    public StudioTimelineEditViewModel(IGenerationOutputEditor? editor, Func<bool> hasNoPendingDraft)
    {
        _editor = editor; _timeline = editor as IGenerationTimelineEditor; _hasNoPendingDraft = hasNoPendingDraft;
        _split = new DelegateCommand(Split, () => CanEdit);
        _earlier = new DelegateCommand(() => Move(-1), () => CanEdit && _asset!.Rank > 1);
        _later = new DelegateCommand(() => Move(1), () => CanEdit && _asset!.Rank < _project!.Assets.Count);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool CanEdit => !_busy && _timeline is not null && _asset is not null && _project?.IsFinalized == false;
    public IReadOnlyList<GenerationMode> Modes { get; } = [GenerationMode.IndividualClips, GenerationMode.Montage];
    public string Summary => _project is null ? "Choose a clip." :
        $"{_project.IncludedCount} kept sections · {_project.IncludedAssets.Sum(asset => asset.Duration.TotalSeconds):0.##} seconds";
    public string Status { get; private set; } = "Split twice and remove the middle section to make a jump cut. Montage joins kept sections in their listed order.";
    public double SplitOffsetSeconds { get; set; }
    public GenerationMode Mode
    {
        get => _project?.Mode ?? GenerationMode.IndividualClips;
        set { if (value != Mode) Change(() => _timeline!.SetTimelineMode(_project!.Id, value)); }
    }
    public bool IsKept
    {
        get => _asset?.IsIncludedInFinalRender == true;
        set
        {
            if (value == IsKept) return;
            Change(() => _editor!.ReplaceAsset(_project!.Id, _asset!.WithDisposition(value
                ? GenerationOutputAssetDisposition.IncludeInFinalRender : GenerationOutputAssetDisposition.ExcludeFromFinalRender)));
        }
    }
    public ICommand SplitCommand => _split;
    public ICommand MoveEarlierCommand => _earlier;
    public ICommand MoveLaterCommand => _later;
    public void Bind(GenerationOutputProject? project, GenerationOutputAsset? asset)
    {
        if (_asset?.Id != asset?.Id || _project?.Id != project?.Id || SplitOffsetSeconds >= asset?.Duration.TotalSeconds)
            SplitOffsetSeconds = Math.Round((asset?.Duration.TotalSeconds ?? 0) / 2, 3);
        _project = project; _asset = asset; Notify();
    }
    public void SetHostBusy(bool busy) { _busy = busy; Notify(); }
    private void Split() => Change(() =>
    {
        if (!double.IsFinite(SplitOffsetSeconds)) throw new ArgumentException("Choose a valid split time inside the clip.");
        _timeline!.SplitAsset(_project!.Id, _asset!.Id, _asset.SourceStart + TimeSpan.FromSeconds(SplitOffsetSeconds));
    });
    private void Move(int direction) => Change(() => _timeline!.MoveAsset(_project!.Id, _asset!.Id, direction));
    private void Change(Action action)
    {
        if (!CanEdit) return;
        if (!_hasNoPendingDraft())
        {
            Status = "Save or reset the pending boundary, caption, graphic, or metadata edit before changing the cut list.";
            Notify(); return;
        }
        try { action(); Status = "Cut list saved. Review split sections' captions and metadata before rendering."; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException) { Status = error.Message; }
        Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this, new(string.Empty));
        _split.RaiseCanExecuteChanged(); _earlier.RaiseCanExecuteChanged(); _later.RaiseCanExecuteChanged();
    }
}
