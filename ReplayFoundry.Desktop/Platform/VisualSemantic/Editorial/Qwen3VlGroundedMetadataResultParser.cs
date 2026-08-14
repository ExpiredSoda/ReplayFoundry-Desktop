using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataGenerator;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataJson;
namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataResultParser
{
    internal static IReadOnlyList<ClipEditorialMetadataDraft> Parse(
        string json,
        IReadOnlyList<ClipEditorialMetadataRequest> requests,
        Qwen3VlQualifiedEditorialRuntime runtime,
        ClipEditorialMetadataGeneratorIdentity identity)
    {
        using JsonDocument document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        JsonElement root = document.RootElement;
        string outputSchema = Qwen3VlEditorialJson.Text(root, "schemaVersion");
        bool generationWatchdog = Qwen3VlGroundedMetadataResultPolicyParser
            .UsesGenerationWatchdog(outputSchema);
        string[] rootFields =
        [
            "schemaVersion",
            "policyVersion",
            "promptSha256",
            "qualificationLockCanonicalHash",
            "results",
            "peakAllocatedGpuBytes",
            "totalElapsedSeconds",
            "canonicalHash",
        ];
        Qwen3VlEditorialJson.Exact(
            root,
            generationWatchdog
                ?
                [
                    .. rootFields,
                    "generationWatchdogPolicy",
                    "groundedMemoryPolicy",
                ]
                : rootFields);
        (string promptVersion, string promptSha256) =
            Qwen3VlGroundedMetadataResultPolicyParser.PromptIdentityFor(
                outputSchema);
        RequireText(
            root,
            "policyVersion",
            Qwen3VlEditorialStructuredDecodingPolicy.Version);
        RequireText(root, "promptSha256", promptSha256);
        RequireText(
            root,
            "qualificationLockCanonicalHash",
            runtime.QualificationLockCanonicalHash);
        if (generationWatchdog)
        {
            Qwen3VlGroundedMetadataResultPolicyParser
                .ValidateGenerationWatchdogPolicy(
                Qwen3VlEditorialJson.Object(
                    root,
                    "generationWatchdogPolicy"));
        }
        string canonicalHash = Qwen3VlEditorialJson.Text(root, "canonicalHash");
        if (!canonicalHash.Equals(
                Qwen3VlCanonicalJson.ComputeObjectSha256(root, "canonicalHash"),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata canonical hash is invalid.");
        }

        TimeSpan elapsed = Seconds(root, "totalElapsedSeconds");
        JsonElement peakElement = Qwen3VlEditorialJson.Property(
            root,
            "peakAllocatedGpuBytes");
        long? peak = peakElement.ValueKind == JsonValueKind.Null
            ? null
            : peakElement.TryGetInt64(out long value) && value >= 0
                ? value
                : throw new Qwen3VlOutputParseException(
                    "Grounded Qwen metadata peak GPU bytes are invalid.");
        if (generationWatchdog)
        {
            Qwen3VlGroundedMemoryPolicy.Parse(
                Qwen3VlEditorialJson.Object(root, "groundedMemoryPolicy"),
                requireCompleted: true,
                expectedPeakAllocatedBytes: peak,
                requireCurrentPolicy: Qwen3VlGroundedMetadataSchemaCapabilities
                    .SupportsLiteralActionPrompt(outputSchema));
        }
        var provenance = new ClipEditorialAiProvenance(
            identity.Name,
            identity.Version,
            Qwen3VlEditorialStructuredDecodingPolicy.Version,
            runtime.Model.RepositoryId,
            runtime.Model.Revision,
            runtime.Model.ManifestSha256,
            PromptName,
            promptVersion,
            promptSha256,
            elapsed,
            peak);

        JsonElement results = Qwen3VlEditorialJson.Property(root, "results");
        if (results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() != requests.Count)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata did not preserve every request.");
        }

        var drafts = new List<ClipEditorialMetadataDraft>(requests.Count);
        var acceptedTitles = new Dictionary<
            Qwen3VlGroundedMetadataRerollTitleScope,
            List<Qwen3VlGroundedMetadataRerollTitleReference>>();
        var groundingPackets = new Dictionary<
            string,
            (string RequestSha256, int SourceAttempt, string CandidateId,
                string FactWitness)>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (JsonElement result in results.EnumerateArray())
        {
            ClipEditorialMetadataRequest request = requests[index++];
            Qwen3VlEditorialJson.Exact(
                result,
                generationWatchdog
                    ?
                    [
                        "candidateId",
                        "attempt",
                        "metadata",
                        "generation",
                        "structuredDecodingAudit",
                        "elapsedSeconds",
                        "generationWatchdog",
                    ]
                    :
                    [
                        "candidateId",
                        "attempt",
                        "metadata",
                        "generation",
                        "structuredDecodingAudit",
                        "elapsedSeconds",
                    ]);
            RequireText(result, "candidateId", request.Context.CandidateId);
            if (Qwen3VlEditorialJson.Integer(result, "attempt") != request.Attempt)
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen metadata changed an attempt identity.");
            }
            Qwen3VlGroundedMetadataGenerationValidation validation =
                Qwen3VlGroundedMetadataGenerationParser.Parse(
                    result,
                    request,
                    outputSchema);
            if (generationWatchdog)
            {
                Qwen3VlGroundedMetadataResultPolicyParser
                    .ValidateGenerationWatchdogSuccess(
                    Qwen3VlEditorialJson.Object(
                        result,
                        "generationWatchdog"),
                    validation.GenerationPassCount);
            }
            Qwen3VlGroundedMetadataResultPolicyParser
                .ValidateGroundingPacketReuse(
                result,
                request,
                validation,
                groundingPackets);
            JsonElement metadata = Qwen3VlEditorialJson.Object(result, "metadata");
            Qwen3VlEditorialJson.Exact(
                metadata,
                "title",
                "description",
                "tags",
                "grounding");
            string title = Qwen3VlEditorialJson.Text(metadata, "title");
            string description = Qwen3VlEditorialJson.Text(
                metadata,
                "description");
            string[] tags = Qwen3VlEditorialJson.Array(metadata, "tags")
                .Select(tag => tag.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(tag.GetString())
                    ? tag.GetString()!
                    : throw new Qwen3VlOutputParseException(
                        "Grounded Qwen metadata tags must be text."))
                .ToArray();
            Qwen3VlGroundedMetadataGroundingReference[] grounding =
                Qwen3VlGroundedMetadataAudienceValidator.ParseGrounding(
                    metadata,
                    request,
                    title,
                    description);
            if (!Qwen3VlGroundedMetadataSchemaCapabilities
                    .SupportsReviewableAudienceCopy(outputSchema))
            {
                Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
                    title,
                    description,
                    tags,
                    request,
                    validation.ActorAuthorityAssessmentApplied
                        ? validation.VisualDrafts[
                            validation.PrimaryVisualDraftOrdinal - 1]
                        : null,
                    validation.ActorAuthorityAssessmentApplied
                        ? validation.PrimaryActorAuthority
                        : null,
                    validation.ActorAuthorityAssessmentApplied
                        ? validation.PrimaryCreatorExperienceRelation
                        : null,
                    requireLiteralActionEntailment:
                        Qwen3VlGroundedMetadataSchemaCapabilities
                            .SupportsLiteralActionPrompt(outputSchema),
                    requireInterfaceAttributionAuthority:
                        Qwen3VlGroundedMetadataSchemaCapabilities
                            .SupportsInterfaceAttribution(outputSchema),
                    allowNeutralPersonSubject:
                        Qwen3VlGroundedMetadataSchemaCapabilities
                            .SupportsNeutralPersonRecovery(outputSchema),
                    creatorAuthorityUsesAudienceFieldsOnly:
                        Qwen3VlGroundedMetadataSchemaCapabilities
                            .SupportsNeutralPersonRecovery(outputSchema));
            }
            Qwen3VlGroundedMetadataRerollTitleReference? acceptedTitle = null;
            IReadOnlyList<string> retainedPriorTitles =
                request.PriorAcceptedTitleExclusions
                    .Select(static value => value.Title)
                    .ToArray();
            if (Qwen3VlGroundedMetadataSchemaCapabilities.IsNewerThan(
                    outputSchema,
                    EarliestOutputSchema))
            {
                var titleScope =
                    new Qwen3VlGroundedMetadataRerollTitleScope(
                        request.Context.CandidateId,
                        request.Context.SourceStart,
                        request.Context.SourceEnd);
                acceptedTitles.TryGetValue(
                    titleScope,
                    out List<Qwen3VlGroundedMetadataRerollTitleReference>?
                        inBatchTitles);
                Qwen3VlGroundedMetadataRerollTitleReference[] priorTitles =
                    RetainPriorTitleReferences(
                        request,
                        inBatchTitles ?? []);
                retainedPriorTitles = priorTitles
                    .Select(static value => value.Title)
                    .ToArray();
                acceptedTitle =
                    Qwen3VlGroundedMetadataRerollDiversityPolicy.Reference(
                        request,
                        title);
                Qwen3VlGroundedMetadataRerollTitleDiversityResult diversity =
                    Qwen3VlGroundedMetadataRerollDiversityPolicy.Evaluate(
                        acceptedTitle,
                        priorTitles);
                if (!diversity.IsMateriallyDistinct &&
                    !validation.MetadataReviewIssues.Contains(
                        "RerollTitleTooSimilar",
                        StringComparer.Ordinal))
                {
                    throw new Qwen3VlOutputParseException(
                        "Grounded Qwen accepted a materially indistinct reroll title.");
                }
                Qwen3VlGroundedMetadataRerollDiversityPolicy
                    .ValidateReportedProvenance(diversity, validation);
            }
            description = Qwen3VlGroundedMetadataEvidenceBuilder.AppendSignature(
                description,
                request.Profile.ReusableDescriptionSignature);
            tags = ClipEditorialGeneratedTags.Build(
                request.Context,
                request.Profile.DefaultTags,
                tags);

            var draft = new ClipEditorialMetadataDraft(
                title,
                description,
                tags,
                ClipEditorialMetadataOrigin.AiAssisted,
                identity,
                request.Attempt,
                Qwen3VlGroundedMetadataEvidenceBuilder.Build(
                    request,
                    grounding,
                    validation),
                Qwen3VlGroundedMetadataEvidenceBuilder.BuildWarnings(
                    request,
                    validation,
                    grounding),
                provenance,
                ClipEditorialMetadataReadiness.GroundedDraft,
                qualityIssues: ClipEditorialMetadataReview.BuildIssues(
                    validation.MetadataReviewIssues),
                priorAcceptedTitles: retainedPriorTitles);
            drafts.Add(draft);
            if (acceptedTitle is not null)
            {
                if (!acceptedTitles.TryGetValue(
                        acceptedTitle.Scope,
                        out List<
                            Qwen3VlGroundedMetadataRerollTitleReference>?
                            history))
                {
                    history = [];
                    acceptedTitles.Add(acceptedTitle.Scope, history);
                }
                history.Add(acceptedTitle);
                if (history.Count >
                    ClipEditorialPriorTitleExclusion.MaximumRetainedTitles)
                {
                    history.RemoveRange(
                        0,
                        history.Count -
                            ClipEditorialPriorTitleExclusion
                                .MaximumRetainedTitles);
                }
            }
        }

        return drafts.AsReadOnly();
    }

    internal static Qwen3VlGroundedMetadataRerollTitleReference[]
        RetainPriorTitleReferences(
            ClipEditorialMetadataRequest request,
            IEnumerable<Qwen3VlGroundedMetadataRerollTitleReference>
                inBatchTitles)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(inBatchTitles);
        return request.PriorAcceptedTitleExclusions.Select(
                exclusion =>
                    Qwen3VlGroundedMetadataRerollDiversityPolicy.Reference(
                        exclusion.CandidateId,
                        exclusion.SourceStart,
                        exclusion.SourceEnd,
                        exclusion.Title,
                        request.Context.GameContext.GameHashtag))
            .Concat(inBatchTitles)
            .GroupBy(
                static value => value.Title,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    internal static (string Version, string Sha256) PromptIdentityFor(
        string outputSchema) =>
        Qwen3VlGroundedMetadataResultPolicyParser.PromptIdentityFor(
            outputSchema);
}
