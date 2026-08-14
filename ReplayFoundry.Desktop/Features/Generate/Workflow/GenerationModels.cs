using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

public sealed class GenerationRequest
{
    public GenerationRequest(
        GenerationSourcePreparationResult preparation,
        GenerationSetupOptions setupOptions,
        GenerationCompositionReviewResult compositionReview,
        GenerationEvidenceAnalysisResult evidenceAnalysis)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(setupOptions);
        ArgumentNullException.ThrowIfNull(compositionReview);
        ArgumentNullException.ThrowIfNull(evidenceAnalysis);

        if (setupOptions.Mode is not (
            GenerationMode.IndividualClips or
            GenerationMode.Montage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(setupOptions),
                setupOptions.Mode,
                "The generation mode is not supported.");
        }

        if (!ReferenceEquals(
                preparation,
                compositionReview.Preparation))
        {
            throw new ArgumentException(
                "The composition review must belong to the retained source preparation.",
                nameof(compositionReview));
        }

        GenerationPreflightValidator.ValidateEvidence(
            preparation,
            compositionReview,
            evidenceAnalysis);

        Preparation = preparation;
        SetupOptions = setupOptions;
        CompositionReview = compositionReview;
        EvidenceAnalysis = evidenceAnalysis;
    }

    public GenerationSourcePreparationResult Preparation { get; }

    public IReadOnlyList<SelectedVideoSource> Sources =>
        Preparation.Request.Sources;

    public SelectedVideoSource ReferenceSource =>
        Preparation.Request.ReferenceSource;

    public IReadOnlyList<PreparedGenerationSource> PreparedSources =>
        Preparation.Sources;

    public PreparedGenerationSource ReferencePreparedSource =>
        Preparation.ReferenceSource;

    public int SourceCount =>
        Preparation.Request.SourceCount;

    public GenerationSetupOptions SetupOptions { get; }

    public GenerationCompositionReviewResult
        CompositionReview
    {
        get;
    }

    public IReadOnlyList<PreparedSourceCompositionPlan>
        SourceCompositionPlans =>
        CompositionReview.SourcePlans;

    public PreparedSourceCompositionPlan
        ReferenceCompositionPlan =>
        CompositionReview.ReferencePlan;

    public GenerationEvidenceAnalysisResult
        EvidenceAnalysis
    {
        get;
    }

    public IReadOnlyList<AnalyzedGenerationSource>
        AnalyzedSources =>
        EvidenceAnalysis.Sources;

    public AnalyzedGenerationSource
        ReferenceAnalyzedSource =>
        EvidenceAnalysis.ReferenceSource;

    public GenerationMode Mode =>
        SetupOptions.Mode;
}

public sealed class GenerationProgressUpdate
{
    public GenerationProgressUpdate(
        string title,
        string detail,
        bool isIndeterminate,
        double? progressPercent = null,
        string? sourceName = null,
        int? sourceNumber = null,
        int? sourceCount = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "A progress update requires a title.",
                nameof(title));
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException(
                "A progress update requires a detail message.",
                nameof(detail));
        }

        if (isIndeterminate && progressPercent is not null)
        {
            throw new ArgumentException(
                "Indeterminate progress cannot include a percentage.",
                nameof(progressPercent));
        }

        if (progressPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(progressPercent),
                progressPercent,
                "Progress must be between 0 and 100 percent.");
        }

        bool hasSourcePosition =
            sourceNumber is not null ||
            sourceCount is not null;

        if (hasSourcePosition)
        {
            if (sourceNumber is null || sourceCount is null)
            {
                throw new ArgumentException(
                    "Source number and source count must be supplied together.");
            }

            int actualSourceNumber = sourceNumber.Value;
            int actualSourceCount = sourceCount.Value;

            if (actualSourceCount <= 0 ||
                actualSourceNumber <= 0 ||
                actualSourceNumber > actualSourceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceNumber),
                    sourceNumber,
                    "The source position is outside the available source count.");
            }

            if (string.IsNullOrWhiteSpace(sourceName))
            {
                throw new ArgumentException(
                    "A source progress update requires a source name.",
                    nameof(sourceName));
            }
        }
        else if (!string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException(
                "A source name requires a source number and source count.",
                nameof(sourceName));
        }

        Title = title;
        Detail = detail;
        IsIndeterminate = isIndeterminate;
        ProgressPercent = progressPercent;
        SourceName = sourceName;
        SourceNumber = sourceNumber;
        SourceCount = sourceCount;
    }

    public string Title { get; }

    public string Detail { get; }

    public bool IsIndeterminate { get; }

    public double? ProgressPercent { get; }

    public string? SourceName { get; }

    public int? SourceNumber { get; }

    public int? SourceCount { get; }
}

