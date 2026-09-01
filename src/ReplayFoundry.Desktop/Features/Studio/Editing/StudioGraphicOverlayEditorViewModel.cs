using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal sealed record StudioGraphicPlacementDraftSnapshot(
    string OverlayId,
    double CenterXPercent,
    double CenterYPercent,
    double WidthPercent);

public sealed class StudioGraphicOverlayEditorViewModel : INotifyPropertyChanged
{
    private readonly IGenerationOutputEditor? _outputEditor;
    private readonly DelegateCommand _applyCommand;
    private readonly DelegateCommand _removeCommand;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private StudioGraphicOverlay? _selectedOverlay;
    private double _centerXPercent = 50;
    private double _centerYPercent = 50;
    private double _widthPercent = 30;
    private string _status = "Drop a PNG, JPG, or WebP onto the preview to add it.";
    private string? _error;
    private bool _isHostBusy;

    public StudioGraphicOverlayEditorViewModel(IGenerationOutputEditor? outputEditor)
    {
        _outputEditor = outputEditor;
        _applyCommand = new DelegateCommand(ApplyPlacement, CanEdit);
        _removeCommand = new DelegateCommand(RemoveSelected, CanEdit);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<StudioGraphicOverlay> Overlays =>
        _asset?.Appearance.GraphicOverlays ?? [];

    public StudioGraphicOverlay? SelectedOverlay
    {
        get => _selectedOverlay;
        set
        {
            if (ReferenceEquals(_selectedOverlay, value)) return;
            if (value is not null && !Overlays.Any(item => ReferenceEquals(item, value)))
                throw new ArgumentException("The selected graphic must belong to this clip.", nameof(value));
            _selectedOverlay = value;
            if (value is not null)
            {
                _centerXPercent = value.CenterXPercent;
                _centerYPercent = value.CenterYPercent;
                _widthPercent = value.WidthPercent;
            }
            NotifyAll();
        }
    }

    public double CenterXPercent
    {
        get => _centerXPercent;
        set => SetPercent(ref _centerXPercent, value, 0, 100);
    }

    public double CenterYPercent
    {
        get => _centerYPercent;
        set => SetPercent(ref _centerYPercent, value, 0, 100);
    }

    public double WidthPercent
    {
        get => _widthPercent;
        set => SetPercent(
            ref _widthPercent,
            value,
            StudioGraphicOverlay.MinimumWidthPercent,
            StudioGraphicOverlay.MaximumWidthPercent);
    }

    public string PlacementText =>
        $"Center {CenterXPercent:0.#}% × {CenterYPercent:0.#}% · width {WidthPercent:0.#}%";
    public bool HasOverlays => Overlays.Count > 0;
    public bool HasSelection => SelectedOverlay is not null;
    public bool HasUnsavedChanges => SelectedOverlay is not null &&
        (Math.Abs(CenterXPercent - SelectedOverlay.CenterXPercent) >= 0.05 ||
         Math.Abs(CenterYPercent - SelectedOverlay.CenterYPercent) >= 0.05 ||
         Math.Abs(WidthPercent - SelectedOverlay.WidthPercent) >= 0.05);
    public string Status => _status;
    public string? Error => _error;
    public bool HasError => !string.IsNullOrWhiteSpace(_error);
    public ICommand ApplyPlacementCommand => _applyCommand;
    public ICommand RemoveGraphicCommand => _removeCommand;

    public void Bind(GenerationOutputProject? project, GenerationOutputAsset? asset)
    {
        string? selectedId = _selectedOverlay?.Id;
        _project = project;
        _asset = asset;
        _selectedOverlay = Overlays.FirstOrDefault(item =>
            selectedId is not null && item.Id.Equals(selectedId, StringComparison.Ordinal)) ??
            Overlays.FirstOrDefault();
        if (_selectedOverlay is not null)
        {
            _centerXPercent = _selectedOverlay.CenterXPercent;
            _centerYPercent = _selectedOverlay.CenterYPercent;
            _widthPercent = _selectedOverlay.WidthPercent;
        }
        _error = null;
        _status = asset is null
            ? "Select a clip before adding a graphic."
            : asset.IsRendered
                ? "This finalized clip is read-only."
                : Overlays.Count == 0
                    ? "Drop a PNG, JPG, or WebP onto the preview to add it."
                    : "Choose a graphic, adjust its placement, then apply.";
        NotifyAll();
    }

    public void SetHostBusy(bool value)
    {
        if (_isHostBusy == value)
        {
            return;
        }

        _isHostBusy = value;
        NotifyAll();
    }

    internal StudioGraphicPlacementDraftSnapshot? CapturePendingDraft() =>
        HasUnsavedChanges && SelectedOverlay is not null
            ? new StudioGraphicPlacementDraftSnapshot(
                SelectedOverlay.Id,
                CenterXPercent,
                CenterYPercent,
                WidthPercent)
            : null;

    internal void RestorePendingDraft(
        StudioGraphicPlacementDraftSnapshot draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        StudioGraphicOverlay? overlay = Overlays.FirstOrDefault(item =>
            item.Id.Equals(draft.OverlayId, StringComparison.Ordinal));
        if (overlay is null || _project?.IsFinalized != false)
        {
            return;
        }

        _selectedOverlay = overlay;
        _centerXPercent = draft.CenterXPercent;
        _centerYPercent = draft.CenterYPercent;
        _widthPercent = draft.WidthPercent;
        NotifyAll();
    }

    public bool TryAddFile(string imageFullPath)
    {
        if (_isHostBusy ||
            _project?.IsFinalized != false ||
            _asset is null ||
            _outputEditor is null)
        {
            _error = "Open an editable Studio clip before adding a graphic.";
            NotifyAll();
            return false;
        }

        try
        {
            var overlay = new StudioGraphicOverlay(
                Guid.NewGuid().ToString("N"),
                imageFullPath,
                50,
                50,
                30);
            ReplaceOverlays([.. Overlays, overlay]);
            _selectedOverlay = overlay;
            _status = $"Added {overlay.DisplayName}. Adjust its center and width in Graphics controls.";
            _error = null;
            NotifyAll();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            _error = exception.Message;
            _status = "The graphic was not added.";
            NotifyAll();
            return false;
        }
    }

    private void ApplyPlacement()
    {
        if (!CanEdit()) return;
        StudioGraphicOverlay replacement = SelectedOverlay!.WithPlacement(
            CenterXPercent,
            CenterYPercent,
            WidthPercent);
        ReplaceOverlays(Overlays.Select(item =>
            ReferenceEquals(item, SelectedOverlay) ? replacement : item));
        _selectedOverlay = replacement;
        _status = "Graphic placement saved; the preview is updating automatically.";
        _error = null;
        NotifyAll();
    }

    private void RemoveSelected()
    {
        if (!CanEdit()) return;
        string name = SelectedOverlay!.DisplayName;
        ReplaceOverlays(Overlays.Where(item => !ReferenceEquals(item, SelectedOverlay)));
        _selectedOverlay = null;
        _status = $"Removed {name} from this clip.";
        _error = null;
        NotifyAll();
    }

    private void ReplaceOverlays(IEnumerable<StudioGraphicOverlay> overlays)
    {
        StudioClipAppearance current = _asset!.Appearance;
        var appearance = new StudioClipAppearance(
            current.CaptionStyle,
            current.CaptionVerticalPositionPercent,
            current.VideoEffect,
            current.VideoEffectIntensityPercent,
            overlays,
            current.CaptionWordLimit,
            current.CaptionMaximumWidthPercent,
            current.CaptionFontScalePercent);
        _outputEditor!.ReplaceAsset(
            _project!.Id,
            _asset.WithStudioEdits(_asset.SourceStart, _asset.SourceEnd, appearance));
    }

    private bool CanEdit() =>
        !_isHostBusy &&
        _outputEditor is not null &&
        _project?.IsFinalized == false &&
        _asset is not null &&
        SelectedOverlay is not null;

    private void SetPercent(ref double field, double value, double minimum, double maximum)
    {
        double normalized = Math.Round(Math.Clamp(value, minimum, maximum), 1, MidpointRounding.AwayFromZero);
        if (Math.Abs(field - normalized) < 0.05) return;
        field = normalized;
        OnPropertyChanged();
        OnPropertyChanged(nameof(PlacementText));
        _applyCommand.RaiseCanExecuteChanged();
    }

    private void NotifyAll()
    {
        foreach (string name in new[]
        {
            nameof(Overlays), nameof(SelectedOverlay), nameof(CenterXPercent),
            nameof(CenterYPercent), nameof(WidthPercent), nameof(PlacementText),
            nameof(HasOverlays), nameof(HasSelection),
            nameof(HasUnsavedChanges), nameof(Status), nameof(Error), nameof(HasError),
        }) OnPropertyChanged(name);
        _applyCommand.RaiseCanExecuteChanged();
        _removeCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
