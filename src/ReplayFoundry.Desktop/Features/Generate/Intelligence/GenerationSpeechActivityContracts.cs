using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public enum GenerationSpeechActivityPhase
{
    PreparingAudio,
    DetectingSpeech,
    SourceComplete,
    BatchComplete,
    UsingSavedSpeechActivity,
}

public sealed record GenerationSpeechActivityProgress
{
    public GenerationSpeechActivityProgress(
        GenerationSpeechActivityPhase phase,
        string title,
        string detail,
        string? sourceName,
        int? sourceNumber,
        int? sourceCount,
        int? absoluteAudioStreamIndex,
        bool isIndeterminate,
        double? overallPercentage)
    {
        if (!Enum.IsDefined(phase) ||
            string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(detail) ||
            overallPercentage is < 0 or > 100 ||
            isIndeterminate && overallPercentage is not null)
        {
            throw new ArgumentException(
                "Speech-activity progress must use a defined truthful phase and boundary.");
        }

        bool hasSource = sourceNumber is not null || sourceCount is not null;
        if (hasSource &&
            (sourceNumber is null || sourceCount is null ||
             sourceNumber <= 0 || sourceCount <= 0 || sourceNumber > sourceCount ||
             string.IsNullOrWhiteSpace(sourceName)) ||
            !hasSource && (!string.IsNullOrWhiteSpace(sourceName) || absoluteAudioStreamIndex is not null) ||
            absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentException(
                "Speech-activity source progress is incomplete or invalid.");
        }

        Phase = phase;
        Title = title.Trim();
        Detail = detail.Trim();
        SourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName.Trim();
        SourceNumber = sourceNumber;
        SourceCount = sourceCount;
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        IsIndeterminate = isIndeterminate;
        OverallPercentage = overallPercentage;
    }

    public GenerationSpeechActivityPhase Phase { get; }
    public string Title { get; }
    public string Detail { get; }
    public string? SourceName { get; }
    public int? SourceNumber { get; }
    public int? SourceCount { get; }
    public int? AbsoluteAudioStreamIndex { get; }
    public bool IsIndeterminate { get; }
    public double? OverallPercentage { get; }
}

public sealed class GenerationSpeechActivitySettings
{
    public GenerationSpeechActivitySettings(
        SpeechActivityOptions options,
        ModelArtifactManifest model,
        string policyVersion = "1.0",
        TimeSpan? maximumAudioChunkDuration = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Model = model ?? throw new ArgumentNullException(nameof(model));
        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException(
                "Speech-activity integration requires a policy version.",
                nameof(policyVersion));
        }

        PolicyVersion = policyVersion.Trim();
        MaximumAudioChunkDuration = maximumAudioChunkDuration ??
            TimeSpan.FromMinutes(10);
        if (MaximumAudioChunkDuration < TimeSpan.FromMinutes(1) ||
            MaximumAudioChunkDuration > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAudioChunkDuration),
                "Speech-activity audio chunks must remain between one and thirty minutes.");
        }
    }

    public SpeechActivityOptions Options { get; }
    public ModelArtifactManifest Model { get; }
    public string PolicyVersion { get; }
    public TimeSpan MaximumAudioChunkDuration { get; }
}

public sealed class GenerationSpeechStreamResult
{
    private readonly ReadOnlyCollection<SpeechActivityInterval> _intervals;
    private readonly ReadOnlyCollection<SpeechActivityExecutionManifest>
        _executionManifests;

    public GenerationSpeechStreamResult(
        AnalyzedGenerationSource source,
        int absoluteAudioStreamIndex,
        AudioContentRoleAssignment role,
        IEnumerable<SpeechActivityInterval> intervals,
        IEnumerable<SpeechActivityExecutionManifest> executionManifests)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(executionManifests);
        SpeechActivityInterval[] snapshot = intervals.ToArray();
        SpeechActivityExecutionManifest[] manifestSnapshot =
            executionManifests.ToArray();
        if (absoluteAudioStreamIndex < 0 ||
            !source.PreparedSource.Media.AudioStreams.Any(
                stream => stream.Index == absoluteAudioStreamIndex) ||
            snapshot.Any(static item => item is null) ||
            manifestSnapshot.Length == 0 ||
            manifestSnapshot.Any(static item => item is null) ||
            snapshot.Where((item, index) =>
                    item.AbsoluteEnd > source.PreparedSource.Media.Duration ||
                    index > 0 && snapshot[index - 1].AbsoluteEnd > item.AbsoluteStart)
                .Any())
        {
            throw new ArgumentException(
                "Generation speech activity must remain ordered and bound to an inspected source stream.");
        }

