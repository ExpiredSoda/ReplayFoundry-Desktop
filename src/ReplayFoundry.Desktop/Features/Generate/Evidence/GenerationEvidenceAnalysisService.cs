using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Summaries;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

public sealed class GenerationEvidenceAnalysisService :
    IGenerationEvidenceAnalysisService
{
    private readonly IMediaEvidenceAnalyzer _analyzer;
    private readonly GenerationSourceFreshnessValidator _freshnessValidator;

    public GenerationEvidenceAnalysisService(
        IMediaEvidenceAnalyzer analyzer,
        GenerationSourceFreshnessValidator freshnessValidator)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _freshnessValidator = freshnessValidator ??
            throw new ArgumentNullException(nameof(freshnessValidator));
    }

    public MediaEvidenceAnalyzerIdentity AnalyzerIdentity => _analyzer.Identity;

    public async Task<GenerationEvidenceAnalysisResult> AnalyzeAsync(
        GenerationEvidenceAnalysisRequest request,
        IProgress<GenerationEvidenceAnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _freshnessValidator.EnsureFresh(request.Preparation);

        var analyzedSources =
            new List<AnalyzedGenerationSource>(request.SourceCount);
        for (int index = 0; index < request.SourceCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreparedGenerationSource preparedSource =
                request.PreparedSources[index];
            var sourcePlan = request.SourcePlans[index];
            int sourceNumber = index + 1;

            progress?.Report(GenerationEvidenceProgressTranslator.Create(
                GenerationEvidenceAnalysisPhase.PreparingAnalysis,
                "Getting the video ready",
                "Preparing this video so Replay Foundry can find its strongest moments.",
                preparedSource,
                sourceNumber,
                request.SourceCount,
                isIndeterminate: true));

            var mediaRequest = MediaEvidenceAnalysisRequest.CreateCompositionAware(
                preparedSource.Media,
                sourcePlan.Plan,
                request.Settings.Options,
                request.Settings.IncludedRegionRoles);
            var lowLevelProgress =
                new SynchronousProgress<MediaEvidenceProgressUpdate>(
                    update => GenerationEvidenceProgressTranslator.Translate(
                        progress,
                        update,
                        preparedSource,
                        sourceNumber,
                        request.SourceCount));

            MediaEvidenceResult evidence;
            try
            {
                evidence = await _analyzer.AnalyzeAsync(
                    mediaRequest,
                    lowLevelProgress,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (MediaToolNotFoundException exception)
            {
                throw new GenerationEvidenceToolUnavailableException(
                    preparedSource.Source.FullPath,
                    sourceNumber,
                    request.SourceCount,
                    BuildFriendlyFailure(
                        preparedSource,
                        sourceNumber,
                        request.SourceCount,
                        "Replay Foundry cannot read this video because a required video tool is unavailable. Repair or reinstall Replay Foundry, then try again."),
                    exception);
            }
            catch (MediaEvidenceAnalysisException exception)
            {
                throw new GenerationEvidenceAnalysisException(
                    preparedSource.Source.FullPath,
                    sourceNumber,
                    request.SourceCount,
                    BuildFriendlyFailure(
                        preparedSource,
                        sourceNumber,
                        request.SourceCount,
                        exception.Message),
                    exception.DiagnosticDetails,
                    exception);
            }
            catch (Exception exception)
            {
                throw new GenerationEvidenceAnalysisException(
                    preparedSource.Source.FullPath,
                    sourceNumber,
                    request.SourceCount,
                    BuildFriendlyFailure(
                        preparedSource,
                        sourceNumber,
                        request.SourceCount,
                        "Replay Foundry could not finish checking this video."),
                    innerException: exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureAnalyzerIdentity(
                evidence,
                preparedSource,
                sourceNumber,
                request.SourceCount);

            int totalPasses = 2 + preparedSource.Media.AudioStreams.Count;
            progress?.Report(GenerationEvidenceProgressTranslator.CreateBoundary(
                GenerationEvidenceAnalysisPhase.FinishingSourceEvidence,
                "Finishing this video",
                "Putting together what Replay Foundry found in the picture and sound.",
                preparedSource,
                sourceNumber,
                request.SourceCount,
                totalPasses,
                totalPasses));

            MediaEvidenceSummary summary = MediaEvidenceSummaryBuilder.Build(
                preparedSource.Media,
                evidence,
                request.Settings.SummaryOptions);
            analyzedSources.Add(new AnalyzedGenerationSource(
                preparedSource,
                sourcePlan,
                evidence,
                summary,
                request.Settings));

            _freshnessValidator.EnsureFresh(request.Preparation);
            progress?.Report(GenerationEvidenceProgressTranslator.Create(
                GenerationEvidenceAnalysisPhase.SourceEvidenceComplete,
                "Video ready",
                "Replay Foundry has finished checking this video.",
                preparedSource,
                sourceNumber,
                request.SourceCount,
                isIndeterminate: false,
                overallPercentage:
                    sourceNumber / (double)request.SourceCount * 100));
        }

        cancellationToken.ThrowIfCancellationRequested();
        _freshnessValidator.EnsureFresh(request.Preparation);
        var result = new GenerationEvidenceAnalysisResult(
            request,
            analyzedSources);
        progress?.Report(new GenerationEvidenceAnalysisProgress(
            GenerationEvidenceAnalysisPhase.BatchComplete,
            "Videos ready",
            "Replay Foundry has finished checking every selected video.",
            sourceFileName: null,
            sourceNumber: null,
            sourceCount: null,
            audioStreamIndex: null,
            isIndeterminate: false,
            overallPercentage: 100));
        return result;
    }

    private void EnsureAnalyzerIdentity(
        MediaEvidenceResult evidence,
        PreparedGenerationSource source,
        int sourceNumber,
        int sourceCount)
    {
        if (string.Equals(
                evidence.Manifest.AnalyzerName,
                AnalyzerIdentity.Name,
                StringComparison.Ordinal) &&
            string.Equals(
                evidence.Manifest.AnalyzerVersion,
                AnalyzerIdentity.Version,
                StringComparison.Ordinal))
        {
            return;
        }

        throw new GenerationEvidenceAnalysisException(
            source.Source.FullPath,
            sourceNumber,
            sourceCount,
            BuildFriendlyFailure(
                source,
                sourceNumber,
                sourceCount,
                "The analyzer returned evidence from an unexpected implementation identity."));
    }

    private static string BuildFriendlyFailure(
        PreparedGenerationSource source,
        int sourceNumber,
        int sourceCount,
        string reason) =>
        $"Evidence analysis stopped for video {sourceNumber} of " +
        $"{sourceCount}, '{source.Source.FileName}'. {reason}";
}
