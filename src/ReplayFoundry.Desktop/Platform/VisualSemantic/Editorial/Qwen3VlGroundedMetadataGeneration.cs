using System.Globalization;
using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed class Qwen3VlGroundedMetadataExecutor
{
    private static readonly JsonSerializerOptions RequestJsonOptions =
        VisualSemanticRequestJsonPolicy.IndentedCamelCase;

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
        _modelIntegrity = _runtime.ModelIntegrity;

        _promptText = Qwen3VlGroundedMetadataPrompt.Load(
            _runtime.Host.HostScriptPath,
            nameof(runtime));
    }

    internal async Task<TResult> GenerateBatchAsync<TResult>(
        IReadOnlyList<ClipEditorialMetadataRequest> requests,
        Func<string, IReadOnlyList<ClipEditorialMetadataRequest>,
            TResult> parse,
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
        Exception? generationFailure = null;
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
                    _runtime.Host.EnvironmentVariables,
                    inheritParentEnvironment: false),
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
            TResult result = parse(json, requests);
            await Task.Run(
                () => _modelIntegrity.Verify(cancellationToken),
                cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            generationFailure = exception;
            throw;
        }
        finally
        {
            Exception? cleanupFailure = workspace.TryCleanup();
            if (cleanupFailure is not null && generationFailure is null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(cleanupFailure)
                    .Throw();
            }
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
                name = request.Context.GameContext.AudienceGameName,
                hashtag = request.Context.GameContext.AudienceGameHashtag,
                source = request.Context.GameContext.Source.ToString(),
                notes = request.Context.GameContext.IsUserGrounded
                    ? request.Context.GameContext.ContextNotes
                    : null,
            },
            gameKnowledge = Qwen3VlGroundedMetadataPayload
                .CreateGameKnowledge(request),
            visualText = Qwen3VlGroundedMetadataPayload
                .CreateVisualText(request),
            editorialBrief = CreateEditorialBrief(request),
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
                    spans = transcript.Spans.Select(static span => new
                    {
                        startSeconds = span.SourceStart.TotalSeconds,
                        endSeconds = span.SourceEnd.TotalSeconds,
                        span.Text,
                    }).ToArray(),
                }).ToArray(),
            evidence = Qwen3VlGroundedMetadataPayload.CreateEvidence(
                request,
                reviewVideo),
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

    private static object CreateEditorialBrief(
        ClipEditorialMetadataRequest request) => new
        {
            policyVersion = GroundedEditorialBrief.PolicyVersion,
            copyGoal = GroundedEditorialBrief.BroadCopyGoal,
            fingerprint = request.Context.EditorialBrief.Fingerprint,
            revisionKind = request.RevisionKind.ToString(),
            request.Context.EditorialBrief.CanonicalIdentity,
            request.Context.EditorialBrief.PrimaryGameplayBeat,
            request.Context.EditorialBrief.LeadIn,
            request.Context.EditorialBrief.VisibleFollowThrough,
            request.Context.EditorialBrief.SafeCommentaryAngle,
            creatorControlRelation = request.Context.EditorialBrief
                .CreatorControlRelation.ToString(),
            request.Context.EditorialBrief.QualityFlags,
            claims = request.Context.EditorialBrief.Claims
                .Take(24)
                .Select(static claim => new
                {
                    claim.Id,
                    kind = claim.Kind.ToString(),
                    claim.Value,
                    authority = claim.Authority.ToString(),
                    state = claim.State.ToString(),
                    claim.PublicSourceIds,
                    claim.LocalEvidenceIds,
                    fieldAuthorizations = claim.FieldAuthorizations
                        .Select(static field => field.ToString())
                        .ToArray(),
                }).ToArray(),
            sourceBindings = request.Context.EditorialBrief.SourceBindings
                .Take(24)
                .Select(static binding => new
                {
                    binding.ClaimId,
                    binding.PublicSourceIds,
                    binding.LocalEvidenceIds,
                    fieldAuthorizations = binding.FieldAuthorizations
                        .Select(static field => field.ToString())
                        .ToArray(),
                }).ToArray(),
        };

    internal static string ReviewEvidenceId(
        VisualSemanticInputManifest reviewVideo) =>
        Qwen3VlGroundedMetadataPayload.ReviewEvidenceId(reviewVideo);

}
