using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataEvidenceBuilder
{
    internal static IReadOnlyList<ClipEditorialEvidenceReference> Build(
        ClipEditorialMetadataRequest request,
        IReadOnlyList<Qwen3VlGroundedMetadataGroundingReference> grounding,
        Qwen3VlGroundedMetadataGenerationValidation validation)
    {
        IEnumerable<ClipEditorialEvidenceReference> knowledgeEvidence =
            grounding
                .SelectMany(static value => value.KnowledgeReferenceIds)
                .Distinct(StringComparer.Ordinal)
                .Select(id => BuildKnowledgeEvidence(request.Context, id));
        IEnumerable<ClipEditorialEvidenceReference> visualDraftEvidence =
            validation.VisualDrafts.Select(static draft =>
                new ClipEditorialEvidenceReference(
                    $"qwen-grounded-visual-draft-{draft.Ordinal:D2}",
                    ClipEditorialEvidenceKind.VisualObservation,
                    VisualDraftDescription(draft)));
        IEnumerable<ClipEditorialEvidenceReference> stableReadableTextEvidence =
            validation.StableReadableText.Select(static (value, index) =>
                new ClipEditorialEvidenceReference(
                    $"qwen-stable-readable-text-{index + 1:D2}",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Readable text repeated with the same normalized wording " +
                    $"across at least two chronological visual drafts: {value}"));
        IEnumerable<ClipEditorialEvidenceReference> visualEventEvidence =
            validation.VisualEventSelectionAssessments.Select(assessment =>
                new ClipEditorialEvidenceReference(
                    $"qwen-visual-event-assessment-{assessment.Ordinal:D2}",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Visual-event assessment " + assessment.Ordinal +
                    $": distinct action={assessment.DistinctAction}, " +
                    $"object interaction={assessment.ObjectInteraction}, " +
                    $"visible outcome={assessment.VisibleOutcome}, " +
                    $"readable interface change={assessment.ReadableInterfaceChange}, " +
                    $"routine only={assessment.RoutineOnly}, " +
                    $"uncertain={assessment.Uncertain}, score={assessment.Score}." +
                    (validation.ActorAuthorityAssessmentApplied
                        ? $" Actor authority={assessment.ActorAuthority}; " +
                            $"creator-experience relation={assessment.CreatorExperienceRelation}; " +
                            $"presentation={assessment.PresentationKind}."
                        : string.Empty)))
            .Append(new ClipEditorialEvidenceReference(
                PrimaryVisualEvidenceId(validation),
                ClipEditorialEvidenceKind.VisualObservation,
                $"Visual draft {validation.PrimaryVisualDraftOrdinal} was selected deterministically as " +
                (PrimaryHasDistinctEventSupport(validation)
                    ? "the primary audience-metadata event."
                    : "the best available grounded audience-metadata evidence; no distinct visual event was asserted.") +
                (validation.ActorAuthorityAssessmentApplied
                    ? $" Actor authority={validation.PrimaryActorAuthority}; " +
                        $"creator-experience relation={validation.PrimaryCreatorExperienceRelation}; " +
                        $"presentation={PrimaryPresentation(validation)}; " +
                        $"moment={validation.EditorialFrame.MomentKind}."
                    : string.Empty)));
        IEnumerable<ClipEditorialEvidenceReference> selectedKnowledgeEvidence =
            validation.SelectedCurrentPassageId == "None"
                ? []
                : [BuildKnowledgeEvidence(
                    request.Context,
                    validation.SelectedCurrentPassageId)];
        IEnumerable<ClipEditorialEvidenceReference> generalContextEvidence =
            (request.Context.GameKnowledge?.Matches ?? [])
                .Where(static match =>
                    match.Strength == GameKnowledgeMatchStrength.GeneralContext)
                .Select(match => BuildKnowledgeEvidence(
                    request.Context,
                    match.Passage.Id));
        IEnumerable<ClipEditorialEvidenceReference> knowledgeAssessmentEvidence =
            validation.KnowledgeSelectionAssessments.Select(
                static (assessment, index) =>
                    new ClipEditorialEvidenceReference(
                        $"qwen-knowledge-assessment-{index + 1:D2}",
                        ClipEditorialEvidenceKind.GameKnowledge,
                        $"Licensed passage {assessment.PassageId} visible-support assessment: " +
                        $"setting={assessment.SettingSupport}, " +
                        $"entity={assessment.EntityIdentitySupport}, " +
                        $"object={assessment.DistinctiveObjectSupport}, " +
                        $"action={assessment.CentralActionSupport}, " +
                        $"chronology={assessment.ChronologySupport}, " +
                        $"material contradiction={assessment.MaterialContradiction}."));
        return request.Context.Evidence
            .Concat(request.Context.Transcripts.Select(
                transcript => new ClipEditorialEvidenceReference(
                    $"stream-{transcript.AbsoluteAudioStreamIndex}",
                    transcript.Role.Role switch
                    {
                        AudioContentRole.CreatorSpeech =>
                            ClipEditorialEvidenceKind.CreatorTranscript,
                        AudioContentRole.GameDialogue =>
                            ClipEditorialEvidenceKind.GameDialogueTranscript,
                        AudioContentRole.MixedSpeech =>
                            ClipEditorialEvidenceKind.MixedTranscript,
                        _ => ClipEditorialEvidenceKind.UserContext,
                    },
                    $"{transcript.Authority} {transcript.Role.Role} transcript from absolute audio stream {transcript.AbsoluteAudioStreamIndex}.")))
            .Concat(visualDraftEvidence)
            .Concat(stableReadableTextEvidence)
            .Concat(visualEventEvidence)
            .Concat(knowledgeAssessmentEvidence)
            .Concat(knowledgeEvidence)
            .Concat(selectedKnowledgeEvidence)
            .Concat(generalContextEvidence)
            .Concat(validation.IsolatedFieldMergedJsonSha256 is string mergedHash
                ? new[] { new ClipEditorialEvidenceReference(
                    "qwen-isolated-field-authoring", ClipEditorialEvidenceKind.VisualObservation,
                    "Visual copy and attributed commentary were generated in two separately constrained component passes. " +
                    "Their mechanical merge was verified against the returned metadata; merged JSON SHA-256=" + mergedHash + ".") }
                : Array.Empty<ClipEditorialEvidenceReference>())
            .Append(new ClipEditorialEvidenceReference(
                "qwen-editorial-frame",
                ClipEditorialEvidenceKind.VisualObservation,
                "Story-shape-only editorial frame: presentation=" +
                PrimaryPresentation(validation) +
                "; moment=" + validation.EditorialFrame.MomentKind +
                "; premise=" +
                (validation.EditorialFrame.Premise ?? "unresolved") + "."))
            .Append(new ClipEditorialEvidenceReference(
                Qwen3VlGroundedMetadataExecutor.ReviewEvidenceId(
                    request.ReviewVideo!),
                ClipEditorialEvidenceKind.VisualObservation,
                "Audience metadata was grounded against the verified bounded review video used for this clip."))
            .GroupBy(static evidence => evidence.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
    }

    internal static IReadOnlyList<ClipEditorialWarning> BuildWarnings(
        ClipEditorialMetadataRequest request,
        Qwen3VlGroundedMetadataGenerationValidation validation,
        IReadOnlyCollection<Qwen3VlGroundedMetadataGroundingReference> grounding)
    {
        ClipEditorialContext context = request.Context;
        var warnings = new List<ClipEditorialWarning>();
        if (context.Transcripts.Count == 0)
        {
            warnings.Add(new ClipEditorialWarning(
                ClipEditorialWarningCode.TranscriptUnavailable,
                "No user-selected transcript was available, so AI metadata used visual and deterministic evidence only."));
        }
        if (request.ReviewVideo is null &&
            !context.Evidence.Any(static evidence =>
                evidence.Kind == ClipEditorialEvidenceKind.VisualObservation))
        {
            warnings.Add(new ClipEditorialWarning(
                ClipEditorialWarningCode.VisualObservationUnavailable,
                "No qualified visual observation was available for this clip."));
        }
        if (!context.GameContext.IsUserGrounded)
        {
            warnings.Add(new ClipEditorialWarning(
                ClipEditorialWarningCode.GameContextUnconfirmed,
                "The game name came from a folder hint and should be confirmed before publishing."));
        }
        if (context.GameKnowledge?.Warnings.Any(static warning =>
                warning.Code == GameKnowledgeWarningCode.Unavailable) == true)
        {
            warnings.Add(new ClipEditorialWarning(
                ClipEditorialWarningCode.GameKnowledgeUnavailable,
                "Open game knowledge was unavailable; this draft remains grounded in local clip evidence."));
        }
        else if (context.GameContext.UseOpenGameKnowledge &&
            validation.SelectedCurrentPassageId == "None" &&
            grounding.Count == 0)
        {
            bool hasGeneralContext = context.GameKnowledge?.Matches.Any(
                static match => match.Strength ==
                    GameKnowledgeMatchStrength.GeneralContext) == true;
            warnings.Add(new ClipEditorialWarning(
                ClipEditorialWarningCode.GameKnowledgeNotSelected,
                hasGeneralContext
                    ? "Licensed general game context supplied canonical vocabulary, but no passage was visually confirmed for this exact event. The clip itself remained the authority for what happened."
                    : "No licensed game-knowledge passage was visually confirmed for this exact clip, so the draft used only bounded local clip evidence and any locally retained user notes."));
        }
        else if (context.GameContext.UseOpenGameKnowledge &&
            context.GameKnowledge?.HasClipLinkedKnowledge != true &&
            grounding.Count == 0)
        {
            warnings.Add(new ClipEditorialWarning(
                ClipEditorialWarningCode.GameKnowledgeNotClipLinked,
                "No licensed game-knowledge passage had enough local clip overlap to support story-specific copy."));
        }
        if (validation.RejectedRules.Count > 0)
        {
            warnings.Add(new ClipEditorialWarning(
                ClipEditorialWarningCode.AiDraftRegenerated,
                "An earlier AI draft was rejected by strict quality validation and regenerated (" +
                string.Join(", ", validation.RejectedRules) + ")."));
        }
        if (validation.MetadataReviewRequired)
        {
            warnings.Add(new ClipEditorialWarning(
                ClipEditorialWarningCode.MetadataReviewRequired,
                "The local AI completed this draft, but its title or description needs review. You can edit it or request a structurally different reroll."));
        }
        return warnings.AsReadOnly();
    }

    internal static GameKnowledgeInfluenceAudit BuildAudit(
        ClipEditorialMetadataRequest request,
        IReadOnlyCollection<Qwen3VlGroundedMetadataGroundingReference> grounding,
        Qwen3VlGroundedMetadataGenerationValidation validation)
    {
        string[] usedClaims = grounding
            .SelectMany(static item => item.KnowledgeReferenceIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (request.Context.GameContext.IsUserGrounded)
        {
            usedClaims = usedClaims.Append("game-identity")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        string[] usedEvidence = grounding
            .SelectMany(static item => item.ClipEvidenceReferenceIds)
            .Append(PrimaryVisualEvidenceId(validation))
            .Append("qwen-editorial-frame")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        GroundedEditorialBrief resolvedBrief = ResolveBrief(
            request.Context.EditorialBrief,
            validation,
            usedClaims);
        GroundedEditorialSourceBinding[] usedBindings = resolvedBrief
            .SourceBindings
            .Where(binding => usedClaims.Contains(
                binding.ClaimId,
                StringComparer.Ordinal))
            .ToArray();
        bool ambiguousClaimUsed = resolvedBrief.Claims.Any(claim =>
            usedClaims.Contains(claim.Id, StringComparer.Ordinal) &&
            claim.State == GroundedGameContextClaimState.Ambiguous);
        return new GameKnowledgeInfluenceAudit(
            request.Context.EditorialBrief.Fingerprint,
            request.RevisionKind,
            usedClaims,
            usedEvidence,
            validation.MetadataReviewRequired || ambiguousClaimUsed,
            fallbackReason: null,
            resolvedBrief,
            usedBindings);
    }

    private static GroundedEditorialBrief ResolveBrief(
        GroundedEditorialBrief brief,
        Qwen3VlGroundedMetadataGenerationValidation validation,
        IReadOnlyCollection<string> usedClaimIds)
    {
        string? beat = validation.EditorialFrame.Premise;
        string? leadIn = validation.PrimaryVisualDraftOrdinal > 1
            ? JoinActions(validation.VisualDrafts[
                validation.PrimaryVisualDraftOrdinal - 2])
            : null;
        string? followThrough = validation.PrimaryVisualDraftOrdinal <
                validation.VisualDrafts.Count
            ? JoinActions(validation.VisualDrafts[
                validation.PrimaryVisualDraftOrdinal])
            : null;
        GroundedCreatorControlRelation relation = validation
            .PrimaryCreatorExperienceRelation switch
        {
            Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorActed =>
                GroundedCreatorControlRelation.CreatorControlled,
            Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorAffected =>
                GroundedCreatorControlRelation.CreatorAffected,
            Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorEncountered =>
                GroundedCreatorControlRelation.CreatorEncountered,
            _ => GroundedCreatorControlRelation.Unestablished,
        };
        var flags = new List<string>();
        if (beat is null) flags.Add("PrimaryBeatUnresolved");
        if (validation.EditorialFrame.Premise is null)
        {
            flags.Add("EditorialPremiseUnresolved");
        }
        if (validation.EditorialFrame.CreatorAuthorityAdjusted)
        {
            flags.Add("CreatorAuthorityConservativelyAdjusted");
        }
        if (!PrimaryHasDistinctEventSupport(validation))
        {
            flags.Add("PrimaryVisualEventNotDistinct");
        }
        if (usedClaimIds.Any(id => brief.Claims.Any(claim =>
                claim.Id.Equals(id, StringComparison.Ordinal) &&
                claim.State == GroundedGameContextClaimState.Ambiguous)))
        {
            flags.Add("AmbiguousClaimUsed");
        }
        return brief.WithResolvedGameplay(
            beat,
            leadIn,
            followThrough,
            relation,
            flags,
            MapPresentationKind(PrimaryPresentation(validation)),
            MapMomentKind(validation.EditorialFrame.MomentKind));
    }

    private static Qwen3VlGroundedMetadataPresentationKind PrimaryPresentation(
        Qwen3VlGroundedMetadataGenerationValidation validation)
    {
        int index = validation.PrimaryVisualDraftOrdinal - 1;
        return index >= 0 && index <
                validation.VisualEventSelectionAssessments.Count
            ? validation.VisualEventSelectionAssessments[index].PresentationKind
            : Qwen3VlGroundedMetadataPresentationKind.Unclear;
    }

    private static bool PrimaryHasDistinctEventSupport(
        Qwen3VlGroundedMetadataGenerationValidation validation)
    {
        int index = validation.PrimaryVisualDraftOrdinal - 1;
        return index >= 0 && index <
                validation.VisualEventSelectionAssessments.Count &&
            validation.VisualEventSelectionAssessments[index]
                .HasDistinctEventSupport;
    }

    private static string PrimaryVisualEvidenceId(
        Qwen3VlGroundedMetadataGenerationValidation validation) =>
        PrimaryHasDistinctEventSupport(validation)
            ? "qwen-primary-visual-event"
            : "qwen-best-available-visual-evidence";

    private static GroundedEditorialPresentationKind MapPresentationKind(
        Qwen3VlGroundedMetadataPresentationKind value) => value switch
        {
            Qwen3VlGroundedMetadataPresentationKind.InteractiveGameplay =>
                GroundedEditorialPresentationKind.InteractiveGameplay,
            Qwen3VlGroundedMetadataPresentationKind.CinematicSequence =>
                GroundedEditorialPresentationKind.CinematicSequence,
            Qwen3VlGroundedMetadataPresentationKind.InWorldRecording =>
                GroundedEditorialPresentationKind.InWorldRecording,
            Qwen3VlGroundedMetadataPresentationKind.DocumentOrLore =>
                GroundedEditorialPresentationKind.DocumentOrLore,
            Qwen3VlGroundedMetadataPresentationKind.MenuOrLoadout =>
                GroundedEditorialPresentationKind.MenuOrLoadout,
            Qwen3VlGroundedMetadataPresentationKind.ObjectiveOrInterface =>
                GroundedEditorialPresentationKind.ObjectiveOrInterface,
            Qwen3VlGroundedMetadataPresentationKind.MixedOrTransition =>
                GroundedEditorialPresentationKind.MixedOrTransition,
            _ => GroundedEditorialPresentationKind.Unclear,
        };

    private static GroundedEditorialMomentKind MapMomentKind(
        Qwen3VlGroundedMetadataMomentKind value) => value switch
        {
            Qwen3VlGroundedMetadataMomentKind.Action =>
                GroundedEditorialMomentKind.Action,
            Qwen3VlGroundedMetadataMomentKind.Progress =>
                GroundedEditorialMomentKind.Progress,
            Qwen3VlGroundedMetadataMomentKind.Complication =>
                GroundedEditorialMomentKind.Complication,
            Qwen3VlGroundedMetadataMomentKind.Discovery =>
                GroundedEditorialMomentKind.Discovery,
            Qwen3VlGroundedMetadataMomentKind.Exposition =>
                GroundedEditorialMomentKind.Exposition,
            Qwen3VlGroundedMetadataMomentKind.Decision =>
                GroundedEditorialMomentKind.Decision,
            Qwen3VlGroundedMetadataMomentKind.Outcome =>
                GroundedEditorialMomentKind.Outcome,
            Qwen3VlGroundedMetadataMomentKind.Routine =>
                GroundedEditorialMomentKind.Routine,
            _ => GroundedEditorialMomentKind.Unclear,
        };

    private static string? JoinActions(
        Qwen3VlGroundedMetadataVisualDraft draft)
    {
        string value = string.Join("; ", draft.Actions).Trim();
        return value.Length == 0 ? null : value;
    }

    internal static string AppendSignature(string description, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return description;
        }

        string combined = description + Environment.NewLine +
            Environment.NewLine + signature.Trim();
        if (combined.Length > ClipEditorialMetadataDraft.MaximumDescriptionLength)
        {
            throw new Qwen3VlOutputParseException(
                "The generated description and reusable signature exceed the editorial limit.");
        }
        return combined;
    }

    private static string VisualDraftDescription(
        Qwen3VlGroundedMetadataVisualDraft draft)
    {
        string prefix = FormattableString.Invariant(
            $"Chronological visual draft {draft.Ordinal} covering {draft.StartSeconds:0.###}-{draft.EndSeconds:0.###} seconds: ");
        return prefix +
            $"environment={draft.Environment}; environment uncertain={draft.EnvironmentUncertain}; " +
            $"subjects and objects={string.Join(" | ", draft.SubjectsAndObjects)}; " +
            $"actions={string.Join(" | ", draft.Actions)}; " +
            $"readable text={string.Join(" | ", draft.ReadableText)}; " +
            $"uncertainties={string.Join(" | ", draft.Uncertainties)}.";
    }

    private static ClipEditorialEvidenceReference BuildKnowledgeEvidence(
        ClipEditorialContext context,
        string passageId)
    {
        ClipGameKnowledgeContext knowledge = context.GameKnowledge ??
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata cited absent game knowledge.");
        GameKnowledgeMatch match = knowledge.Matches.Single(value =>
            value.Passage.Id.Equals(passageId, StringComparison.Ordinal));
        GameKnowledgeSource source = knowledge.Snapshot!.Sources.Single(value =>
            value.Id.Equals(match.Passage.SourceId, StringComparison.Ordinal));
        return new ClipEditorialEvidenceReference(
            match.Passage.Id,
            ClipEditorialEvidenceKind.GameKnowledge,
            $"{source.Attribution} {source.LicenseIdentifier}. " +
            $"{match.Passage.Section}: {match.Passage.Text}");
    }
}
