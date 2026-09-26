using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;

public enum SpeechActivityWarningCode
{
    TrailingAudioPadded,
    NoSpeechDetected,
    MaximumSpeechDurationSplit,
    RuntimeReportedWarning,
}

public sealed record SpeechActivityWarning
{
    public SpeechActivityWarning(
        SpeechActivityWarningCode code,
        string message)
    {
        if (!Enum.IsDefined(code) || string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "Speech-activity warnings require a defined code and message.");
        }

        Code = code;
        Message = message.Trim();
    }

    public SpeechActivityWarningCode Code { get; }

    public string Message { get; }
}

public sealed class SpeechActivityOptions
{
    public SpeechActivityOptions(
        double speechThreshold,
        double silenceThreshold,
        TimeSpan minimumSpeechDuration,
        TimeSpan minimumSilenceDuration,
        TimeSpan speechPadding,
        TimeSpan maximumSpeechDuration,
        TimeSpan processTimeout)
    {
        if (!double.IsFinite(speechThreshold) ||
            !double.IsFinite(silenceThreshold) ||
            speechThreshold is <= 0 or > 1 ||
            silenceThreshold is < 0 or >= 1 ||
            silenceThreshold >= speechThreshold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speechThreshold),
                "Speech thresholds must be finite, bounded, and hysteretic.");
        }

        if (minimumSpeechDuration <= TimeSpan.Zero ||
            minimumSilenceDuration <= TimeSpan.Zero ||
            speechPadding < TimeSpan.Zero ||
            maximumSpeechDuration <= minimumSpeechDuration ||
            processTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSpeechDuration),
                "Speech timing options must be positive and internally consistent.");
        }

        SpeechThreshold = speechThreshold;
        SilenceThreshold = silenceThreshold;
        MinimumSpeechDuration = minimumSpeechDuration;
        MinimumSilenceDuration = minimumSilenceDuration;
        SpeechPadding = speechPadding;
        MaximumSpeechDuration = maximumSpeechDuration;
        ProcessTimeout = processTimeout;
    }

    public double SpeechThreshold { get; }

    public double SilenceThreshold { get; }

    public TimeSpan MinimumSpeechDuration { get; }

    public TimeSpan MinimumSilenceDuration { get; }

    public TimeSpan SpeechPadding { get; }

    public TimeSpan MaximumSpeechDuration { get; }

    public TimeSpan ProcessTimeout { get; }

    public IReadOnlyDictionary<string, string> ToNormalizedValues() =>
        new ReadOnlyDictionary<string, string>(
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["maximumSpeechMilliseconds"] = MaximumSpeechDuration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                ["minimumSilenceMilliseconds"] = MinimumSilenceDuration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                ["minimumSpeechMilliseconds"] = MinimumSpeechDuration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                ["processTimeoutMilliseconds"] = ProcessTimeout.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                ["silenceThreshold"] = SilenceThreshold.ToString("0.########", CultureInfo.InvariantCulture),
                ["speechPaddingMilliseconds"] = SpeechPadding.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                ["speechThreshold"] = SpeechThreshold.ToString("0.########", CultureInfo.InvariantCulture),
            });

    public static SpeechActivityOptions CreateBalancedDefaults() =>
        new(
            speechThreshold: 0.50,
            silenceThreshold: 0.35,
            minimumSpeechDuration: TimeSpan.FromMilliseconds(250),
            minimumSilenceDuration: TimeSpan.FromMilliseconds(100),
            speechPadding: TimeSpan.FromMilliseconds(30),
            maximumSpeechDuration: TimeSpan.FromSeconds(30),
            processTimeout: TimeSpan.FromMinutes(30));
}

public sealed class SpeechActivityRequest
{
    public SpeechActivityRequest(
        string requestId,
        string audioPath,
        TimeSpan inputDuration,
        TimeSpan absoluteSourceOffset,
        TimeSpan sourceDuration,
        int absoluteAudioStreamIndex,
        AudioContentRoleAssignment role,
        SpeechActivityOptions options,
        ModelArtifactManifest model)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException(
                "Speech-activity analysis requires a stable request identity.",
                nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(audioPath) ||
            !Path.IsPathFullyQualified(audioPath))
        {
            throw new ArgumentException(
                "Speech-activity audio paths must be fully qualified.",
                nameof(audioPath));
        }

        if (inputDuration <= TimeSpan.Zero ||
            absoluteSourceOffset < TimeSpan.Zero ||
            sourceDuration <= TimeSpan.Zero ||
            absoluteSourceOffset + inputDuration > sourceDuration ||
            absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputDuration),
                "Speech-activity coverage and stream identity must remain inside the source.");
        }

        RequestId = requestId.Trim();
        AudioPath = Path.GetFullPath(audioPath);
        InputDuration = inputDuration;
        AbsoluteSourceOffset = absoluteSourceOffset;
        SourceDuration = sourceDuration;
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        Role = role ?? throw new ArgumentNullException(nameof(role));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public string RequestId { get; }

    public string AudioPath { get; }

    public TimeSpan InputDuration { get; }

    public TimeSpan AbsoluteSourceOffset { get; }

    public TimeSpan SourceDuration { get; }

    public int AbsoluteAudioStreamIndex { get; }

    public AudioContentRoleAssignment Role { get; }

    public SpeechActivityOptions Options { get; }

    public ModelArtifactManifest Model { get; }
}

