using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataGenerator;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataJson;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataRecoveryPolicyParser;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataSelection;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataVisualParser
{
    internal static Qwen3VlGroundedMetadataVisualValidation Parse(
        JsonElement generation,
        Qwen3VlGroundedMetadataGenerationSchemaProfile profile,
        Qwen3VlGroundedMetadataRecoveryValidation recovery)
    {
        int visualDraftCount = Qwen3VlEditorialJson.Integer(
            generation,
            "visualDraftCount");
        JsonElement[] visualDrafts = Qwen3VlEditorialJson.Array(
            generation,
            "visualDrafts");
        if (visualDrafts.Length != visualDraftCount)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen visual-draft provenance count is invalid.");
        }
        double previousEnd = 0;
        string? previousTier = null;
        var validatedDrafts = new List<Qwen3VlGroundedMetadataVisualDraft>(
            visualDrafts.Length);
        for (int index = 0; index < visualDrafts.Length; index++)
        {
            JsonElement draft = visualDrafts[index];
            string[] visualDraftFields =
            [
                "ordinal",
                "startSeconds",
                "endSeconds",
                "environment",
                "environmentUncertain",
                "subjectsAndObjects",
                "actions",
                "readableText",
                "uncertainties",
                "generatedTokenCount",
                "decodedTextSha256",
            ];
            Qwen3VlEditorialJson.Exact(
                draft,
                profile.AdaptiveSampling
                    ? [.. visualDraftFields, "sampling"]
                    : visualDraftFields);
            double start = Qwen3VlEditorialJson.Finite(draft, "startSeconds");
            double end = Qwen3VlEditorialJson.Finite(draft, "endSeconds");
            string? samplingTier = null;
            if (profile.AdaptiveSampling)
            {
                samplingTier =
                    Qwen3VlGroundedMetadataSamplingPolicy.ValidateDraft(
                        Qwen3VlEditorialJson.Object(draft, "sampling"),
                        profile.PeakBoundedSampling,
                        profile.LowPeakSampling);
            }
            Qwen3VlGroundedMetadataSamplingPolicy.ValidateWindowTimeline(
                previousEnd,
                previousTier,
                start,
                end,
                samplingTier,
                profile.PeakBoundedSampling);
            string environment = Qwen3VlEditorialJson.Text(draft, "environment");
            bool environmentUncertain = Boolean(draft, "environmentUncertain");
            string[] subjectsAndObjects = VisualDraftTextArray(
                draft,
                "subjectsAndObjects",
                1,
                6,
                100);
            string[] actions = VisualDraftTextArray(
                draft,
                "actions",
                1,
                4,
                100);
            string[] readableText = VisualDraftTextArray(
                draft,
                "readableText",
                0,
                4,
                80);
            string[] uncertainties = VisualDraftTextArray(
                draft,
                "uncertainties",
                0,
                3,
                100);
            if (Qwen3VlEditorialJson.Integer(draft, "ordinal") != index + 1 ||
                environment.Length > 120 ||
                Qwen3VlEditorialJson.Integer(
                    draft,
                    "generatedTokenCount") is < 1 or >= 768)
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen visual-draft provenance is invalid.");
            }
            _ = Qwen3VlEditorialJson.Sha256(draft, "decodedTextSha256");
            validatedDrafts.Add(new Qwen3VlGroundedMetadataVisualDraft(
                index + 1,
                start,
                end,
                environment,
                environmentUncertain,
                subjectsAndObjects,
                actions,
                readableText,
                uncertainties));
            previousEnd = end;
            previousTier = samplingTier;
        }
        string[] stableReadableText = VisualDraftTextArray(
            generation,
            "stableReadableText",
            0,
            4,
            80);
        RequireText(
            generation,
            "stableReadableTextPolicyVersion",
            StableReadableTextPolicyVersion);
        IReadOnlyList<string> expectedStableReadableText =
            Qwen3VlGroundedMetadataReadableText.FindStable(validatedDrafts);
        if (!stableReadableText.SequenceEqual(
                expectedStableReadableText,
                StringComparer.Ordinal))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen stable readable-text provenance is invalid.");
        }
        RequireText(
            generation,
            "visualDraftPromptVersion",
            profile.InterfaceAttributionVisualDraftPrompt
                ? VisualDraftPromptVersion
                : profile.LiteralActionVisualDraftPrompt
                    ? PreviousVisualDraftPromptVersion
                    : EarlierVisualDraftPromptVersion);
        RequireText(
            generation,
            "visualDraftPromptSha256",
            profile.InterfaceAttributionVisualDraftPrompt
                ? VisualDraftPromptSha256
                : profile.LiteralActionVisualDraftPrompt
                    ? PreviousVisualDraftPromptSha256
                    : EarlierVisualDraftPromptSha256);
        RequireText(
            generation,
            "visualDraftSchemaVersion",
            VisualDraftSchemaVersion);
        bool selectionApplied = Boolean(
            generation,
            "visualEventSelectionApplied");
        int primaryOrdinal = Qwen3VlEditorialJson.Integer(
            generation,
            "primaryVisualDraftOrdinal");
        RequireText(
            generation,
            "visualEventSelectionPromptVersion",
            profile.ActorAuthority
                ? VisualEventSelectionPromptVersion
                : PreviousVisualEventSelectionPromptVersion);
        RequireText(
            generation,
            "visualEventSelectionPromptSha256",
            profile.ActorAuthority
                ? VisualEventSelectionPromptSha256
                : PreviousVisualEventSelectionPromptSha256);
        RequireText(
            generation,
            "visualEventSelectionSchemaVersion",
            profile.FourDraftEventSelection
                ? VisualEventSelectionSchemaVersion
                : profile.ActorAuthority
                    ? PreviousVisualEventSelectionSchemaVersion
                    : InitialVisualEventSelectionSchemaVersion);
        JsonElement[] assessments = Qwen3VlEditorialJson.Array(
            generation,
            "visualEventSelectionAssessments");
        int assessmentCount = Qwen3VlEditorialJson.Integer(
            generation,
            "visualEventSelectionAssessmentCount");
        if (assessmentCount != assessments.Length ||
            selectionApplied != (visualDraftCount > 1) ||
            assessments.Length !=
                (profile.ActorAuthority
                    ? visualDraftCount
                    : selectionApplied ? visualDraftCount : 0) ||
            profile.ActorAuthority && !recovery.ActorAuthorityAssessmentApplied)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen visual-event assessment count is invalid.");
        }
        var validatedAssessments =
            new List<Qwen3VlGroundedMetadataVisualEventAssessment>(
                assessments.Length);
        for (int index = 0; index < assessments.Length; index++)
        {
            JsonElement assessment = assessments[index];
            Qwen3VlEditorialJson.Exact(
                assessment,
                profile.ActorAuthority
                    ? [
                        "ordinal",
                        "distinctAction",
                        "objectInteraction",
                        "visibleOutcome",
                        "readableInterfaceChange",
                        "routineOnly",
                        "uncertain",
                        "actorAuthority",
                        "creatorExperienceRelation",
                    ]
                    : [
                        "ordinal",
                        "distinctAction",
                        "objectInteraction",
                        "visibleOutcome",
                        "readableInterfaceChange",
                        "routineOnly",
                        "uncertain",
                    ]);
            int ordinal = Qwen3VlEditorialJson.Integer(assessment, "ordinal");
            if (ordinal != index + 1)
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen visual-event assessments are not ordered.");
            }
            bool uncertain = Boolean(assessment, "uncertain");
            if ((validatedDrafts[index].EnvironmentUncertain ||
                    validatedDrafts[index].Uncertainties.Count > 0) &&
                !uncertain)
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen visual-event assessment contradicted typed draft uncertainty.");
            }
            Qwen3VlGroundedMetadataActorAuthority actorAuthority =
                profile.ActorAuthority
                    ? ActorAuthority(assessment, "actorAuthority")
                    : Qwen3VlGroundedMetadataActorAuthority.Unknown;
            Qwen3VlGroundedMetadataCreatorExperienceRelation relation =
                profile.ActorAuthority
                    ? CreatorExperienceRelation(
                        assessment,
                        "creatorExperienceRelation")
                    : Qwen3VlGroundedMetadataCreatorExperienceRelation
                        .Unestablished;
            if (relation ==
                    Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorActed &&
                actorAuthority !=
                    Qwen3VlGroundedMetadataActorAuthority.CreatorControlled)
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen visual-event actor authority is invalid.");
            }
            validatedAssessments.Add(
                new Qwen3VlGroundedMetadataVisualEventAssessment(
                    ordinal,
                    Boolean(assessment, "distinctAction"),
                    Boolean(assessment, "objectInteraction"),
                    Boolean(assessment, "visibleOutcome"),
                    Boolean(assessment, "readableInterfaceChange"),
                    Boolean(assessment, "routineOnly"),
                    uncertain,
                    actorAuthority,
                    relation));
        }
        int expectedPrimaryOrdinal = 1;
        if (selectionApplied)
        {
            Qwen3VlGroundedMetadataVisualEventSelectionOutcome selection =
                SelectPrimaryVisualDraft(validatedAssessments);
            if (selection.Code ==
                    Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode
                        .NoDistinctPrimaryEvent ||
                selection.PrimaryVisualDraftOrdinal is not int selectedOrdinal)
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen visual-event selection established no distinct primary event.");
            }
            expectedPrimaryOrdinal = selectedOrdinal;
        }
        if (primaryOrdinal != expectedPrimaryOrdinal)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen primary visual-event selection is invalid.");
        }
        if (profile.ActorAuthority)
        {
            Qwen3VlGroundedMetadataVisualEventAssessment primaryAssessment =
                validatedAssessments[primaryOrdinal - 1];
            if (recovery.PrimaryActorAuthority !=
                    primaryAssessment.ActorAuthority ||
                recovery.PrimaryCreatorExperienceRelation !=
                    primaryAssessment.CreatorExperienceRelation)
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen primary actor-authority provenance is invalid.");
            }
        }
        return new(
            validatedDrafts.AsReadOnly(),
            stableReadableText,
            primaryOrdinal,
            validatedAssessments.AsReadOnly());
    }
}
