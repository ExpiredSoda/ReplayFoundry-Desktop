using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public interface IVisualSemanticEditorialProvider
{
    InferenceProviderIdentity Identity { get; }

    Task<VisualSemanticEditorialBatchResult> ObserveAsync(
        VisualSemanticBatchRequest request,
        CancellationToken cancellationToken);
}

public sealed record VisualSemanticEditorialResult
{
    public VisualSemanticEditorialResult(
        VisualSemanticRequest request,
        VisualSemanticEditorialObservation observation,
        VisualSemanticEditorialCanonicalizationAudit canonicalizationAudit,
        TimeSpan elapsed,
        double? neuralEditorialValue = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(canonicalizationAudit);
        if (elapsed < TimeSpan.Zero || neuralEditorialValue.HasValue && (!double.IsFinite(neuralEditorialValue.Value) || neuralEditorialValue is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        Request = request;
        Observation = observation;
        CanonicalizationAudit = canonicalizationAudit;
        Elapsed = elapsed;
        NeuralEditorialValue = neuralEditorialValue;
    }

    public VisualSemanticRequest Request { get; }

    public VisualSemanticEditorialObservation Observation { get; }

    public VisualSemanticEditorialCanonicalizationAudit CanonicalizationAudit { get; }

    public TimeSpan Elapsed { get; }
    public double? NeuralEditorialValue { get; }
}

public sealed record VisualSemanticEditorialFailure(
    VisualSemanticRequest Request,
    string Stage,
    string ErrorCode,
    TimeSpan Elapsed,
    bool CanRetry = true);

public sealed class VisualSemanticEditorialBatchResult
{
    private readonly ReadOnlyCollection<VisualSemanticEditorialResult> _results;

    public VisualSemanticEditorialBatchResult(
        VisualSemanticBatchRequest request,
        IEnumerable<VisualSemanticEditorialResult> results,
        TimeSpan elapsed,
        long? peakAllocatedGpuBytes,
        IEnumerable<VisualSemanticEditorialFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(results);
        VisualSemanticEditorialResult[] snapshot = results.ToArray();
        VisualSemanticEditorialFailure[] failed = (failures ?? []).ToArray();
        if (snapshot.Any(value => value is null) || failed.Any(value => value is null))
            throw new ArgumentException("Editorial results and failures cannot contain null entries.", nameof(results));
        VisualSemanticRequest[] completed = snapshot.Select(value => value.Request)
            .Concat(failed.Select(value => value.Request)).ToArray();
        if (elapsed < TimeSpan.Zero ||
            peakAllocatedGpuBytes < 0 ||
            completed.Length != request.Requests.Count ||
            completed.Distinct(ReferenceEqualityComparer.Instance).Count() != completed.Length ||
            completed.Any(value => !request.Requests.Any(expected => ReferenceEquals(expected, value))) ||
            failed.Any(value => value.Elapsed < TimeSpan.Zero || string.IsNullOrWhiteSpace(value.Stage) ||
                string.IsNullOrWhiteSpace(value.ErrorCode)) ||
            !snapshot.Select(value => value.Request).SequenceEqual(request.Requests.Where(value =>
                snapshot.Any(result => ReferenceEquals(result.Request, value)))))
        {
            throw new ArgumentException(
                "A qualified editorial batch must account for every request exactly once and preserve successful request order.",
                nameof(results));
        }

        Request = request;
        _results = Array.AsReadOnly(snapshot);
        Failures = Array.AsReadOnly(failed);
        Elapsed = elapsed;
        PeakAllocatedGpuBytes = peakAllocatedGpuBytes;
    }

    public VisualSemanticBatchRequest Request { get; }

    public IReadOnlyList<VisualSemanticEditorialResult> Results => _results;
    public IReadOnlyList<VisualSemanticEditorialFailure> Failures { get; }

    public TimeSpan Elapsed { get; }

    public long? PeakAllocatedGpuBytes { get; }
}
