using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed class VisualSemanticReviewVideoMaterializationRequest
{
    public VisualSemanticReviewVideoMaterializationRequest(
        string candidateId,
        MediaProbeResult media,
        TimeSpan sourceStart,
        TimeSpan sourceEnd,
        NormalizedRectangle? contentRegion = null)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (string.IsNullOrWhiteSpace(candidateId) ||
            sourceStart < TimeSpan.Zero ||
            sourceEnd <= sourceStart ||
            sourceEnd > media.Duration ||
            sourceEnd - sourceStart >
                VisualSemanticVideoInputPolicy.CreateV05A1()
                    .MaximumReviewDuration)
        {
            throw new ArgumentException(
                "A visual-semantic review request requires a stable candidate and a bounded source interval.");
        }

        CandidateId = candidateId.Trim();
        Media = media;
        SourceStart = sourceStart;
        SourceEnd = sourceEnd;
        ContentRegion = contentRegion;
    }

    public string CandidateId { get; }

    public MediaProbeResult Media { get; }

    public TimeSpan SourceStart { get; }

    public TimeSpan SourceEnd { get; }

    public NormalizedRectangle? ContentRegion { get; }

    public TimeSpan Duration => SourceEnd - SourceStart;
}

public sealed class MaterializedVisualSemanticReviewVideo : IDisposable
{
    private Action? _cleanup;

    internal MaterializedVisualSemanticReviewVideo(
        VisualSemanticReviewVideoMaterializationRequest request,
        VisualSemanticInputManifest input,
        Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(cleanup);
        if (input.ReviewVideoDuration != request.Duration)
        {
            throw new ArgumentException(
                "A materialized review video must preserve its requested duration.",
                nameof(input));
        }

        Request = request;
        Input = input;
        _cleanup = cleanup;
    }

    public VisualSemanticReviewVideoMaterializationRequest Request { get; }

    public VisualSemanticInputManifest Input { get; }

    public void Dispose() => Interlocked.Exchange(ref _cleanup, null)?.Invoke();
}

public interface IVisualSemanticReviewVideoMaterializer
{
    Task<MaterializedVisualSemanticReviewVideo> MaterializeAsync(
        VisualSemanticReviewVideoMaterializationRequest request,
        CancellationToken cancellationToken);
}
