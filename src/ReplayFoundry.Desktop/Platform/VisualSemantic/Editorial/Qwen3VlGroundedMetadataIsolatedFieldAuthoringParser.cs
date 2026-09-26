using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataIsolatedFieldAuthoringParser
{
    internal const string PolicyVersion = "grounded-editorial-isolated-fields-1.0";
    internal const string ComponentPromptVersion = "1.1";
    internal const string VisualPromptSha256 = "dc1c9ff658191405213df367fabf28e815ed522c3c2d0aaf5350126c5526b263";
    internal const string CommentaryPromptSha256 = "09e3c1819b1722b0b596eae4ff8e9a6dac4636fcd873b6e3f3a59e991ab0366a";
    internal const string PreviousComponentPromptVersion = "1.0";
    internal const string PreviousVisualPromptSha256 = "8a7810dd6f6d56c53a89783ce844e4586ea61b141b586ba18db687a6b34ede29";
    internal const string PreviousCommentaryPromptSha256 = "f63ce0a4378222b2a5d4c2a6d1373c2bc6e2c6403abdf5e69737e7270ae772d5";
    internal const string VisualSchema = "grounded-editorial-isolated-visual-field-json-schema-1.0";
    internal const string CommentarySchema = "grounded-editorial-isolated-commentary-field-json-schema-1.0";

    internal static string? Validate(
        JsonElement result, JsonElement generation,
        ClipEditorialMetadataRequest request, string outputSchema)
    {
        if (!Qwen3VlGroundedMetadataSchemaCapabilities.SupportsIsolatedFieldAuthoring(outputSchema))
            return null;
        JsonElement authoring = Qwen3VlEditorialJson.Property(generation, "isolatedFieldAuthoring");
        int passes = Qwen3VlEditorialJson.Integer(generation, "isolatedFieldAuthoringPassCount");
        if (authoring.ValueKind == JsonValueKind.Null)
        {
            if (passes != 0) Fail("Inactive isolated authoring must report zero component passes.");
            return null;
        }
        Qwen3VlEditorialJson.Exact(authoring, "policyVersion", "visualField",
            "attributedThoughtField", "components", "mergedJson", "mergedJsonSha256");
        RequireText(authoring, "policyVersion", PolicyVersion);
        if (passes != 2 || request.Profile.CopyObjective != ClipEditorialCopyObjective.BalancedActionAndCommentary ||
            request.VariantIntent == ClipEditorialVariantIntent.CommentaryLed || !HasAutomaticNomination(request))
            Fail("Isolated authoring requires the balanced objective and exactly two component passes.");
        int hashtagLength = request.Context.GameContext.AudienceGameHashtag.Length;
        int titleMaximum = Math.Min(80, Math.Min(100, Math.Max(80, hashtagLength + 12)) - hashtagLength - 1);
        string thoughtField = request.VariantIntent == ClipEditorialVariantIntent.SpecificCuriosity && titleMaximum >= 20
            ? "Title" : "Description";
        string visualField = thoughtField == "Title" ? "Description" : "Title";
        RequireText(authoring, "visualField", visualField);
        RequireText(authoring, "attributedThoughtField", thoughtField);
        JsonElement[] components = Qwen3VlEditorialJson.Array(authoring, "components");
        if (components.Length != 2) Fail("Isolated authoring must retain both ordered component witnesses.");
        bool compactPrompts = Qwen3VlGroundedMetadataSchemaCapabilities.SupportsCompactIsolatedFieldAuthoring(outputSchema);
        string promptVersion = compactPrompts ? ComponentPromptVersion : PreviousComponentPromptVersion;
        JsonElement visual = ValidateComponent(components[0], "Visual", visualField, promptVersion,
            compactPrompts ? VisualPromptSha256 : PreviousVisualPromptSha256, VisualSchema);
        JsonElement commentary = ValidateComponent(components[1], "Commentary", thoughtField, promptVersion,
            compactPrompts ? CommentaryPromptSha256 : PreviousCommentaryPromptSha256, CommentarySchema);
        Qwen3VlEditorialJson.Exact(visual, "text", "tags", "grounding", "temporalVoice");
        Qwen3VlEditorialJson.Exact(commentary, "text");
        RequireText(visual, "temporalVoice", "RetrospectivePast");
        string visualText = BoundedText(visual, "text", visualField == "Title" ? titleMaximum : 420);
        string thoughtText = BoundedText(commentary, "text", thoughtField == "Title" ? titleMaximum : 420);
        if (!new[] { "I wondered about ", "I wondered whether ", "I wondered if ", "I compared " }
                .Any(opening => thoughtText.StartsWith(opening, StringComparison.Ordinal) && thoughtText.Length > opening.Length) ||
            thoughtText.IndexOfAny(['"', '\\', '\r', '\n']) >= 0)
            Fail("The commentary component did not retain its attributed opening.");
        JsonElement merged = Qwen3VlEditorialJson.Object(authoring, "mergedJson");
        Qwen3VlEditorialJson.Exact(merged, "titleBody", "description", "tags", "grounding", "temporalVoice");
        if (RawText(merged, "titleBody") != (visualField == "Title" ? visualText : thoughtText) ||
            RawText(merged, "description") != (visualField == "Description" ? visualText : thoughtText))
            Fail("The mechanical merge changed a component's exact text.");
        RequireText(merged, "temporalVoice", "RetrospectivePast");
        Equal(merged.GetProperty("tags"), visual.GetProperty("tags"), "Merged tags changed component output.");
        Equal(merged.GetProperty("grounding"), visual.GetProperty("grounding"), "Merged grounding changed component output.");
        string mergedHash = Qwen3VlEditorialJson.Sha256(authoring, "mergedJsonSha256");
        if (!Hash(merged).Equals(mergedHash, StringComparison.OrdinalIgnoreCase)) Fail("The merged JSON witness hash changed.");
        JsonElement metadata = Qwen3VlEditorialJson.Object(result, "metadata");
        Qwen3VlEditorialJson.Exact(metadata, "title", "description", "tags", "grounding");
        string title = RawText(merged, "titleBody").Trim();
        if (title.EndsWith('.') && !title.EndsWith("...", StringComparison.Ordinal) && title[..^1].TrimEnd().Length > 0)
            title = title[..^1].TrimEnd();
        RequireText(metadata, "title", title);
        RequireText(metadata, "description", RawText(merged, "description").Trim());
        string[] tags = Qwen3VlEditorialJson.Array(merged, "tags").Select(value =>
            value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString()!.Length <= 60 &&
                value.GetString()!.IndexOfAny(['#', '"', '\\', '\r', '\n', '\t']) < 0
                ? value.GetString()!.Trim() : throw new Qwen3VlOutputParseException("Isolated tags must be bounded text.")).ToArray();
        if (tags.Length is < 1 or > 8) Fail("Isolated tags must remain bounded.");
        Equal(metadata.GetProperty("tags"), JsonSerializer.SerializeToElement(tags), "Metadata tags changed component output.");
        ValidateGrounding(merged, metadata, request, visualField);
        // The legacy top trace is the final commentary component, never a model
        // generation of the mechanically merged full metadata object.
        foreach (string field in new[] { "generatedTokenCount", "maximumNewTokens", "terminationReason", "firstEndOfSequenceGeneratedIndex" })
            Equal(generation.GetProperty(field), components[1].GetProperty(field), "The final trace is not the commentary component trace.");
        RequireHash(generation, "decodedTextSha256", Qwen3VlEditorialJson.Sha256(components[1], "rawOutputSha256"));
        Equal(result.GetProperty("structuredDecodingAudit"), components[1].GetProperty("structuredDecodingAudit"),
            "The top audit is not the final commentary component audit.");
        return mergedHash;
    }

    private static JsonElement ValidateComponent(JsonElement component, string kind, string field, string promptVersion, string promptHash, string schema)
    {
        Qwen3VlEditorialJson.Exact(component, "kind", "field", "promptVersion", "promptSha256",
            "canonicalMessagesSha256", "renderedPromptSha256", "renderedPromptUtf8ByteCount",
            "inputTokenIdsSha256", "inputTokenCount", "rawOutputSha256", "outputJsonSha256", "outputJson",
            "generatedTokenCount", "maximumNewTokens", "terminationReason", "firstEndOfSequenceGeneratedIndex", "structuredDecodingAudit");
        RequireText(component, "kind", kind);
        RequireText(component, "field", field);
        RequireText(component, "promptVersion", promptVersion);
        RequireHash(component, "promptSha256", promptHash);
        foreach (string name in new[] { "canonicalMessagesSha256", "renderedPromptSha256", "inputTokenIdsSha256", "rawOutputSha256" })
            _ = Qwen3VlEditorialJson.Sha256(component, name);
        if (Qwen3VlEditorialJson.Integer(component, "renderedPromptUtf8ByteCount") <= 0 ||
            Qwen3VlEditorialJson.Integer(component, "inputTokenCount") <= 0)
            Fail("The isolated component has no prompt or input-token witness.");
        int count = Qwen3VlEditorialJson.Integer(component, "generatedTokenCount");
        if (count <= 0 || count >= Qwen3VlGroundedMetadataGenerator.MaximumNewTokens ||
            Qwen3VlEditorialJson.Integer(component, "maximumNewTokens") != Qwen3VlGroundedMetadataGenerator.MaximumNewTokens ||
            Qwen3VlEditorialJson.Integer(component, "firstEndOfSequenceGeneratedIndex") != count - 1)
            Fail("An isolated component did not reach bounded EOS completion.");
        RequireText(component, "terminationReason", "EndOfSequence");
        Qwen3VlGroundedMetadataEvidenceParser.ValidateComponentStructuredDecodingAudit(
            Qwen3VlEditorialJson.Object(component, "structuredDecodingAudit"), count, schema);
        string outputJson = RawText(component, "outputJson");
        if (outputJson.Length > 65_536) Fail("The isolated JSON witness exceeds its bounded generation.");
        RequireHash(component, "outputJsonSha256", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(outputJson))));
        try
        {
            using JsonDocument document = JsonDocument.Parse(outputJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) Fail("An isolated component must be a JSON object.");
            return document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw new Qwen3VlOutputParseException("The isolated component JSON is invalid: " + error.Message);
        }
    }

    private static void ValidateGrounding(JsonElement merged, JsonElement metadata, ClipEditorialMetadataRequest request, string visualField)
    {
        var bindings = new Dictionary<string, (string Knowledge, string Clip)>(StringComparer.Ordinal);
        JsonElement knowledge = JsonSerializer.SerializeToElement(Qwen3VlGroundedMetadataPayload.CreateGameKnowledge(request),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        JsonElement[] matches = knowledge.ValueKind == JsonValueKind.Null ? [] : Qwen3VlEditorialJson.Array(knowledge, "matches");
        foreach (JsonElement match in matches)
        {
            if (Qwen3VlEditorialJson.Text(match, "strength") is not ("ClipLinked" or "CandidateForVisualGrounding")) continue;
            string knowledgeId = Qwen3VlEditorialJson.Text(match, "id");
            IEnumerable<string> clips = Qwen3VlEditorialJson.Array(match, "clipEvidenceIds").Select(static value => value.GetString()!);
            foreach (string clip in clips)
            {
                string id = "gkb-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(knowledgeId + "\n" + clip))).ToLowerInvariant();
                bindings.Add(id, (knowledgeId, clip));
            }
        }
        JsonElement[] raw = Qwen3VlEditorialJson.Array(merged, "grounding");
        if (raw.Length > 1) Fail("Only the visual component may bind one audience field.");
        var expected = new List<object>();
        foreach (JsonElement item in raw)
        {
            Qwen3VlEditorialJson.Exact(item, "audienceField", "bindingIds");
            RequireText(item, "audienceField", visualField);
            string[] ids = Qwen3VlEditorialJson.Array(item, "bindingIds").Select(value =>
                value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty).ToArray();
            if (ids.Length is < 1 or > 4 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(id => !bindings.ContainsKey(id)))
                Fail("An isolated component cited an unavailable knowledge binding.");
            expected.Add(new { audienceField = visualField,
                knowledgeReferenceIds = ids.Select(id => bindings[id].Knowledge).Distinct(StringComparer.Ordinal).ToArray(),
                clipEvidenceReferenceIds = ids.Select(id => bindings[id].Clip).Distinct(StringComparer.Ordinal).ToArray() });
        }
        Equal(metadata.GetProperty("grounding"), JsonSerializer.SerializeToElement(expected), "Metadata grounding changed the isolated binding witness.");
    }

    internal static void ValidatePassProvenance(
        Qwen3VlGroundedMetadataRecoveryValidation recovery,
        Qwen3VlGroundedMetadataVisualValidation visual,
        Qwen3VlGroundedMetadataEvidenceValidation evidence,
        string outputSchema = Qwen3VlGroundedMetadataGenerator.OutputSchema)
    {
        int expectedGrounding = visual.VisualDrafts.Count +
            (recovery.ActorAuthorityAssessmentApplied || visual.VisualDrafts.Count > 1 ? 1 : 0) +
            (evidence.KnowledgeSelectionApplied ? 1 : 0);
        if (recovery.GroundingPassCount != expectedGrounding || recovery.SynthesisPassCount != 0 ||
            recovery.GenerationPassCount != (recovery.GroundingPacketReused == true ? 0 : expectedGrounding) + 2 ||
            recovery.SynthesisPassAttestations.Count != 0 || evidence.RejectedValidationRules.Count != 0 ||
            !evidence.GroundingReviewApplied || recovery.DuplicateSynthesisRecoveryApplied ||
            recovery.DuplicateSynthesisRecoverySourcePassOrdinal is not null || recovery.DuplicateSynthesisRecoveryRepeatedPassOrdinal is not null ||
            recovery.DuplicateSynthesisRecoverySourceRejectedJsonSha256 is not null || recovery.DuplicateSynthesisRecoveryRepeatedRejectedJsonSha256 is not null ||
            recovery.SampledSynthesisApplied || recovery.NonRetrospectiveRetryAnchorApplied ||
            recovery.NonRetrospectiveRetryAnchorSourcePassOrdinal is not null || recovery.NonRetrospectiveRetryAnchorSourceRule is not null ||
            recovery.NonRetrospectiveRetryAnchorEnvelopeSha256 is not null || recovery.NonRetrospectiveRetryAnchorAuthoritySha256 is not null ||
            recovery.SynthesisRecoveryPoolApplied || recovery.SynthesisRecoveryPoolSourcePassOrdinal is not null ||
            recovery.SynthesisRecoveryPoolSourceRejectedJsonSha256 is not null || recovery.SynthesisRecoveryPoolSourceSelectionReason is not null ||
            recovery.SynthesisRecoveryPoolAttemptedCandidateCount != 0 || recovery.SynthesisRecoveryPoolSelectedCandidateOrdinal is not null ||
            recovery.EditorialRephrase is not { Attempted: false, Applied: false, EligibilitySkipped: false })
            Fail("Isolated authoring reported ordinary synthesis, recovery or rephrase work that did not occur.");
        var expectedModules = Qwen3VlGroundedMetadataSchemaCapabilities.SupportsEditorialResponsibilityModules(outputSchema)
            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.GroundedMetadataModules
            : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PreviousResponsibilitySplitGroundedMetadataModules;
        if (recovery.ModuleIdentities.Count != expectedModules.Count) Fail("The isolated authoring module inventory changed.");
        for (int index = 0; index < expectedModules.Count; index++)
        {
            var actual = recovery.ModuleIdentities[index];
            var expected = expectedModules[index];
            if (actual.ModuleName != expected.ModuleName || actual.FileName != expected.FileName)
                Fail("The isolated authoring module inventory changed.");
        }
    }

    private static string BoundedText(JsonElement value, string name, int maximum)
    {
        string text = RawText(value, name);
        if (text.Length > maximum) Fail("Isolated component text exceeds the audience field bound.");
        return text;
    }
    private static string Hash(JsonElement value) => Qwen3VlCanonicalJson.ComputeObjectSha256(value, string.Empty);
    private static bool HasAutomaticNomination(ClipEditorialMetadataRequest request)
    {
        GroundedEditorialBrief brief = request.Context.EditorialBrief;
        if (brief.SafeCommentaryAngle is not string angle || !brief.QualityFlags.Contains(
                "AutomaticCreatorReactionAngleAvailable", StringComparer.Ordinal)) return false;
        static string Normalize(string value) => string.Join(' ', Regex.Matches(value, "[A-Za-z0-9]+", RegexOptions.CultureInvariant)
            .Select(static match => match.Value.ToLowerInvariant()));
        string normalized = Normalize(angle);
        return normalized.Length > 0 && request.Context.Transcripts.Any(transcript =>
            transcript.Role.Role == AudioContentRole.CreatorSpeech &&
            transcript.Authority == ClipEditorialTranscriptAuthority.AutomaticUnreviewed &&
            (Normalize(transcript.Text).Contains(normalized, StringComparison.Ordinal) ||
             Normalize(string.Join(' ', transcript.Spans.Select(static span => span.Text))).Contains(normalized, StringComparison.Ordinal)));
    }
    private static void RequireText(JsonElement value, string name, string expected)
    {
        if (!Qwen3VlEditorialJson.Text(value, name).Equals(expected, StringComparison.Ordinal))
            Fail("An isolated component field changed: " + name);
    }
    private static string RawText(JsonElement value, string name)
    {
        JsonElement property = Qwen3VlEditorialJson.Property(value, name);
        return property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()! : throw new Qwen3VlOutputParseException("An isolated component requires text: " + name);
    }
    private static void RequireHash(JsonElement value, string name, string expected)
    {
        if (!Qwen3VlEditorialJson.Sha256(value, name).Equals(expected, StringComparison.OrdinalIgnoreCase)) Fail("An isolated component hash changed: " + name);
    }
    private static void Equal(JsonElement left, JsonElement right, string message)
    {
        if (!JsonElement.DeepEquals(left, right)) Fail(message);
    }
    private static void Fail(string message) => throw new Qwen3VlOutputParseException(message);
}
