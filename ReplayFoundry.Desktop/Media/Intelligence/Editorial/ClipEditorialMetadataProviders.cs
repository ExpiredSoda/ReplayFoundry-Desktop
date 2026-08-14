using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

public interface IClipEditorialMetadataGenerator
{
    ClipEditorialMetadataGeneratorIdentity Identity { get; }

    bool IsAvailable { get; }

    Task<ClipEditorialMetadataDraft> GenerateAsync(
        ClipEditorialMetadataRequest request,
        CancellationToken cancellationToken);
}

public interface IClipEditorialMetadataBatchGenerator :
    IClipEditorialMetadataGenerator
{
    Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(
        IReadOnlyList<ClipEditorialMetadataRequest> requests,
        CancellationToken cancellationToken);
}

/// <summary>
/// Marks an editorial provider whose semantic claims require a bounded,
/// verified local review video. The application layer materializes and owns
/// that transient artifact; the provider only consumes the immutable request.
/// </summary>
public interface IClipEditorialVisualMetadataGenerator :
    IClipEditorialMetadataBatchGenerator
{
}
