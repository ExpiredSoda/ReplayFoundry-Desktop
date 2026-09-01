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
            profile.EditorialFraming
                ? VisualEventSelectionPromptVersion
                : profile.ActorAuthority
                    ? PreviousEditorialFramingVisualEventSelectionPromptVersion
                    : PreviousVisualEventSelectionPromptVersion);
        RequireText(
            generation,
            "visualEventSelectionPromptSha256",
            profile.EditorialFraming
                ? VisualEventSelectionPromptSha256
                : profile.ActorAuthority
                    ? PreviousEditorialFramingVisualEventSelectionPromptSha256
                    : PreviousVisualEventSelectionPromptSha256);
        RequireText(
            generation,
            "visualEventSelectionSchemaVersion",
            profile.EditorialFraming
                ? VisualEventSelectionSchemaVersion
                : profile.FourDraftEventSelection
                    ? PreviousEditorialFramingVisualEventSelectionSchemaVersion
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
                profile.EditorialFraming
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
                        "presentationKind",
                    ]
                    : profile.ActorAuthority
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
            Qwen3VlGroundedMetadataPresentationKind presentationKind =
                profile.EditorialFraming
                    ? ParseEnum<Qwen3VlGroundedMetadataPresentationKind>(
                        assessment,
                        "presentationKind")
                    : Qwen3VlGroundedMetadataPresentationKind.Unclear;
            if ((relation ==
                    Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorActed &&
                actorAuthority !=
                    Qwen3VlGroundedMetadataActorAuthority.CreatorControlled) ||
                (profile.EditorialFraming &&
                presentationKind is (
                    Qwen3VlGroundedMetadataPresentationKind.CinematicSequence or
                    Qwen3VlGroundedMetadataPresentationKind.InWorldRecording) &&
                (actorAuthority ==
                    Qwen3VlGroundedMetadataActorAuthority.CreatorControlled ||
                 relation != Qwen3VlGroundedMetadataCreatorExperienceRelation
                    .Unestablished)))
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
                    relation,
                    presentationKind));
        }
        int expectedPrimaryOrdinal = 1;
        if (selectionApplied)
        {
            Qwen3VlGroundedMetadataVisualEventSelectionOutcome selection =
                SelectPrimaryVisualDraft(
                    validatedAssessments,
                    profile.EditorialFrameAdherence ? validatedDrafts : null,
                    profile.BestAvailableVisualEvidence);
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
        Qwen3VlGroundedMetadataEditorialFrame editorialFrame =
            profile.EditorialFraming
                ? ParseEditorialFrame(generation, primaryOrdinal, validatedAssessments)
                : Qwen3VlGroundedMetadataEditorialFrame.Unclear(primaryOrdinal);
        if (profile.BestAvailableVisualEvidence &&
            selectionApplied &&
            !validatedAssessments.Any(static assessment =>
                assessment.HasDistinctEventSupport))
        {
            Qwen3VlGroundedMetadataMomentKind expectedMoment =
                validatedAssessments[primaryOrdinal - 1].RoutineOnly
                    ? Qwen3VlGroundedMetadataMomentKind.Routine
                    : Qwen3VlGroundedMetadataMomentKind.Unclear;
            if (editorialFrame.MomentKind != expectedMoment ||
                editorialFrame.Premise is not null ||
                !editorialFrame.SupportingDraftOrdinals.SequenceEqual(
                    [primaryOrdinal]))
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen best-available visual evidence was not conservatively framed.");
            }
        }
        return new(
            validatedDrafts.AsReadOnly(),
            stableReadableText,
            primaryOrdinal,
            editorialFrame,
            validatedAssessments.AsReadOnly());
    }

    private static Qwen3VlGroundedMetadataEditorialFrame ParseEditorialFrame(
        JsonElement generation,
        int primaryOrdinal,
        IReadOnlyList<Qwen3VlGroundedMetadataVisualEventAssessment> assessments)
    {
        JsonElement frame = Qwen3VlEditorialJson.Object(
            generation,
            "editorialFrame");
        Qwen3VlEditorialJson.Exact(
            frame,
            "policyVersion",
            "authorityKind",
            "momentKind",
            "premise",
            "supportingDraftOrdinals",
            "creatorAuthorityAdjusted");
        RequireText(frame, "policyVersion", "grounded-editorial-frame-1.0");
        RequireText(frame, "authorityKind", "StoryShapeOnly");
        Qwen3VlGroundedMetadataMomentKind momentKind =
            ParseEnum<Qwen3VlGroundedMetadataMomentKind>(frame, "momentKind");
        string? premise = Qwen3VlEditorialJson.NullableText(frame, "premise");
        if (premise?.Length > 180)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen editorial premise is invalid.");
        }
        int[] ordinals = Qwen3VlEditorialJson.Array(
                frame,
                "supportingDraftOrdinals")
            .Select(static item => item.ValueKind == JsonValueKind.Number &&
                    item.TryGetInt32(out int value)
                ? value
                : throw new Qwen3VlOutputParseException(
                    "Grounded Qwen editorial-frame ordinal is invalid."))
            .ToArray();
        if (ordinals.Length is < 1 or > 4 ||
            !ordinals.SequenceEqual(ordinals.Order()) ||
            ordinals.Distinct().Count() != ordinals.Length ||
            ordinals.Any(value => value < 1 || value > assessments.Count) ||
            !ordinals.Contains(primaryOrdinal))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen editorial-frame support is invalid.");
        }
        bool adjusted = Boolean(frame, "creatorAuthorityAdjusted");
        Qwen3VlGroundedMetadataVisualEventAssessment primary =
            assessments[primaryOrdinal - 1];
        if (adjusted &&
            (primary.PresentationKind !=
                Qwen3VlGroundedMetadataPresentationKind.CinematicSequence &&
             primary.PresentationKind !=
                Qwen3VlGroundedMetadataPresentationKind.InWorldRecording ||
             primary.ActorAuthority !=
                Qwen3VlGroundedMetadataActorAuthority.OtherPerson ||
             primary.CreatorExperienceRelation !=
                Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen editorial-frame authority adjustment is invalid.");
        }
        return new(
            "grounded-editorial-frame-1.0",
            "StoryShapeOnly",
            momentKind,
            premise,
            ordinals,
            adjusted);
    }

    private static T ParseEnum<T>(JsonElement value, string name)
        where T : struct, Enum
    {
        string text = Qwen3VlEditorialJson.Text(value, name);
        return Enum.TryParse(text, ignoreCase: false, out T parsed) &&
            Enum.IsDefined(parsed)
                ? parsed
                : throw new Qwen3VlOutputParseException(
                    $"Grounded Qwen '{name}' is unsupported.");
    }
}
