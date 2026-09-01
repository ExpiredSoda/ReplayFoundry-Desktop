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
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(canonicalizationAudit);
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        Request = request;
        Observation = observation;
        CanonicalizationAudit = canonicalizationAudit;
        Elapsed = elapsed;
    }

    public VisualSemanticRequest Request { get; }

    public VisualSemanticEditorialObservation Observation { get; }

    public VisualSemanticEditorialCanonicalizationAudit CanonicalizationAudit { get; }

    public TimeSpan Elapsed { get; }
}

public sealed class VisualSemanticEditorialBatchResult
{
    private readonly ReadOnlyCollection<VisualSemanticEditorialResult> _results;

    public VisualSemanticEditorialBatchResult(
        VisualSemanticBatchRequest request,
        IEnumerable<VisualSemanticEditorialResult> results,
        TimeSpan elapsed,
        long? peakAllocatedGpuBytes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(results);
        VisualSemanticEditorialResult[] snapshot = results.ToArray();
        if (elapsed < TimeSpan.Zero ||
            peakAllocatedGpuBytes < 0 ||
            snapshot.Length != request.Requests.Count ||
            snapshot.Where((value, index) =>
                    value is null ||
                    !ReferenceEquals(value.Request, request.Requests[index]))
                .Any())
        {
            throw new ArgumentException(
                "A qualified editorial batch must preserve every request in order.",
                nameof(results));
        }

        Request = request;
        _results = Array.AsReadOnly(snapshot);
        Elapsed = elapsed;
        PeakAllocatedGpuBytes = peakAllocatedGpuBytes;
    }

    public VisualSemanticBatchRequest Request { get; }

    public IReadOnlyList<VisualSemanticEditorialResult> Results => _results;

    public TimeSpan Elapsed { get; }

    public long? PeakAllocatedGpuBytes { get; }
}
