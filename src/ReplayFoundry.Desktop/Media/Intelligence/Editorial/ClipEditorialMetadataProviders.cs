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

public enum ClipEditorialMetadataCaseFailureCode
{
    NoDistinctPrimaryVisualEvent = 0,
}

public sealed record ClipEditorialMetadataCaseFailure(
    ClipEditorialMetadataCaseFailureCode Code);

public sealed record ClipEditorialMetadataBatchOutcome(
    ClipEditorialMetadataDraft? Draft,
    ClipEditorialMetadataCaseFailure? Failure)
{
    public bool IsAccepted => Draft is not null && Failure is null;

    public bool IsFailed => Draft is null && Failure is not null;
}

public interface IClipEditorialMetadataFailSoftBatchGenerator :
    IClipEditorialMetadataBatchGenerator
{
    Task<IReadOnlyList<ClipEditorialMetadataBatchOutcome>>
        GenerateBatchOutcomesAsync(
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
