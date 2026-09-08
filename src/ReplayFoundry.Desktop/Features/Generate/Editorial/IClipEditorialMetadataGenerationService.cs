using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

public interface IClipEditorialMetadataGenerationService
{
    bool IsAiAvailable { get; }

    string? AiUnavailableReason => null;

    Task<ClipEditorialMetadataDraft> GenerateAsync(
        ClipEditorialMetadataRequest request,
        CancellationToken cancellationToken);

    async Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(
        IReadOnlyList<ClipEditorialMetadataRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var drafts = new List<ClipEditorialMetadataDraft>(requests.Count);
        foreach (ClipEditorialMetadataRequest request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            drafts.Add(await GenerateAsync(request, cancellationToken));
        }

        return drafts.AsReadOnly();
    }
}
