using ReplayFoundry.Desktop.Features.Generate.Preparation;

namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

public sealed class GenerationEvidenceAnalysisCoordinator :
    IGenerationEvidenceAnalysisCoordinator,
    IDisposable
{
    private readonly IGenerationEvidenceAnalysisService
        _analysisService;

    private readonly GenerationSourceFreshnessValidator
        _freshnessValidator;

    private readonly SemaphoreSlim _analysisGate =
        new(1, 1);

    private readonly object _stateSync =
        new();

    private GenerationEvidenceAnalysisResult?
        _current;

    private GenerationEvidenceAnalysisFingerprint?
        _currentFingerprint;

    private long _invalidationVersion;

    private bool _disposed;

    public GenerationEvidenceAnalysisCoordinator(
        IGenerationEvidenceAnalysisService analysisService,
        GenerationSourceFreshnessValidator freshnessValidator,
        GenerationEvidenceAnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(analysisService);
        ArgumentNullException.ThrowIfNull(freshnessValidator);
        ArgumentNullException.ThrowIfNull(settings);

        _analysisService = analysisService;
        _freshnessValidator = freshnessValidator;
        Settings = settings;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _analysisGate.Dispose();
    }

    public GenerationEvidenceAnalysisSettings Settings { get; }

    public GenerationEvidenceAnalysisResult? Current
    {
        get
        {
            lock (_stateSync)
            {
                return _current;
            }
        }
    }

    public async Task<GenerationEvidenceAnalysisResult>
        GetOrAnalyzeAsync(
            GenerationEvidenceAnalysisRequest request,
            IProgress<GenerationEvidenceAnalysisProgress>? progress,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        await _analysisGate.WaitAsync(
            cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            GenerationEvidenceAnalysisFingerprint fingerprint =
                GenerationEvidenceAnalysisFingerprint.Create(
                    request,
                    _analysisService.AnalyzerIdentity);

            GenerationEvidenceAnalysisResult? cached;
            GenerationEvidenceAnalysisFingerprint?
                cachedFingerprint;

            lock (_stateSync)
            {
                cached = _current;
                cachedFingerprint = _currentFingerprint;
            }

            if (cached is not null &&
                fingerprint.Equals(
                    cachedFingerprint))
            {
                try
                {
                    _freshnessValidator.EnsureFresh(
                        request.Preparation);
                }
                catch
                {
                    ClearCurrent();
                    throw;
                }

                GenerationEvidenceAnalysisResult rebound =
                    Rebind(
                        request,
                        cached);

                lock (_stateSync)
                {
                    _current = rebound;
                    _currentFingerprint = fingerprint;
                }

                progress?.Report(
                    new GenerationEvidenceAnalysisProgress(
                        GenerationEvidenceAnalysisPhase
                            .UsingSavedEvidence,
                        "Using saved evidence",
                        "The confirmed layouts and analysis inputs are unchanged, so no media analysis needs to run again.",
                        sourceFileName: null,
                        sourceNumber: null,
                        sourceCount: null,
                        audioStreamIndex: null,
                        isIndeterminate: false,
                        overallPercentage: 100));

                return rebound;
            }

            long operationVersion;

            lock (_stateSync)
            {
                _current = null;
                _currentFingerprint = null;
                operationVersion =
                    _invalidationVersion;
            }

            _freshnessValidator.EnsureFresh(
                request.Preparation);

            GenerationEvidenceAnalysisResult result =
                await _analysisService.AnalyzeAsync(
                    request,
                    progress,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            _freshnessValidator.EnsureFresh(
                request.Preparation);

            lock (_stateSync)
            {
                if (_invalidationVersion !=
                    operationVersion)
                {
                    throw new InvalidOperationException(
                        "Evidence analysis was invalidated before its completed result could be retained.");
                }

                _current = result;
                _currentFingerprint = fingerprint;
            }

            return result;
        }
        finally
        {
            _analysisGate.Release();
        }
    }

    public void Invalidate()
    {
        lock (_stateSync)
        {
            _invalidationVersion++;
            _current = null;
            _currentFingerprint = null;
        }
    }

    private void ClearCurrent()
    {
        lock (_stateSync)
        {
            _current = null;
            _currentFingerprint = null;
        }
    }

    private static GenerationEvidenceAnalysisResult Rebind(
        GenerationEvidenceAnalysisRequest request,
        GenerationEvidenceAnalysisResult cached)
    {
        if (cached.Sources.Count !=
            request.SourceCount)
        {
            throw new InvalidOperationException(
                "Cached evidence cannot be rebound to a different source count.");
        }

        var reboundSources =
            new AnalyzedGenerationSource[
                request.SourceCount];

        for (int index = 0;
             index < request.SourceCount;
             index++)
        {
            AnalyzedGenerationSource payload =
                cached.Sources[index];

            reboundSources[index] =
                new AnalyzedGenerationSource(
                    request.PreparedSources[index],
                    request.SourcePlans[index],
                    payload.Evidence,
                    payload.Summary,
                    request.Settings);
        }

        return new GenerationEvidenceAnalysisResult(
            request,
            reboundSources);
    }
}
