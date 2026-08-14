using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed class Qwen3VlGroundedMetadataExecutor : IDisposable
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly Qwen3VlQualifiedEditorialRuntime _runtime;
    private readonly IProcessRunner _processRunner;
    private readonly IQwen3VlBatchWorkspaceFactory _workspaceFactory;
    private readonly IQwen3VlGroundedFailureArchive _failureArchive;
    private readonly Qwen3VlVerifiedModelLease _modelIntegrity;
    private readonly string _promptText;

    internal Qwen3VlGroundedMetadataExecutor(
        Qwen3VlQualifiedEditorialRuntime runtime,
        IProcessRunner processRunner,
        IQwen3VlBatchWorkspaceFactory workspaceFactory,
        IQwen3VlGroundedFailureArchive failureArchive)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _processRunner = processRunner ??
            throw new ArgumentNullException(nameof(processRunner));
        _workspaceFactory = workspaceFactory ??
            throw new ArgumentNullException(nameof(workspaceFactory));
        _failureArchive = failureArchive ??
            throw new ArgumentNullException(nameof(failureArchive));
        _modelIntegrity = new Qwen3VlVerifiedModelLease(_runtime.Model);

        string hostDirectory = Path.GetDirectoryName(
            _runtime.Host.HostScriptPath)!;
        string promptPath = Path.Combine(
            hostDirectory,
            "replayfoundry-editorial-metadata-prompt-1.37.txt");
        if (!File.Exists(promptPath))
        {
            throw new ArgumentException(
                $"The grounded Qwen metadata prompt is missing beside the explicit host script: '{promptPath}'.",
                nameof(runtime));
        }

        _promptText = File.ReadAllText(promptPath)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(_promptText)))
            .ToLowerInvariant();
        if (!hash.Equals(
                Qwen3VlGroundedMetadataGenerator.PromptSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The grounded Qwen metadata prompt hash changed.");
        }
    }

    internal async Task<IReadOnlyList<ClipEditorialMetadataDraft>>
        GenerateBatchAsync(
            IReadOnlyList<ClipEditorialMetadataRequest> requests,
            Func<string, IReadOnlyList<ClipEditorialMetadataRequest>,
                IReadOnlyList<ClipEditorialMetadataDraft>> parse,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count is < 1 or > Qwen3VlGroundedMetadataGenerator.MaximumCases ||
            requests.Any(static request => request is null) ||
            requests.Select(static request =>
                    (request.Context.CandidateId, request.Attempt))
                .Distinct()
                .Count() != requests.Count)
        {
            throw new ArgumentException(
                $"Grounded Qwen metadata requires 1 to {Qwen3VlGroundedMetadataGenerator.MaximumCases} unique candidate attempts.",
                nameof(requests));
        }

        cancellationToken.ThrowIfCancellationRequested();
        // The model manifest can cover several gigabytes. Keep the first
        // integrity pass off the WPF dispatcher while retaining exact,
        // cancellation-aware verification before any model process starts.
        await Task.Run(
            () => _modelIntegrity.Verify(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        foreach (ClipEditorialMetadataRequest request in requests)
        {
            VisualSemanticInputManifest reviewVideo = request.ReviewVideo ??
                throw new InvalidOperationException(
                    "Grounded Qwen metadata requires a verified bounded review video.");
            await reviewVideo.VerifyIntegrityAsync(cancellationToken);
        }
        Qwen3VlBatchWorkspace workspace = _workspaceFactory.Create();
        try
        {
            await WriteRequestAsync(
                workspace.InputBatchPath,
                requests,
                cancellationToken);
            string failureOutputPath =
                _runtime.Host.FailureOutputPath ??
                workspace.FailureOutputPath;
            Qwen3VlHostFailureFile.RequireAvailable(failureOutputPath);
            Qwen3VlBatchCommand command =
                Qwen3VlBatchCommandBuilder.BuildGroundedMetadataRun(
                    _runtime.Host,
                    workspace,
                    _runtime.QualificationLockPath);
            ProcessRunResult process = await _processRunner.RunAsync(
                new ProcessRunRequest(
                    _runtime.Host.PythonExecutablePath,
                    command.Arguments,
                    _runtime.Host.ProcessTimeout,
                    workspace.DirectoryPath,
                    _runtime.Host.MaximumStandardOutputCharacters,
                    _runtime.Host.MaximumStandardErrorCharacters,
                    _runtime.Host.EnvironmentVariables),
                cancellationToken);
            if (!process.Succeeded)
            {
                Qwen3VlHostFailureEnvelope? hostFailure = null;
                Exception? envelopeFailure = null;
                try
                {
                    hostFailure = await Qwen3VlHostFailureFile
                        .ReadIfPresentAsync(
                            failureOutputPath,
                            _runtime.Host.MaximumStructuredOutputBytes,
                            Qwen3VlHostCommand.Run,
                            Qwen3VlHostFailureParseContext
                                .FromGroundedMetadata(requests, _runtime),
                            process.ExitCode,
                            cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                    when (exception is
                          Qwen3VlOutputParseException or
                          IOException or
                          UnauthorizedAccessException)
                {
                    envelopeFailure = exception;
                }
                Qwen3VlGroundedFailureArchiveResult archive =
                    _failureArchive.Archive(
                        failureOutputPath,
                        _runtime.Host.MaximumStructuredOutputBytes);
                string? failure =
                    Qwen3VlProcessOutputReader.FailureSummary(process);
                string? diagnostics = AppendDiagnostics(
                    Qwen3VlProcessOutputReader.Diagnostics(process),
                    archive);
                throw new Qwen3VlInferenceException(
                    failure is null
                        ? "The qualified local Qwen metadata batch failed." +
                            Qwen3VlGroundedMetadataFailureSummary.For(hostFailure)
                        : $"The qualified local Qwen metadata batch failed: {failure}" +
                            Qwen3VlGroundedMetadataFailureSummary.For(hostFailure),
                    diagnostics,
                    hostFailure: hostFailure,
                    failureEnvelopeParseException: envelopeFailure);
            }

            Qwen3VlHostFailureFile.RequireAbsentAfterSuccess(
                failureOutputPath);

            string json = await Qwen3VlProcessOutputReader.ReadAsync(
                workspace.OutputBatchPath,
                _runtime.Host.MaximumStructuredOutputBytes,
                cancellationToken);
            IReadOnlyList<ClipEditorialMetadataDraft> result =
                parse(json, requests);
            await Task.Run(
                () => _modelIntegrity.Verify(cancellationToken),
                cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            workspace.Cleanup();
        }
    }

    private static string? AppendDiagnostics(
        string? diagnostics,
        Qwen3VlGroundedFailureArchiveResult archive)
    {
        string? archiveLine = archive.ArchivedPath is not null
            ? $"Retained grounded failure diagnostics: {archive.ArchivedPath}"
            : archive.Warning;
        return string.IsNullOrWhiteSpace(archiveLine)
            ? diagnostics
            : string.IsNullOrWhiteSpace(diagnostics)
                ? archiveLine
                : diagnostics + Environment.NewLine + archiveLine;
    }

    private async Task WriteRequestAsync(
        string path,
        IReadOnlyList<ClipEditorialMetadataRequest> requests,
        CancellationToken cancellationToken)
    {
        object payload = new
        {
            schemaVersion = Qwen3VlGroundedMetadataGenerator.InputSchema,
            prompt = new
            {
                name = Qwen3VlGroundedMetadataGenerator.PromptName,
                version = Qwen3VlGroundedMetadataGenerator.PromptVersion,
                sha256 = Qwen3VlGroundedMetadataGenerator.PromptSha256,
                text = _promptText,
            },
            model = new
            {
                repositoryId = _runtime.Model.RepositoryId,
                revision = _runtime.Model.Revision,
                manifestSha256 = _runtime.Model.ManifestSha256,
            },
            requests = requests.Select(CreateRequest).ToArray(),
        };
        string json = JsonSerializer.Serialize(
            payload,
            RequestJsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static object CreateRequest(
        ClipEditorialMetadataRequest request)
    {
        VisualSemanticInputManifest reviewVideo = request.ReviewVideo ??
            throw new InvalidOperationException(
                "Grounded Qwen metadata requires a verified bounded review video.");
        return new
        {
            candidateId = request.Context.CandidateId,
            request.Attempt,
            priorAcceptedTitles = request.PriorAcceptedTitleExclusions
                .Select(static value => value.Title)
                .ToArray(),
            reviewVideo = new
            {
                path = reviewVideo.ReviewVideoPath,
                sha256 = reviewVideo.ReviewVideoSha256,
                byteLength = reviewVideo.ReviewVideoByteLength,
                lastWriteTimeUtc =
                    reviewVideo.ReviewVideoLastWriteTimeUtc.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
                reviewVideoDurationSeconds =
                    reviewVideo.ReviewVideoDuration.TotalSeconds,
            },
            game = new
            {
                name = request.Context.GameContext.GameName,
                hashtag = request.Context.GameContext.GameHashtag,
                source = request.Context.GameContext.Source.ToString(),
                notes = request.Context.GameContext.ContextNotes,
            },
            gameKnowledge = CreateGameKnowledge(request),
            visualText = CreateVisualText(request),
            clip = new
            {
                startSeconds = request.Context.SourceStart.TotalSeconds,
                endSeconds = request.Context.SourceEnd.TotalSeconds,
                sourceDurationSeconds =
                    request.Context.SourceDuration.TotalSeconds,
                deterministicScore = request.Context.DeterministicScore,
                deterministicReason =
                    request.Context.DeterministicReason,
            },
            transcripts = request.Context.Transcripts.Select(
                static transcript => new
                {
                    transcript.AbsoluteAudioStreamIndex,
                    role = transcript.Role.Role.ToString(),
                    authority = transcript.Authority.ToString(),
                    transcript.Text,
                }).ToArray(),
            evidence = CreateEvidence(request, reviewVideo),
            profile = new
            {
                request.Profile.AudienceAddress,
                request.Profile.NamingGuidance,
                request.Profile.ReusableDescriptionSignature,
                request.Profile.DefaultTags,
                voicePerspective =
                    request.Profile.VoicePerspective.ToString(),
                variantIntent = request.VariantIntent.ToString(),
            },
        };
    }

    private static object? CreateGameKnowledge(
        ClipEditorialMetadataRequest request)
    {
        ClipEditorialContext context = request.Context;
        ClipGameKnowledgeContext? knowledge = context.GameKnowledge;
        GameKnowledgeSnapshot? snapshot = knowledge?.Snapshot;
        if (snapshot is null || knowledge!.Matches.Count == 0)
        {
            return null;
        }
        var sourceIds = knowledge.Matches
            .Select(static value => value.Passage.SourceId)
            .ToHashSet(StringComparer.Ordinal);
        return new
        {
            policyVersion =
                DeterministicGameKnowledgeRetriever.PolicyVersion,
            snapshotSha256 = snapshot.SnapshotSha256,
            provider = new
            {
                snapshot.Provider.Name,
                snapshot.Provider.Version,
            },
            sources = snapshot.Sources
                .Where(source => sourceIds.Contains(source.Id))
                .Select(source => new
                {
                    source.Id,
                    kind = source.Kind.ToString(),
                    role = source.Role.ToString(),
                    source.Title,
                    pageUri = source.PageUri.AbsoluteUri,
                    source.RevisionId,
                    revisionTimestampUtc = source.RevisionTimestampUtc
                        .ToString("O", CultureInfo.InvariantCulture),
                    source.LicenseIdentifier,
                    licenseUri = source.LicenseUri.AbsoluteUri,
                    source.Attribution,
                    source.ContentSha256,
                }).ToArray(),
            matches = knowledge.Matches.Select(match => new
            {
                id = match.Passage.Id,
                match.Passage.SourceId,
                match.Passage.Section,
                match.Passage.Text,
                match.Passage.ContentSha256,
                strength = match.Strength.ToString(),
                temporalRelation = match.TemporalRelation.ToString(),
                match.Relevance,
                match.MatchedTerms,
                clipEvidenceIds = match.Strength ==
                        GameKnowledgeMatchStrength.CandidateForVisualGrounding
                    ? new[] { ReviewEvidenceId(request.ReviewVideo!) }
                    : match.ClipEvidenceIds,
            }).ToArray(),
        };
    }

    private static object[] CreateEvidence(
        ClipEditorialMetadataRequest request,
        VisualSemanticInputManifest reviewVideo)
    {
        var result = request.Context.Evidence.Select(evidence => (object)new
        {
            id = evidence.Id,
            kind = evidence.Kind.ToString(),
            description = evidence.Description,
        }).ToList();
        result.Add(new
        {
            id = ReviewEvidenceId(reviewVideo),
            kind = ClipEditorialEvidenceKind.VisualObservation.ToString(),
            description =
                "The verified bounded review video supplied to the local visual model.",
        });
        if (request.Context.VisualText is not null)
        {
            int remaining = Math.Max(0, 24 - result.Count);
            result.AddRange(request.Context.VisualText.GroundingAnchors
                .Take(remaining)
                .Select(
                anchor => (object)new
                {
                    id = anchor.EvidenceId,
                    kind = ClipEditorialEvidenceKind.VisualObservation.ToString(),
                    description =
                        $"Stable local Gameplay OCR across {anchor.OccurrenceCount} sampled frames: {anchor.DisplayText}",
                }));
        }
        return result.ToArray();
    }

    private static object? CreateVisualText(
        ClipEditorialMetadataRequest request)
    {
        var visualText = request.Context.VisualText;
        if (visualText is null)
        {
            return null;
        }

        var provider = visualText.Frames.FirstOrDefault()?.Provider;
        return new
        {
            samplingPolicyVersion =
                ClipVisualTextContext.SamplingPolicyVersion,
            stabilityPolicyVersion =
                ClipVisualTextContext.StabilityPolicyVersion,
            provider = provider is null
                ? null
                : new
                {
                    provider.Name,
                    provider.Version,
                    provider.Backend,
                    provider.RuntimeVersion,
                    provider.LanguageTag,
                },
            sampledFrameCount = visualText.Frames.Count,
            groundingAnchors = visualText.GroundingAnchors.Select(anchor => new
            {
                text = anchor.DisplayText,
                sourceKind = anchor.SourceKind.ToString(),
                occurrenceCount = anchor.OccurrenceCount,
                sourceTimestampsSeconds = anchor.SourceTimestamps
                    .Select(static value => value.TotalSeconds)
                    .ToArray(),
            }).ToArray(),
            diagnosticAnchors = visualText.Anchors
                .Where(static anchor => !anchor.MayGroundAudienceCopy)
                .Take(12)
                .Select(anchor => new
                {
                    text = anchor.DisplayText,
                    sourceKind = anchor.SourceKind.ToString(),
                    occurrenceCount = anchor.OccurrenceCount,
                    sourceTimestampsSeconds = anchor.SourceTimestamps
                        .Select(static value => value.TotalSeconds)
                        .ToArray(),
                }).ToArray(),
        };
    }

    internal static string ReviewEvidenceId(
        VisualSemanticInputManifest reviewVideo) =>
        $"bounded-review-{reviewVideo.ReviewVideoSha256[..16].ToLowerInvariant()}";

    public void Dispose() => _modelIntegrity.Dispose();

}
