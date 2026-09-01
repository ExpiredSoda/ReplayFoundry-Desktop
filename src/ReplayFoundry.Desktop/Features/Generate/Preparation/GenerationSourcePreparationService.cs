using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public sealed class GenerationSourcePreparationService :
    IGenerationSourcePreparationService
{
    private readonly IMediaProbe _mediaProbe;
    private readonly IGenerationSourceFileSnapshotProvider
        _snapshotProvider;

    public GenerationSourcePreparationService(
        IMediaProbe mediaProbe,
        IGenerationSourceFileSnapshotProvider snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(mediaProbe);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        _mediaProbe = mediaProbe;
        _snapshotProvider = snapshotProvider;
    }

    public async Task<GenerationSourcePreparationResult> PrepareAsync(
        GenerationSourcePreparationRequest request,
        IProgress<GenerationSourcePreparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(
            new GenerationSourcePreparationProgress(
                "Preparing your videos",
                "Replay Foundry is checking the selected sources before setup.",
                0));

        var prepared =
            new List<PreparedGenerationSource>(
                request.SourceCount);

        for (int index = 0;
             index < request.SourceCount;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SelectedVideoSource source =
                request.Sources[index];

            double startingPercent =
                index /
                (double)request.SourceCount *
                90;

            progress?.Report(
                new GenerationSourcePreparationProgress(
                    "Inspecting source",
                    $"Checking video {index + 1} of {request.SourceCount}.",
                    startingPercent,
                    source.FileName,
                    index + 1,
                    request.SourceCount));

            GenerationSourceFileSnapshot beforeProbe =
                _snapshotProvider.Capture(
                    source.FullPath);

            MediaProbeResult media;

            try
            {
                media =
                    await _mediaProbe.ProbeAsync(
                        source.FullPath,
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (MediaToolNotFoundException)
            {
                throw;
            }
            catch (MediaProbeException exception)
            {
                throw new GenerationSourcePreparationException(
                    source.FullPath,
                    $"Replay Foundry could not prepare '{source.FileName}'. " +
                    exception.Message,
                    exception.DiagnosticDetails,
                    exception);
            }

            GenerationSourceFileSnapshot afterProbe =
                _snapshotProvider.Capture(
                    source.FullPath);

            GenerationSourceFreshnessValidator.EnsureUnchanged(
                beforeProbe,
                afterProbe);

            prepared.Add(
                new PreparedGenerationSource(
                    source,
                    media,
                    afterProbe));

            double completedPercent =
                (index + 1d) /
                request.SourceCount *
                90;

            progress?.Report(
                new GenerationSourcePreparationProgress(
                    "Source ready",
                    $"Finished checking video {index + 1} of {request.SourceCount}.",
                    completedPercent,
                    source.FileName,
                    index + 1,
                    request.SourceCount));
        }

        var result =
            new GenerationSourcePreparationResult(
                request,
                prepared);

        progress?.Report(
            new GenerationSourcePreparationProgress(
                "Sources prepared",
                "Replay Foundry has the structural information needed for setup and previews.",
                100));

        return result;
    }
}
