namespace ReplayFoundry.Desktop.Media.Intelligence.Learning;

public enum TasteSignal { Like, Neutral, Dislike, Rendered, Published, HiddenAccepted, ManualCreated }

public sealed record TasteClip(string Id, string SourceGroup, string Content, string Context, double[] Measurements, double BaselineScore)
{
    public const int MeasurementCount = 48;
    public void Validate()
    {
        if (!IsHash(Id) || !IsHash(SourceGroup) || Content is null || Context is null || Content.Length > 8192 || Context.Length > 2048 ||
            Measurements is null || Measurements.Length != MeasurementCount || Measurements.Any(x => !double.IsFinite(x) || Math.Abs(x) > 1) ||
            !double.IsFinite(BaselineScore) || BaselineScore is < 0 or > 100)
            throw new ArgumentException("Invalid bounded learning example.");
    }
    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed record TasteInput(double[] Content, double[] Context, double[] Measurements, bool HasContent, bool HasContext,
    string EncoderHash = TasteInput.ExpectedEncoderHash)
{
    public const string Schema = "foundry-taste-input-1";
    public const string ExpectedEncoderHash = "AFDB6F1A0E45B715D0BB9B11772F032C399BABD23BFC31FED1C170AFC848BDB1";
    public void Validate()
    {
        if (EncoderHash != ExpectedEncoderHash || Content is null || Context is null || Measurements is null ||
            Content.Length != 384 || Context.Length != 384 || Measurements.Length != TasteClip.MeasurementCount ||
            Content.Concat(Context).Concat(Measurements).Any(x => !double.IsFinite(x) || Math.Abs(x) > 1.001))
            throw new ArgumentException("Invalid neural model input.");
    }
}

public sealed record TasteExample(TasteClip Clip, DateTimeOffset FirstObservedUtc, DateTimeOffset UpdatedUtc,
    int? Rating, TasteSignal[] Actions, TasteInput? Input = null, string Schema = "foundry-taste-example-1")
{
    public void Validate()
    {
        if (Schema != "foundry-taste-example-1" || Clip is null || Actions is null)
            throw new ArgumentException("Invalid learning example schema.");
        Clip.Validate(); Input?.Validate();
        if (Rating is < 0 or > 2 || FirstObservedUtc.Offset != TimeSpan.Zero || UpdatedUtc.Offset != TimeSpan.Zero ||
            UpdatedUtc < FirstObservedUtc || Actions.Length > 4 || Actions.Distinct().Count() != Actions.Length ||
            Actions.Any(x => !Enum.IsDefined(x) || x < TasteSignal.Rendered))
            throw new ArgumentException("Invalid learning observation.");
    }
}

public sealed record TastePrediction(bool IsActive, double Preference, double Uncertainty, string ModelId)
{
    public static TastePrediction Inactive { get; } = new(false, 0, 1, "");
}

public sealed record TasteLearningStatus(bool Enabled, bool IsWorking, bool IsActive, int Clips, int Ratings,
    int Recordings, string Message, string? ModelId = null, int Likes = 0, int Dislikes = 0);

public interface ITasteLearningService
{
    TasteLearningStatus Status { get; }
    DateTimeOffset HistoryStartUtc => DateTimeOffset.MinValue;
    event EventHandler? Changed;
    void Observe(TasteClip clip, TasteSignal? signal);
    Task<IReadOnlyDictionary<string, TastePrediction>> PredictAsync(IReadOnlyList<TasteClip> clips, CancellationToken cancellationToken);
    Task TrainAsync(CancellationToken cancellationToken);
    void SetEnabled(bool enabled);
    Task ResetAsync(CancellationToken cancellationToken);
}