public sealed class GeneratedClipCandidate
{
    public GeneratedClipCandidate(
        string id,
        int globalRank,
        string sourceFullPath,
        TimeSpan start,
        TimeSpan end,
        double score,
        string reason,
        double qualityTarget = 0,
        GenerationCandidateSelectionReason selectionReason =
            GenerationCandidateSelectionReason.QualityQualified,
        ClipPreferenceFeatureVector? preferenceFeatures = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A generated clip candidate requires an identifier.",
                nameof(id));
        }
        if (globalRank <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(globalRank));
        }
        if (string.IsNullOrWhiteSpace(sourceFullPath))
        {
            throw new ArgumentException(
                "A generated clip candidate requires a source path.",
                nameof(sourceFullPath));
        }

        if (!Path.IsPathFullyQualified(sourceFullPath))
        {
            throw new ArgumentException(
                "A generated clip candidate source path must be fully qualified.",
                nameof(sourceFullPath));
        }
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "A clip candidate cannot start before the source begins.");
        }

        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "A clip candidate must end after it starts.");
        }

        if (score is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(score),
                score,
                "A clip candidate score must be between 0 and 100.");
        }

        if (qualityTarget is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(qualityTarget),
                qualityTarget,
                "A clip quality target must be between 0 and 100.");
        }

        if (!Enum.IsDefined(selectionReason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectionReason));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A clip candidate requires an explanation.",
                nameof(reason));
        }

        Id = id.Trim();
        GlobalRank = globalRank;
        SourceFullPath = sourceFullPath;
        Start = start;
        End = end;
        Score = score;
        Reason = reason;
        QualityTarget = qualityTarget;
        SelectionReason = selectionReason;
        PreferenceFeatures = preferenceFeatures;
    }

    public string Id { get; }

    public int GlobalRank { get; }

    public string SourceFullPath { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration =>
        End - Start;

    public double Score { get; }

    public string Reason { get; }

    public double QualityTarget { get; }

    public GenerationCandidateSelectionReason SelectionReason { get; }

    public ClipPreferenceFeatureVector? PreferenceFeatures { get; }

    public bool MeetsQualityTarget =>
        Score >= QualityTarget;

    public bool RequiredDiversityRelaxation =>
        SelectionReason ==
        GenerationCandidateSelectionReason.CountFillRelaxedDiversity;
}

public sealed class GenerationResult
{
    private readonly ReadOnlyCollection<GeneratedClipCandidate> _candidates;

    public GenerationResult(
        GenerationRequest request,
        GenerationMomentFindingResult moments,
        GenerationCaptionPreparationResult? captions = null,
        GenerationEditorialMetadataResult? editorialMetadata = null,
        GenerationCandidateIntelligenceResult? candidateIntelligence = null,
        GenerationHiddenMomentDeck? hiddenMoments = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(moments);
        if (!ReferenceEquals(
                moments.Request.EvidenceAnalysis,
                request.EvidenceAnalysis) ||
            !ReferenceEquals(moments.Request.Setup, request.SetupOptions) ||
            captions is not null &&
            !ReferenceEquals(captions.Moments, moments) ||
            editorialMetadata is not null &&
            !ReferenceEquals(editorialMetadata.Moments, moments) ||
            candidateIntelligence is not null &&
            !ReferenceEquals(candidateIntelligence.RefinedMoments, moments) ||
            hiddenMoments is not null &&
            !ReferenceEquals(hiddenMoments.SelectedMoments, moments))
        {
            throw new ArgumentException(
                "Generation results must retain one coherent request, moment, and caption chain.");
        }
        GeneratedClipCandidate[] snapshot =
            moments.SelectedCandidates
                .Select(
                    selected =>
                    {
                        string reason = selected.Candidate.Score.Components
                            .OrderByDescending(
                                static component =>
                                    component.SignedContribution)
                            .Select(
                                static component =>
                                    component.Explanation)
                            .FirstOrDefault() ??
                            "Selected by deterministic evidence.";
                        return new GeneratedClipCandidate(
                            selected.Id,
                            selected.GlobalRank,
                            selected.AnalyzedSource.PreparedSource.Media
                                .FullPath,
                            selected.Candidate.Window.Start,
                            selected.Candidate.Window.End,
                            selected.FinalScore,
                            reason,
                            request.SetupOptions.QualityThreshold,
                            selected.SelectionReason,
                            GenerationClipPreferenceFeatureExtractor.Create(
                                selected));
                    })
                .ToArray();

        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A successful generation result requires at least one selected moment.");
        }

        Request = request;
        Moments = moments;
        Captions = captions;
        EditorialMetadata = editorialMetadata;
        CandidateIntelligence = candidateIntelligence;
        HiddenMoments = hiddenMoments ??
            new GenerationHiddenMomentDeck(moments);
        Mode = request.Mode;
        _candidates = Array.AsReadOnly(snapshot);
    }

    public GenerationRequest Request { get; }

    public GenerationMomentFindingResult Moments { get; }

    public GenerationCaptionPreparationResult? Captions { get; }

    public GenerationEditorialMetadataResult? EditorialMetadata { get; }

    public GenerationCandidateIntelligenceResult? CandidateIntelligence { get; }

    public GenerationHiddenMomentDeck HiddenMoments { get; }

    public GenerationMode Mode { get; }

    public IReadOnlyList<GeneratedClipCandidate> Candidates =>
        _candidates;

    public int CandidateCount =>
        _candidates.Count;

    public int OutputFileCount => 0;
}

public sealed class GenerationSourceException : IOException
{
    public GenerationSourceException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A source failure requires a message.",
                nameof(message));
        }
    }
}

public sealed class GenerationEngineUnavailableException :
    InvalidOperationException
{
    public GenerationEngineUnavailableException(
        string message)
        : base(message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "An unavailable-engine failure requires a message.",
                nameof(message));
        }
    }
}
