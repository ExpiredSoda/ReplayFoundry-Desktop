namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed class VisualSemanticVideoInputPolicy
{
    public const string CurrentSchemaVersion =
        "visual-semantic-video-policy-1.1";
    public const string CurrentVideoBackend = "torchcodec";
    public const string CurrentSamplingPolicyVersion =
        "uniform-fps-bounded-1.0";
    public const string CurrentTrimPolicyVersion =
        "virtual-source-offset-1.0";

    private VisualSemanticVideoInputPolicy(
        string schemaVersion,
        TimeSpan maximumReviewDuration,
        int maximumWidth,
        int maximumHeight,
        int maximumPixelsPerFrame,
        int minimumFrames,
        int maximumFrames,
        double framesPerSecond,
        long maximumTotalPixels,
        bool audioSupplied,
        string videoBackend,
        string samplingPolicyVersion,
        string trimPolicyVersion)
    {
        if (maximumReviewDuration <= TimeSpan.Zero ||
            maximumWidth <= 0 ||
            maximumHeight <= 0 ||
            maximumPixelsPerFrame <= 0 ||
            maximumPixelsPerFrame >
                checked(maximumWidth * maximumHeight) ||
            minimumFrames <= 0 ||
            maximumFrames < minimumFrames ||
            !double.IsFinite(framesPerSecond) ||
            framesPerSecond <= 0 ||
            maximumTotalPixels <= 0 ||
            maximumTotalPixels <
                checked((long)minimumFrames * maximumPixelsPerFrame) ||
            audioSupplied)
        {
            throw new ArgumentException(
                "The visual-semantic video policy must be finite, bounded, internally consistent, and video-only.");
        }

        SchemaVersion = VisualSemanticContractText.Required(
            schemaVersion,
            nameof(schemaVersion),
            64);
        MaximumReviewDuration = maximumReviewDuration;
        MaximumWidth = maximumWidth;
        MaximumHeight = maximumHeight;
        MaximumPixelsPerFrame = maximumPixelsPerFrame;
        MinimumFrames = minimumFrames;
        MaximumFrames = maximumFrames;
        FramesPerSecond = framesPerSecond;
        MaximumTotalPixels = maximumTotalPixels;
        AudioSupplied = audioSupplied;
        VideoBackend = VisualSemanticContractText.Required(
            videoBackend,
            nameof(videoBackend),
            64);
        SamplingPolicyVersion = VisualSemanticContractText.Required(
            samplingPolicyVersion,
            nameof(samplingPolicyVersion),
            64);
        TrimPolicyVersion = VisualSemanticContractText.Required(
            trimPolicyVersion,
            nameof(trimPolicyVersion),
            64);
    }

    public string SchemaVersion { get; }

    public TimeSpan MaximumReviewDuration { get; }

    public int MaximumWidth { get; }

    public int MaximumHeight { get; }

    public int MaximumPixelsPerFrame { get; }

    public int MinimumFrames { get; }

    public int MaximumFrames { get; }

    public double FramesPerSecond { get; }

    public long MaximumTotalPixels { get; }

    public bool AudioSupplied { get; }

    public string VideoBackend { get; }

    public string SamplingPolicyVersion { get; }

    public string TrimPolicyVersion { get; }

    public static VisualSemanticVideoInputPolicy CreateV05A1() =>
        new(
            CurrentSchemaVersion,
            TimeSpan.FromSeconds(70),
            maximumWidth: 640,
            maximumHeight: 640,
            maximumPixelsPerFrame: 131_072,
            minimumFrames: 4,
            maximumFrames: 32,
            framesPerSecond: 0.5,
            maximumTotalPixels: 4_194_304,
            audioSupplied: false,
            CurrentVideoBackend,
            CurrentSamplingPolicyVersion,
            CurrentTrimPolicyVersion);
}
