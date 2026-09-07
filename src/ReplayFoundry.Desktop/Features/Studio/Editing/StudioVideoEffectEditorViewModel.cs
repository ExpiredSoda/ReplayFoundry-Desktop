using System.ComponentModel;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioVideoEffectEditorViewModel : INotifyPropertyChanged
{
    private readonly StudioClipEditorViewModel _clip;
    private readonly DelegateCommand _reset;
    private bool _showOriginal;
    private bool _canEdit;
    private string? _assetId;

    internal StudioVideoEffectEditorViewModel(StudioClipEditorViewModel clip)
    {
        _clip = clip;
        _reset = new DelegateCommand(Reset, () => CanAdjust);
        _clip.PropertyChanged += (_, _) => NotifyState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ComparisonChanged;
    public bool HasEffect => _clip.SelectedVideoEffect.Value != StudioVideoEffectPreset.None;
    public bool CanAdjust => _canEdit && HasEffect && !_clip.IsApplyingBoundaryEdit;
    public ICommand ResetCommand => _reset;
    public string ComparisonStatus => ShowOriginal
        ? "Showing original color for comparison. Your chosen effect has not changed."
        : "Changes save automatically and appear in the finished video.";
    public bool ShowOriginal
    {
        get => _showOriginal;
        set
        {
            if (_showOriginal == value) return;
            _showOriginal = value;
            NotifyState();
            ComparisonChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Comparison is a preview override, never part of the draft that is saved or exported.
    public StudioClipAppearance PreviewAppearance
    {
        get
        {
            var draft = _clip.DraftAppearance;
            return !ShowOriginal ? draft : WithoutColorEffect(draft);
        }
    }

    internal GenerationOutputAsset? ForPreview(GenerationOutputAsset? asset) =>
        !ShowOriginal || asset is null ? asset : asset.WithStudioEdits(
            asset.SourceStart, asset.SourceEnd, WithoutColorEffect(asset.Appearance));

    private static StudioClipAppearance WithoutColorEffect(StudioClipAppearance appearance) => new(
        appearance.CaptionStyle, appearance.CaptionVerticalPositionPercent,
        StudioVideoEffectPreset.None, 0, appearance.GraphicOverlays,
        appearance.CaptionWordLimit, appearance.CaptionMaximumWidthPercent,
        appearance.CaptionFontScalePercent, appearance.CaptionTypography);

    internal void Bind(GenerationOutputProject? project, GenerationOutputAsset? asset)
    {
        if (_assetId != asset?.Id) _showOriginal = false;
        _assetId = asset?.Id;
        _canEdit = asset is not null && project?.IsFinalized == false;
        NotifyState();
    }

    private void Reset()
    {
        ShowOriginal = false;
        _clip.SelectedVideoEffect = _clip.VideoEffectOptions.Single(o => o.Value == StudioVideoEffectPreset.None);
        _clip.VideoEffectIntensityPercent = 0;
    }

    private void NotifyState()
    {
        if (_showOriginal && !HasEffect)
        {
            _showOriginal = false;
            ComparisonChanged?.Invoke(this, EventArgs.Empty);
        }
        foreach (var name in new[] { nameof(HasEffect), nameof(CanAdjust), nameof(ShowOriginal), nameof(ComparisonStatus) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        _reset.RaiseCanExecuteChanged();
    }
}