public sealed record SpeechActivityInterval
{
    public SpeechActivityInterval(
        TimeSpan relativeStart,
        TimeSpan relativeEnd,
        TimeSpan absoluteStart,
        TimeSpan absoluteEnd,
        double peakProbability,
        double meanProbability)
    {
        if (relativeStart < TimeSpan.Zero ||
            relativeEnd <= relativeStart ||
            absoluteStart < TimeSpan.Zero ||
            absoluteEnd <= absoluteStart ||
            absoluteEnd - absoluteStart != relativeEnd - relativeStart ||
            !double.IsFinite(peakProbability) ||
            !double.IsFinite(meanProbability) ||
            peakProbability is < 0 or > 1 ||
            meanProbability is < 0 or > 1 ||
            meanProbability > peakProbability)
        {
            throw new ArgumentException(
                "Speech intervals require bounded matching relative and absolute coverage.");
        }

        RelativeStart = relativeStart;
        RelativeEnd = relativeEnd;
        AbsoluteStart = absoluteStart;
        AbsoluteEnd = absoluteEnd;
        PeakProbability = peakProbability;
        MeanProbability = meanProbability;
    }

    public TimeSpan RelativeStart { get; }

    public TimeSpan RelativeEnd { get; }

    public TimeSpan AbsoluteStart { get; }

    public TimeSpan AbsoluteEnd { get; }

    public TimeSpan Duration => RelativeEnd - RelativeStart;

    public double PeakProbability { get; }

    public double MeanProbability { get; }
}

public sealed class SpeechActivityExecutionManifest
{
    private readonly ReadOnlyDictionary<string, string> _options;
    private readonly ReadOnlyCollection<SpeechActivityWarning> _warnings;

    public SpeechActivityExecutionManifest(
        InferenceProviderIdentity provider,
        ModelArtifactManifest model,
        string runtimeName,
        string runtimeVersion,
        string executionBackend,
        IReadOnlyDictionary<string, string> options,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        TimeSpan elapsed,
        IEnumerable<SpeechActivityWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(runtimeName) ||
            string.IsNullOrWhiteSpace(runtimeVersion) ||
            string.IsNullOrWhiteSpace(executionBackend) ||
            startedAtUtc.Offset != TimeSpan.Zero ||
            completedAtUtc.Offset != TimeSpan.Zero ||
            completedAtUtc < startedAtUtc ||
            elapsed < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Speech-activity execution provenance is incomplete.");
        }

        SpeechActivityWarning[] warningSnapshot =
            warnings?.ToArray() ?? [];
        if (warningSnapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Speech-activity warnings cannot contain null entries.",
                nameof(warnings));
        }

        Provider = provider;
        Model = model;
        RuntimeName = runtimeName.Trim();
        RuntimeVersion = runtimeVersion.Trim();
        ExecutionBackend = executionBackend.Trim();
        _options = new ReadOnlyDictionary<string, string>(
            options
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    static pair => string.IsNullOrWhiteSpace(pair.Key)
                        ? throw new ArgumentException("Speech-activity option names cannot be blank.", nameof(options))
                        : pair.Key.Trim(),
                    static pair => pair.Value ?? throw new ArgumentException("Speech-activity option values cannot be null.", nameof(options)),
                    StringComparer.Ordinal));
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        Elapsed = elapsed;
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public InferenceProviderIdentity Provider { get; }

    public ModelArtifactManifest Model { get; }

    public string RuntimeName { get; }

    public string RuntimeVersion { get; }

    public string ExecutionBackend { get; }

    public IReadOnlyDictionary<string, string> Options => _options;

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public TimeSpan Elapsed { get; }

    public IReadOnlyList<SpeechActivityWarning> Warnings => _warnings;
}

public sealed class SpeechActivityResult
{
    private readonly ReadOnlyCollection<SpeechActivityInterval> _intervals;

    public SpeechActivityResult(
        SpeechActivityRequest request,
        IEnumerable<SpeechActivityInterval> intervals,
        SpeechActivityExecutionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(manifest);

        SpeechActivityInterval[] snapshot = intervals
            .OrderBy(static item => item.RelativeStart)
            .ThenBy(static item => item.RelativeEnd)
            .ToArray();
        if (snapshot.Any(static item => item is null) ||
            snapshot.Where((item, index) =>
                    item.RelativeEnd > request.InputDuration ||
                    item.AbsoluteStart != request.AbsoluteSourceOffset + item.RelativeStart ||
                    item.AbsoluteEnd != request.AbsoluteSourceOffset + item.RelativeEnd ||
                    index > 0 && snapshot[index - 1].RelativeEnd > item.RelativeStart)
                .Any() ||
            !ReferenceEquals(manifest.Model, request.Model))
        {
            throw new ArgumentException(
                "Speech activity must be ordered, non-overlapping, bounded, and bound to the request model.",
                nameof(intervals));
        }

        Request = request;
        _intervals = Array.AsReadOnly(snapshot);
        Manifest = manifest;
    }

    public SpeechActivityRequest Request { get; }

    public IReadOnlyList<SpeechActivityInterval> Intervals => _intervals;

    public SpeechActivityExecutionManifest Manifest { get; }

    public TimeSpan TotalSpeechDuration =>
        TimeSpan.FromTicks(_intervals.Sum(static item => item.Duration.Ticks));

    public double SpeechOccupancy =>
        TotalSpeechDuration.TotalSeconds / Request.InputDuration.TotalSeconds;
}

public interface ISpeechActivityProvider
{
    InferenceProviderIdentity Identity { get; }

    Task<SpeechActivityResult> AnalyzeAsync(
        SpeechActivityRequest request,
        CancellationToken cancellationToken);
}

public sealed class SpeechActivityProviderException : Exception
{
    public SpeechActivityProviderException(
        string message,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "Speech-activity failures require a message.",
                nameof(message));
        }

        DiagnosticDetails = string.IsNullOrWhiteSpace(diagnosticDetails)
            ? null
            : diagnosticDetails.Trim();
    }

    public string? DiagnosticDetails { get; }
}
