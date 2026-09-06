using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static Task ActualIsolatedGenerationRetainsVerifiedRephraseNonAttempt()
    {
        // Exact result object from actual AI34 provider capture 282aca54ce22493ea07e8a6c1db2a163.
        // It is parser evidence, not accepted copy: both semantic review findings remain.
        string captured = System.IO.File.ReadAllText(RepositoryLayout.GetPath("tests",
            "ReplayFoundry.PreparationTests", "Fixtures", "editorial-34-isolated-result.json"));
        const string angle = "I think we have to run to because I think it turns back on if you take your time.";
        ClipEditorialContext basis = AutomaticCommentaryAuthorityRequest(angle).Context;
        var brief = new GroundedEditorialBrief(basis.CandidateId, basis.SourceStart, basis.SourceEnd, [],
            canonicalIdentity: "REANIMAL", safeCommentaryAngle: angle,
            qualityFlags: ["AutomaticCommentaryNominatesOnly", "AutomaticCreatorReactionAngleAvailable"]);
        var context = new ClipEditorialContext(basis.CandidateId, basis.SourceFullPath, basis.SourceLabel,
            basis.SourceStart, basis.SourceEnd, basis.SourceDuration, basis.DeterministicScore,
            basis.DeterministicReason, basis.Transcripts, basis.Evidence,
            new ClipEditorialGameContext("REANIMAL", "#REANIMAL", null, ClipEditorialGameContextSource.UserConfirmed),
            editorialBrief: brief);
        var request = new ClipEditorialMetadataRequest(context, ClipEditorialProfile.Default, 3,
            ClipEditorialGenerationPreference.AiRequired, variantIntent: ClipEditorialVariantIntent.DirectAction);
        using JsonDocument document = JsonDocument.Parse(captured);
        Qwen3VlGroundedMetadataGenerationValidation validation = Qwen3VlGroundedMetadataGenerationParser.Parse(
            document.RootElement, request, Qwen3VlGroundedMetadataGenerator.PreviousCompactIsolatedFieldAuthoringOutputSchema);
        TestAssert.Throws<Qwen3VlOutputParseException>(() => Qwen3VlGroundedMetadataGenerationParser.Parse(
            document.RootElement, request, Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "Actual 1.59 output cannot be relabeled with the compact 1.1 component prompts.");
        TestAssert.True(validation.EditorialRephrase is { Attempted: false, Applied: false, Outcome: "NotAttempted", EligibilitySkipped: false },
            "Verified isolated field authoring must not be forced to invent a rephrase or an eligibility skip.");
        TestAssert.True(validation.IsolatedFieldMergedJsonSha256 is not null &&
            validation.GenerationPassCount == 7 && validation.GroundingPassCount == 5 && validation.SynthesisPassCount == 0,
            "The actual result retains five grounding calls and two independently verified component calls.");
        TestAssert.True(validation.MetadataReviewRequired && validation.MetadataReviewIssues.SequenceEqual(
            new[] { "UnsupportedCreatorEmbodiment", "BalanceNotSatisfied" }),
            "Transport acceptance must retain the actual semantic review findings.");
        TestAssert.Throws<Qwen3VlOutputParseException>(() => Qwen3VlGroundedMetadataEditorialRephrasePolicy.Parse(
            document.RootElement.GetProperty("generation")),
            "The same inactive shape is invalid unless component authoring was verified first.");
        Action<JsonObject>[] mutations =
        [
            result => result["generation"]!["editorialRephraseAttempted"] = true,
            result => result["generation"]!["editorialRephraseApplied"] = true,
            result => result["generation"]!["editorialRephraseOutcome"] = "Applied",
            result => result["generation"]!["editorialRephraseSourceJsonSha256"] = new string('a', 64),
            result => result["generation"]!["editorialRephraseOutputJsonSha256"] = new string('a', 64),
            result => result["generation"]!["editorialRephraseRejectionCode"] = "BalanceNotSatisfied",
            result => result["generation"]!["editorialRephraseCanonicalMessagesSha256"] = new string('a', 64),
            result => result["generation"]!["editorialRephraseRenderedPromptSha256"] = new string('a', 64),
            result => result["generation"]!["editorialRephraseRenderedPromptUtf8ByteCount"] = 1,
            result => result["generation"]!["editorialRephraseInputTokenIdsSha256"] = new string('a', 64),
            result => result["generation"]!["editorialRephraseInputTokenCount"] = 1,
            result => result["generation"]!["editorialRephraseRawOutputSha256"] = new string('a', 64),
            result => { result["generation"]!["isolatedFieldAuthoring"] = null; result["generation"]!["isolatedFieldAuthoringPassCount"] = 0; },
            result => result["generation"]!["generationPassCount"] = 8,
        ];
        foreach (Action<JsonObject> mutate in mutations)
        {
            JsonObject changed = JsonNode.Parse(captured)!.AsObject();
            mutate(changed);
            using JsonDocument altered = JsonDocument.Parse(changed.ToJsonString());
            TestAssert.Throws<Qwen3VlOutputParseException>(() => Qwen3VlGroundedMetadataGenerationParser.Parse(
                altered.RootElement, request, Qwen3VlGroundedMetadataGenerator.PreviousCompactIsolatedFieldAuthoringOutputSchema),
                "A non-attempt cannot claim generated witnesses, an extra pass, or unverified component authoring.");
        }
        return Task.CompletedTask;
    }

    private static Task IsolatedFieldsBindBothComponentsToTheirMechanicalMerge()
    {
        foreach (var variant in new[] { ClipEditorialVariantIntent.DirectAction, ClipEditorialVariantIntent.SpecificCuriosity })
        {
            ClipEditorialMetadataRequest request = AutomaticCommentaryAuthorityRequest("Why is that hat odd?").WithVariantIntent(variant);
            JsonObject result = IsolatedResultFixture(variant);
            ValidateIsolatedFixture(result, request);
            JsonObject generation = result["generation"]!.AsObject();
            string mergedHash = generation["isolatedFieldAuthoring"]!["mergedJsonSha256"]!.GetValue<string>();
            using JsonDocument document = JsonDocument.Parse(result.ToJsonString());
            TestAssert.Equal(mergedHash, Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.Validate(
                document.RootElement, document.RootElement.GetProperty("generation"), request,
                Qwen3VlGroundedMetadataGenerator.OutputSchema),
                "Only the mechanical full-object hash identifies the merged metadata; the top trace identifies commentary.");
            TestAssert.Throws<Qwen3VlOutputParseException>(() => ValidateIsolatedFixture(result, request,
                Qwen3VlGroundedMetadataGenerator.PreviousCompactIsolatedFieldAuthoringOutputSchema),
                "Historical output cannot claim compact component prompt identities.");
            JsonObject historical = result.DeepClone().AsObject();
            Component(historical, 0)["promptVersion"] = Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.PreviousComponentPromptVersion;
            Component(historical, 1)["promptVersion"] = Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.PreviousComponentPromptVersion;
            Component(historical, 0)["promptSha256"] = Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.PreviousVisualPromptSha256;
            Component(historical, 1)["promptSha256"] = Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.PreviousCommentaryPromptSha256;
            ValidateIsolatedFixture(historical, request, Qwen3VlGroundedMetadataGenerator.PreviousCompactIsolatedFieldAuthoringOutputSchema);
            TestAssert.Throws<Qwen3VlOutputParseException>(() => ValidateIsolatedFixture(historical, request),
                "Current output cannot claim the older component prompt identities.");
        }
        return Task.CompletedTask;
    }

    private static Task IsolatedFieldsRejectWitnessAndMergeTampering()
    {
        ClipEditorialMetadataRequest request = AutomaticCommentaryAuthorityRequest("Why is that hat odd?")
            .WithVariantIntent(ClipEditorialVariantIntent.DirectAction);
        Action<JsonObject>[] mutations =
        [
            result => result["generation"]!["isolatedFieldAuthoringPassCount"] = 1,
            result => result["generation"]!["isolatedFieldAuthoring"]!["components"]!.AsArray().RemoveAt(0),
            result => Component(result, 0)["kind"] = "Commentary",
            result => Component(result, 0)["field"] = "Description",
            result => Component(result, 0)["promptSha256"] = new string('f', 64),
            result => Component(result, 0)["inputTokenIdsSha256"] = "missing",
            result => Component(result, 0)["renderedPromptUtf8ByteCount"] = 0,
            result => Component(result, 0)["firstEndOfSequenceGeneratedIndex"] = 0,
            result => Component(result, 0)["maximumNewTokens"] = 2000,
            result => Component(result, 0)["structuredDecodingAudit"]!["schemaVersion"] = Qwen3VlGroundedMetadataGenerator.MetadataSchemaVersion,
            result => Component(result, 0)["structuredDecodingAudit"]!["unconstrainedFallbackUsed"] = true,
            result => Component(result, 0)["structuredDecodingAudit"]!["semanticRepairApplied"] = true,
            result => Component(result, 0)["structuredDecodingAudit"]!["generatedTokenCount"] = 1,
            result => Component(result, 0)["structuredDecodingAudit"]!["compileElapsedSeconds"] = -1,
            result => Component(result, 0)["outputJson"] = "{\"text\":\"Different text\"}",
            result => result["generation"]!["isolatedFieldAuthoring"]!["mergedJsonSha256"] = new string('f', 64),
            result => result["generation"]!["isolatedFieldAuthoring"]!["mergedJson"]!["titleBody"] = "Different text",
            result => result["metadata"]!["title"] = "THE BOAT REACHED THE DOCK",
            result => result["metadata"]!["description"] = "I wondered about something else.",
            result => result["metadata"]!["tags"] = new JsonArray("invented"),
            result => result["generation"]!["decodedTextSha256"] = new string('f', 64),
            result => result["structuredDecodingAudit"]!["schemaVersion"] = Qwen3VlGroundedMetadataGenerator.MetadataSchemaVersion,
        ];
        foreach (Action<JsonObject> mutate in mutations)
        {
            JsonObject result = IsolatedResultFixture(ClipEditorialVariantIntent.DirectAction);
            mutate(result);
            TestAssert.Throws<Qwen3VlOutputParseException>(() => ValidateIsolatedFixture(result, request),
                "Each component and the exact merge must remain bound independently.");
        }
        TestAssert.Throws<Qwen3VlOutputParseException>(() => ValidateIsolatedFixture(
            IsolatedResultFixture(ClipEditorialVariantIntent.DirectAction),
            AutomaticCommentaryAuthorityRequest("Why is that hat odd?", includeFlag: false)
                .WithVariantIntent(ClipEditorialVariantIntent.DirectAction)),
            "The isolated path cannot invent an automatic nomination that the host did not retain.");
        return Task.CompletedTask;
    }

    private static Task IsolatedFieldsCountOnlyActualComponentPasses()
    {
        var recovery = new Qwen3VlGroundedMetadataRecoveryValidation(
            30, new string('a', 64), 4, 2, 0,
            false, null, null, null, null, false, null, null, null,
            false, null, null, null, null, false, null, null, null, 0, null,
            [], null, Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.GroundedMetadataModules
                .Select(value => new Qwen3VlGroundedMetadataModuleIdentity(value.ModuleName, value.FileName, new string('a', 64))).ToArray(),
            [], new string('a', 64), new string('b', 64), 0, false, false, true,
            Qwen3VlGroundedMetadataActorAuthority.Unknown,
            Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
            0, null, null, null, false, [],
            new(false, false, "NotAttempted", null, null, null, null, false));
        var visual = new Qwen3VlGroundedMetadataVisualValidation(
            [new(1, 0, 5, "A dock", false, ["A boat"], ["The boat reached the dock"], [], [])],
            [], 1, Qwen3VlGroundedMetadataEditorialFrame.Unclear(1), []);
        var evidence = new Qwen3VlGroundedMetadataEvidenceValidation(false, "None", [], true, []);
        Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.ValidatePassProvenance(recovery, visual, evidence);
        var previousRecovery = recovery with
        {
            ModuleIdentities = Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PreviousResponsibilitySplitGroundedMetadataModules
                .Select(value => new Qwen3VlGroundedMetadataModuleIdentity(value.ModuleName, value.FileName, new string('a', 64))).ToArray(),
        };
        foreach (string previousSchema in new[]
        {
            Qwen3VlGroundedMetadataGenerator.PreviousResponsibilitySplitOutputSchema,
            Qwen3VlGroundedMetadataGenerator.PreviousCompactIsolatedFieldAuthoringOutputSchema,
        })
        {
            Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.ValidatePassProvenance(previousRecovery, visual, evidence, previousSchema);
            TestAssert.Throws<Qwen3VlOutputParseException>(() =>
                Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.ValidatePassProvenance(recovery, visual, evidence, previousSchema),
                "Historical isolated authoring must not accept the later expanded module roster.");
        }
        TestAssert.Throws<Qwen3VlOutputParseException>(() =>
            Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.ValidatePassProvenance(previousRecovery, visual, evidence),
            "Current isolated authoring must attest every extracted validation responsibility.");
        Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.ValidatePassProvenance(
            recovery with { GroundingPacketReused = true, GenerationPassCount = 2 }, visual, evidence);
        foreach (var invalid in new[]
        {
            recovery with { GenerationPassCount = 3 },
            recovery with { SynthesisPassCount = 2 },
            recovery with { GroundingPassCount = 1 },
            recovery with { GroundingPacketReused = true },
            recovery with { NonRetrospectiveRetryAnchorSourcePassOrdinal = 1 },
            recovery with { SynthesisRecoveryPoolAttemptedCandidateCount = 1 },
            recovery with { EditorialRephrase = recovery.EditorialRephrase! with { Attempted = true } },
        })
            TestAssert.Throws<Qwen3VlOutputParseException>(() =>
                Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.ValidatePassProvenance(invalid, visual, evidence),
                "Component work cannot be mislabeled as synthesis, a retry, rephrase or fresh grounding.");
        return Task.CompletedTask;
    }

    private static JsonObject Component(JsonObject result, int index) =>
        result["generation"]!["isolatedFieldAuthoring"]!["components"]![index]!.AsObject();

    private static void ValidateIsolatedFixture(JsonObject result, ClipEditorialMetadataRequest request, string? outputSchema = null)
    {
        using JsonDocument document = JsonDocument.Parse(result.ToJsonString());
        _ = Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.Validate(document.RootElement,
            document.RootElement.GetProperty("generation"), request, outputSchema ?? Qwen3VlGroundedMetadataGenerator.OutputSchema);
    }

    private static JsonObject IsolatedResultFixture(ClipEditorialVariantIntent variant)
    {
        bool thoughtTitle = variant == ClipEditorialVariantIntent.SpecificCuriosity;
        string visualField = thoughtTitle ? "Description" : "Title";
        string thoughtField = thoughtTitle ? "Title" : "Description";
        const string visualText = "The boat reached the dock.";
        const string thoughtText = "I wondered about that hat.";
        JsonObject visual = new() { ["text"] = visualText, ["tags"] = new JsonArray("boat"),
            ["grounding"] = new JsonArray(), ["temporalVoice"] = "RetrospectivePast" };
        JsonObject commentary = new() { ["text"] = thoughtText };
        JsonObject merged = new() { ["titleBody"] = thoughtTitle ? thoughtText : visualText,
            ["description"] = thoughtTitle ? visualText : thoughtText, ["tags"] = new JsonArray("boat"),
            ["grounding"] = new JsonArray(), ["temporalVoice"] = "RetrospectivePast" };
        JsonObject Witness(string kind, string field, JsonObject output, string promptHash, string schema, int count) => new()
        {
            ["kind"] = kind, ["field"] = field,
            ["promptVersion"] = Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.ComponentPromptVersion,
            ["promptSha256"] = promptHash, ["canonicalMessagesSha256"] = new string('a', 64),
            ["renderedPromptSha256"] = new string('b', 64), ["renderedPromptUtf8ByteCount"] = 200,
            ["inputTokenIdsSha256"] = new string('c', 64), ["inputTokenCount"] = 40,
            ["rawOutputSha256"] = new string(kind == "Visual" ? 'd' : 'e', 64),
            ["outputJsonSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ToJsonString()))),
            ["outputJson"] = output.ToJsonString(), ["generatedTokenCount"] = count,
            ["maximumNewTokens"] = Qwen3VlGroundedMetadataGenerator.MaximumNewTokens,
            ["terminationReason"] = "EndOfSequence", ["firstEndOfSequenceGeneratedIndex"] = count - 1,
            ["structuredDecodingAudit"] = JsonSerializer.SerializeToNode(new
            {
                policyVersion = Qwen3VlEditorialStructuredDecodingPolicy.Version,
                backendName = Qwen3VlEditorialStructuredDecodingPolicy.BackendName,
                backendVersion = Qwen3VlEditorialStructuredDecodingPolicy.BackendVersion,
                schemaVersion = schema, schemaSha256 = new string('a', 64),
                representation = Qwen3VlEditorialStructuredDecodingPolicy.Representation.ToString(),
                cudaMaskBackend = Qwen3VlEditorialStructuredDecodingPolicy.CudaMaskBackend,
                compileElapsedSeconds = 0.1, generatedTokenCount = count, grammarTerminationState = "EndOfSequence",
                strictParserAccepted = true, unconstrainedFallbackUsed = false, semanticRepairApplied = false,
            }),
        };
        JsonObject visualWitness = Witness("Visual", visualField, visual,
            Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.VisualPromptSha256,
            Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.VisualSchema, 50);
        JsonObject thoughtWitness = Witness("Commentary", thoughtField, commentary,
            Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.CommentaryPromptSha256,
            Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.CommentarySchema, 30);
        using JsonDocument mergedDocument = JsonDocument.Parse(merged.ToJsonString());
        JsonObject generation = new()
        {
            ["isolatedFieldAuthoringPassCount"] = 2,
            ["isolatedFieldAuthoring"] = new JsonObject
            {
                ["policyVersion"] = Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser.PolicyVersion,
                ["visualField"] = visualField, ["attributedThoughtField"] = thoughtField,
                ["components"] = new JsonArray(visualWitness, thoughtWitness), ["mergedJson"] = merged,
                ["mergedJsonSha256"] = Qwen3VlCanonicalJson.ComputeObjectSha256(mergedDocument.RootElement, string.Empty),
            },
            ["generatedTokenCount"] = 30, ["maximumNewTokens"] = Qwen3VlGroundedMetadataGenerator.MaximumNewTokens,
            ["terminationReason"] = "EndOfSequence", ["firstEndOfSequenceGeneratedIndex"] = 29,
            ["decodedTextSha256"] = new string('e', 64),
        };
        return new JsonObject
        {
            ["generation"] = generation, ["structuredDecodingAudit"] = thoughtWitness["structuredDecodingAudit"]!.DeepClone(),
            ["metadata"] = new JsonObject { ["title"] = (thoughtTitle ? thoughtText : visualText)[..^1],
                ["description"] = thoughtTitle ? visualText : thoughtText,
                ["tags"] = new JsonArray("boat"), ["grounding"] = new JsonArray() },
        };
    }
}