        Source = source;
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        Role = role;
        _intervals = Array.AsReadOnly(snapshot);
        _executionManifests = Array.AsReadOnly(manifestSnapshot);
    }

    public AnalyzedGenerationSource Source { get; }
    public int AbsoluteAudioStreamIndex { get; }
    public AudioContentRoleAssignment Role { get; }
    public IReadOnlyList<SpeechActivityInterval> Intervals => _intervals;
    public IReadOnlyList<SpeechActivityExecutionManifest>
        ExecutionManifests => _executionManifests;
    public TimeSpan TotalSpeechDuration =>
        TimeSpan.FromTicks(_intervals.Sum(static interval => interval.Duration.Ticks));
}

public sealed class GenerationSourceSpeechActivity
{
    private readonly ReadOnlyCollection<GenerationSpeechStreamResult> _streams;

    public GenerationSourceSpeechActivity(
        AnalyzedGenerationSource source,
        IEnumerable<GenerationSpeechStreamResult> streams)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(streams);
        GenerationSpeechStreamResult[] snapshot = streams
            .OrderBy(static stream => stream.AbsoluteAudioStreamIndex)
            .ToArray();
        if (snapshot.Any(stream => !ReferenceEquals(stream.Source, source)) ||
            snapshot.Select(static stream => stream.AbsoluteAudioStreamIndex).Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Source speech activity requires unique streams from one analyzed source.",
                nameof(streams));
        }

        Source = source;
        _streams = Array.AsReadOnly(snapshot);
    }

    public AnalyzedGenerationSource Source { get; }
    public IReadOnlyList<GenerationSpeechStreamResult> Streams => _streams;
}

public sealed class GenerationSpeechActivityResult
{
    private readonly ReadOnlyCollection<GenerationSourceSpeechActivity> _sources;

    public GenerationSpeechActivityResult(
        GenerationRequest request,
        GenerationSpeechActivitySettings settings,
        InferenceProviderIdentity providerIdentity,
        IEnumerable<GenerationSourceSpeechActivity> sources,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(providerIdentity);
        ArgumentNullException.ThrowIfNull(sources);
        GenerationSourceSpeechActivity[] snapshot = sources.ToArray();
        if (elapsed < TimeSpan.Zero ||
            snapshot.Length != request.AnalyzedSources.Count ||
            snapshot.Where((source, index) =>
                    !ReferenceEquals(source.Source, request.AnalyzedSources[index]))
                .Any())
        {
            throw new ArgumentException(
                "Speech-activity batches must preserve every analyzed source in preparation order.",
                nameof(sources));
        }

        Request = request;
        Settings = settings;
        ProviderIdentity = providerIdentity;
        _sources = Array.AsReadOnly(snapshot);
        Elapsed = elapsed;
    }

    public GenerationRequest Request { get; }
    public GenerationSpeechActivitySettings Settings { get; }
    public InferenceProviderIdentity ProviderIdentity { get; }
    public IReadOnlyList<GenerationSourceSpeechActivity> Sources => _sources;
    public TimeSpan Elapsed { get; }

    public GenerationSourceSpeechActivity FindSource(string sourceFullPath) =>
        _sources.Single(
            source => source.Source.PreparedSource.Media.FullPath.Equals(
                sourceFullPath,
                StringComparison.OrdinalIgnoreCase));
}

public interface IGenerationSpeechActivityService
{
    Task<GenerationSpeechActivityResult> AnalyzeAsync(
        GenerationRequest request,
        IProgress<GenerationSpeechActivityProgress> progress,
        CancellationToken cancellationToken);
}

public sealed class GenerationSpeechActivityException : Exception
{
    public GenerationSpeechActivityException(
        string message,
        string sourceFullPath,
        int absoluteAudioStreamIndex,
        string? diagnosticDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(message) ||
            string.IsNullOrWhiteSpace(sourceFullPath) ||
            !Path.IsPathFullyQualified(sourceFullPath) ||
            absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentException(
                "Speech-activity failures require source and stream context.");
        }

        SourceFullPath = Path.GetFullPath(sourceFullPath);
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        DiagnosticDetails = string.IsNullOrWhiteSpace(diagnosticDetails)
            ? null
            : diagnosticDetails.Trim();
    }

    public string SourceFullPath { get; }
    public int AbsoluteAudioStreamIndex { get; }
    public string? DiagnosticDetails { get; }
}
