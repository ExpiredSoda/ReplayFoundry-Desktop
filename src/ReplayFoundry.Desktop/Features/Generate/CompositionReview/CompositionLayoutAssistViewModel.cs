using System.ComponentModel;
using System.Windows.Input;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public sealed class CompositionLayoutAssistViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CompositionPreviewViewModel _preview;
    private readonly CompositionRegionCollectionViewModel _regions;
    private readonly ICompositionLayoutSuggestionService? _service;
    private readonly AsyncDelegateCommand _suggest;
    private readonly DelegateCommand _apply;
    private CancellationTokenSource? _cancellation;
    private CompositionLayoutSuggestion? _suggestion;
    private bool _disposed;

    public CompositionLayoutAssistViewModel(CompositionPreviewViewModel preview,
        CompositionRegionCollectionViewModel regions, ICompositionLayoutSuggestionService? service)
    {
        _preview = preview;
        _regions = regions;
        _service = service;
        _suggest = new AsyncDelegateCommand(SuggestAsync, () => !_disposed && service is not null && preview.IsCurrent && !preview.IsLoading);
        _apply = new DelegateCommand(Apply, () => !_disposed && _suggestion is not null && preview.IsCurrent);
        preview.PropertyChanged += OnPreviewChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SuggestCommand => _suggest;
    public ICommand ApplyCommand => _apply;
    public bool HasSuggestion => _suggestion is not null;
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "Get a starting layout from a face in this frame. You can adjust every box.";

    public async Task SuggestAsync()
    {
        if (_disposed || IsBusy || _service is null || !_preview.IsCurrent || _preview.Frame is not VideoPreviewFrame frame) return;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _suggestion = null;
        IsBusy = true;
        Status = "Looking for a camera face in this frame…";
        Notify();
        try
        {
            CompositionLayoutSuggestion? result = await _service.SuggestAsync(frame, cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(frame, _preview.Frame)) return;
            _suggestion = result;
            Status = result?.Description ?? "No single clear face found. Try another frame, or draw your camera area below.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_disposed) Status = "The face check isn’t available here. You can still draw each area below.";
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            IsBusy = false;
            if (!_disposed) Notify();
        }
    }

    private void Apply()
    {
        if (_disposed || !_preview.IsCurrent || _suggestion is not { } suggestion) return;
        // Existing manual regions stay intact. Only replace the untouched full-frame starting box.
        CompositionRegionDraftViewModel? gameplay = _regions.Regions.Count == 1 ? _regions.Regions[0] : null;
        if (suggestion.Gameplay is { } game && gameplay is { Role: CompositionRegionRole.Gameplay, X: 0, Y: 0, Width: 1, Height: 1 })
            gameplay.SetGeometry(game.X, game.Y, game.Width, game.Height);
        CompositionRegionDraftViewModel presenter = _regions.AddRegion(CompositionRegionRole.Presenter);
        NormalizedRectangle box = suggestion.Presenter;
        presenter.SetGeometry(box.X, box.Y, box.Width, box.Height);
        _suggestion = null;
        Status = "Boxes added. Drag the corners to fit your camera and game, then confirm this video. A game character can also look like a face.";
        Notify();
    }

    private void OnPreviewChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(CompositionPreviewViewModel.Frame) or nameof(CompositionPreviewViewModel.RequestedTimestamp))
        {
            _cancellation?.Cancel();
            _suggestion = null;
            Status = "Get a starting layout from this frame. You can adjust every box.";
        }
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        _suggest.RaiseCanExecuteChanged();
        _apply.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preview.PropertyChanged -= OnPreviewChanged;
        _cancellation?.Cancel();
        // The in-flight operation owns disposal.
    }
}
