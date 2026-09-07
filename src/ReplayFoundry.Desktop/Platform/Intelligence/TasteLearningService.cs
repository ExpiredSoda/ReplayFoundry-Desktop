using ReplayFoundry.Desktop.Media.Intelligence.Learning;

namespace ReplayFoundry.Desktop.Platform.Intelligence;

// The public source distribution contains the integration contract, not the proprietary learning engine.
public sealed class TasteLearningService : ITasteLearningService, IDisposable
{
    public TasteLearningStatus Status { get; } = new(false, false, false, 0, 0, 0,
        "Personal learning is included in the official Replay Foundry download.");
    public DateTimeOffset HistoryStartUtc => DateTimeOffset.MaxValue;
    public event EventHandler? Changed { add { } remove { } }
    public void Observe(TasteClip clip, TasteSignal? signal) { }
    public Task<IReadOnlyDictionary<string, TastePrediction>> PredictAsync(
        IReadOnlyList<TasteClip> clips, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, TastePrediction>>(new Dictionary<string, TastePrediction>());
    public Task TrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public void SetEnabled(bool enabled) { }
    public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public void Dispose() { }
}
