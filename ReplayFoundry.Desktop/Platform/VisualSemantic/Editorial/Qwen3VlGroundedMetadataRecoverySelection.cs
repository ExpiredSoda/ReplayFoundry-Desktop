namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataRecoverySelection
{
    internal static void Validate(
        int synthesisPassCount,
        IReadOnlyList<string> rejectedValidationRules,
        string decodedTextSha256,
        bool synthesisRecoveryPoolApplied,
        int? synthesisRecoveryPoolSourcePassOrdinal,
        string? synthesisRecoveryPoolSourceRejectedJsonSha256,
        int synthesisRecoveryPoolAttemptedCandidateCount,
        int? synthesisRecoveryPoolSelectedCandidateOrdinal,
        IReadOnlyList<Qwen3VlGroundedMetadataModuleIdentity> moduleIdentities,
        IReadOnlyList<Qwen3VlGroundedMetadataSynthesisPassAttestation>
            attestations,
        bool duplicateSynthesisRecoveryApplied,
        string? duplicateSynthesisRecoverySourceRejectedJsonSha256,
        string? duplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
        bool nonRetrospectiveRetryAnchorApplied,
        int? nonRetrospectiveRetryAnchorSourcePassOrdinal,
        string? nonRetrospectiveRetryAnchorEnvelopeSha256,
        string? nonRetrospectiveRetryAnchorAuthoritySha256,
        IReadOnlySet<string>? retryableSemanticRejections,
        string? synthesisRecoveryPoolSourceSelectionReason,
        bool conditionalRecoveryPoolSource,
        bool strictRetryAnchorSourceRule,
        bool crossDraftRetrySourceWithholding,
        bool creatorAuthorityRetrySourceWithholding,
        bool semanticExhaustionRecovery,
        bool editorialRephraseSupported,
        bool rephraseMessageModuleSupported,
        Qwen3VlGroundedMetadataEditorialRephraseValidation? editorialRephrase)
    {
        ArgumentNullException.ThrowIfNull(rejectedValidationRules);
        ArgumentNullException.ThrowIfNull(decodedTextSha256);
        ArgumentNullException.ThrowIfNull(moduleIdentities);
        ArgumentNullException.ThrowIfNull(attestations);
        retryableSemanticRejections ??=
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .LegacyRetryableSemanticRejectionSet;

        IReadOnlyList<(string ModuleName, string FileName)> expectedModules =
            editorialRephraseSupported
                ? rephraseMessageModuleSupported
                    ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .GroundedMetadataModules
                    : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .PreviousEditorialRephraseGroundedMetadataModules
                : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousGroundedMetadataModules;
        bool validModules = moduleIdentities.Count == expectedModules.Count;
        for (int index = 0; validModules && index < expectedModules.Count; index++)
        {
            Qwen3VlGroundedMetadataModuleIdentity identity =
                moduleIdentities[index];
            (string expectedName, string expectedFile) = expectedModules[index];
            validModules = identity.ModuleName.Equals(
                    expectedName,
                    StringComparison.Ordinal) &&
                identity.FileName.Equals(expectedFile, StringComparison.Ordinal) &&
                IsSha256(identity.Sha256);
        }

        bool recoveredRejectedLanguage =
            editorialRephrase?.RecoveredRejectedLanguage == true;
        bool validAttestations = synthesisPassCount is >= 1 and <= 7 &&
            attestations.Count == synthesisPassCount &&
            rejectedValidationRules.Count == synthesisPassCount -
                (recoveredRejectedLanguage ? 0 : 1);
        for (int index = 0; validAttestations && index < attestations.Count; index++)
        {
            Qwen3VlGroundedMetadataSynthesisPassAttestation attestation =
                attestations[index];
            bool accepted = !recoveredRejectedLanguage &&
                index == attestations.Count - 1;
            string? expectedRejection = accepted
                ? null
                : rejectedValidationRules[index];
            validAttestations = attestation.Accepted == accepted &&
                (accepted
                    ? attestation.RejectionCode is null
                    : attestation.RejectionCode is not null &&
                        Qwen3VlGroundedMetadataSelection.IsKnownValidationRule(
                            attestation.RejectionCode) &&
                        attestation.RejectionCode.Equals(
                            expectedRejection,
                            StringComparison.Ordinal)) &&
                IsSha256(attestation.CanonicalMessagesSha256) &&
                IsSha256(attestation.RenderedPromptSha256) &&
                attestation.RenderedPromptUtf8ByteCount > 0 &&
                IsSha256(attestation.InputTokenIdsSha256) &&
                attestation.InputTokenCount > 0 &&
                IsSha256(attestation.OutputSha256) &&
                IsSha256(attestation.CompletedJsonSha256) &&
                (!strictRetryAnchorSourceRule ||
                    !attestation.RetryAnchorCaptured ||
                    !accepted && string.Equals(
                        attestation.RejectionCode,
                        "NonRetrospectiveVoice",
                        StringComparison.Ordinal)) &&
                (!attestation.RetryAnchorCaptured ||
                    IsSha256(attestation.RetryAnchorEnvelopeSha256)) &&
                (!attestation.RetryAnchorApplied ||
                    IsSha256(attestation.RetryAnchorEnvelopeSha256) &&
                    IsSha256(attestation.RetryAnchorAuthoritySha256) &&
                    attestation.RetryAnchorDisabledReason is null) &&
                (attestation.RetryAnchorAuthoritySha256 is null ||
                    IsSha256(attestation.RetryAnchorAuthoritySha256)) &&
                (attestation.RetryAnchorDisabledReason is null ||
                    !string.IsNullOrWhiteSpace(
                        attestation.RetryAnchorDisabledReason) &&
                    !attestation.RetryAnchorApplied);
        }

        int greedyCount = synthesisRecoveryPoolApplied
            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .LogicalPassOrdinal - 1
            : synthesisPassCount;
        bool recoveryScopeWasNarrowedByCrossDraft =
            semanticExhaustionRecovery
                ? rejectedValidationRules.Take(3).Contains(
                    "CrossDraftTitleContamination",
                    StringComparer.Ordinal)
                : rejectedValidationRules.Count > 0 &&
                    rejectedValidationRules[0].Equals(
                        "CrossDraftTitleContamination",
                        StringComparison.Ordinal);
        bool recoveryStartedWithCreatorAuthorityRejection =
            creatorAuthorityRetrySourceWithholding &&
            rejectedValidationRules.Count > 0 &&
            rejectedValidationRules[0].Equals(
                "UnsupportedCreatorEmbodiment",
                StringComparison.Ordinal);
        int expectedPoolSourcePassOrdinal =
            conditionalRecoveryPoolSource &&
                recoveryScopeWasNarrowedByCrossDraft
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PrimaryOnlyCrossDraftSourcePassOrdinal
                : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .OriginalSourcePassOrdinal;
        string? expectedPoolSourceSelectionReason =
            conditionalRecoveryPoolSource
                ? recoveryScopeWasNarrowedByCrossDraft
                    ? crossDraftRetrySourceWithholding
                        ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .PrimaryOnlyCrossDraftSourceSelectionReason
                        : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .PreviousPrimaryOnlyCrossDraftSourceSelectionReason
                    : recoveryStartedWithCreatorAuthorityRejection
                        ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .CreatorAuthorityRetrySourceSelectionReason
                    : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .OriginalSourceSelectionReason
                : null;
        for (int index = 0; validAttestations && index < greedyCount; index++)
        {
            Qwen3VlGroundedMetadataSynthesisPassAttestation attestation =
                attestations[index];
            validAttestations =
                attestation.LogicalPassOrdinal == index + 1 &&
                attestation.CandidateOrdinal is null &&
                attestation.Decoding ==
                    Qwen3VlGroundedMetadataSynthesisDecoding.Greedy &&
                attestation.Seed == 0 &&
                string.Equals(
                    attestation.SourceSelectionReason,
                    index > 0 && crossDraftRetrySourceWithholding &&
                            rejectedValidationRules[index - 1].Equals(
                                "CrossDraftTitleContamination",
                                StringComparison.Ordinal)
                        ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .CrossDraftRetrySourceSelectionReason
                        : index > 0 && creatorAuthorityRetrySourceWithholding &&
                            rejectedValidationRules[index - 1].Equals(
                                "UnsupportedCreatorEmbodiment",
                                StringComparison.Ordinal)
                            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                                .CreatorAuthorityRetrySourceSelectionReason
                            : null,
                    StringComparison.Ordinal) &&
                (index == 0
                    ? attestation.SourcePassOrdinal is null &&
                        attestation.SourceRejectedJsonSha256 is null
                    : attestation.SourcePassOrdinal == index &&
                        attestation.SourceRejectedJsonSha256 is not null &&
                        attestation.SourceRejectedJsonSha256.Equals(
                            attestations[index - 1].CompletedJsonSha256,
                            StringComparison.OrdinalIgnoreCase));
        }

        if (synthesisRecoveryPoolApplied)
        {
            for (int candidateIndex = 0;
                validAttestations &&
                    candidateIndex < synthesisRecoveryPoolAttemptedCandidateCount;
                candidateIndex++)
            {
                Qwen3VlGroundedMetadataSynthesisPassAttestation attestation =
                    attestations[greedyCount + candidateIndex];
                validAttestations =
                    attestation.LogicalPassOrdinal ==
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .LogicalPassOrdinal &&
                    attestation.CandidateOrdinal == candidateIndex + 1 &&
                    attestation.Decoding ==
                        Qwen3VlGroundedMetadataSynthesisDecoding.RecoveryPool &&
                    attestation.Seed ==
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .Seeds[candidateIndex] &&
                    attestation.SourcePassOrdinal == expectedPoolSourcePassOrdinal &&
                    string.Equals(
                        attestation.SourceSelectionReason,
                        expectedPoolSourceSelectionReason,
                        StringComparison.Ordinal) &&
                    attestation.SourceRejectedJsonSha256 is not null &&
                    attestation.SourceRejectedJsonSha256.Equals(
                        synthesisRecoveryPoolSourceRejectedJsonSha256,
                        StringComparison.OrdinalIgnoreCase) &&
                    (candidateIndex == 0 || HasIdenticalPromptInput(
                        attestations[greedyCount],
                        attestation)) &&
                    (attestation.Accepted ||
                        attestation.RejectionCode is not null &&
                        retryableSemanticRejections.Contains(
                            attestation.RejectionCode));
            }
        }

        bool validFinalOutput = attestations.Count > 0 &&
            (recoveredRejectedLanguage
                ? editorialRephrase is not null &&
                    editorialRephrase.RawOutputSha256.Equals(
                        decodedTextSha256,
                        StringComparison.OrdinalIgnoreCase)
                : attestations[^1].OutputSha256.Equals(
                    decodedTextSha256,
                    StringComparison.OrdinalIgnoreCase));
        bool validLanguageRecovery = !recoveredRejectedLanguage ||
            editorialRephrase is not null &&
            editorialRephrase.Attempted &&
            editorialRephrase.Applied &&
            editorialRephrase.SourceJsonSha256.Equals(
                attestations[^1].CompletedJsonSha256,
                StringComparison.OrdinalIgnoreCase) &&
            !editorialRephrase.OutputJsonSha256.Equals(
                editorialRephrase.SourceJsonSha256,
                StringComparison.OrdinalIgnoreCase);
        bool validPoolSource = !synthesisRecoveryPoolApplied ||
            synthesisRecoveryPoolSourcePassOrdinal ==
                expectedPoolSourcePassOrdinal &&
            string.Equals(
                synthesisRecoveryPoolSourceSelectionReason,
                expectedPoolSourceSelectionReason,
                StringComparison.Ordinal) &&
            attestations[expectedPoolSourcePassOrdinal - 1]
                .CompletedJsonSha256.Equals(
                    synthesisRecoveryPoolSourceRejectedJsonSha256,
                    StringComparison.OrdinalIgnoreCase) &&
            (recoveredRejectedLanguage
                ? synthesisRecoveryPoolSelectedCandidateOrdinal is null
                : synthesisRecoveryPoolSelectedCandidateOrdinal ==
                    synthesisRecoveryPoolAttemptedCandidateCount);
        bool validDuplicateWitness = !duplicateSynthesisRecoveryApplied ||
            attestations.Count >= 3 &&
            attestations[1].CompletedJsonSha256.Equals(
                duplicateSynthesisRecoverySourceRejectedJsonSha256,
                StringComparison.OrdinalIgnoreCase) &&
            attestations[2].CompletedJsonSha256.Equals(
                duplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
                StringComparison.OrdinalIgnoreCase);
        bool semanticExhaustionRecoveryApplied =
            semanticExhaustionRecovery &&
            !duplicateSynthesisRecoveryApplied &&
            rejectedValidationRules.Count >= 3 &&
            retryableSemanticRejections.Contains(
                rejectedValidationRules[2]);
        bool validRecoveryActivation = !synthesisRecoveryPoolApplied ||
            duplicateSynthesisRecoveryApplied ||
            semanticExhaustionRecoveryApplied;
        bool attestedRetryAnchorApplied = attestations.Any(
            static value => value.RetryAnchorApplied);
        bool validRetryAnchor =
            attestedRetryAnchorApplied == nonRetrospectiveRetryAnchorApplied;
        if (validRetryAnchor && nonRetrospectiveRetryAnchorApplied)
        {
            validRetryAnchor =
                nonRetrospectiveRetryAnchorSourcePassOrdinal is int sourcePass &&
                sourcePass >= 1 &&
                sourcePass <= rejectedValidationRules.Count &&
                (!strictRetryAnchorSourceRule ||
                    rejectedValidationRules[sourcePass - 1].Equals(
                        "NonRetrospectiveVoice",
                        StringComparison.Ordinal)) &&
                attestations[sourcePass - 1].RetryAnchorCaptured &&
                attestations[sourcePass - 1].RetryAnchorEnvelopeSha256 is not null &&
                attestations[sourcePass - 1].RetryAnchorEnvelopeSha256!.Equals(
                    nonRetrospectiveRetryAnchorEnvelopeSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                attestations.Where(static value => value.RetryAnchorApplied).All(
                    value => value.RetryAnchorEnvelopeSha256!.Equals(
                            nonRetrospectiveRetryAnchorEnvelopeSha256,
                            StringComparison.OrdinalIgnoreCase) &&
                        value.RetryAnchorAuthoritySha256!.Equals(
                            nonRetrospectiveRetryAnchorAuthoritySha256,
                            StringComparison.OrdinalIgnoreCase));
        }

        if (!validModules || !validAttestations || !validFinalOutput ||
            !validLanguageRecovery ||
            !validPoolSource || !validDuplicateWitness || !validRetryAnchor ||
            !validRecoveryActivation)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen synthesis recovery-pool attestation is invalid.");
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or
                >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool HasIdenticalPromptInput(
        Qwen3VlGroundedMetadataSynthesisPassAttestation expected,
        Qwen3VlGroundedMetadataSynthesisPassAttestation actual) =>
        actual.CanonicalMessagesSha256.Equals(
            expected.CanonicalMessagesSha256,
            StringComparison.OrdinalIgnoreCase) &&
        actual.RenderedPromptSha256.Equals(
            expected.RenderedPromptSha256,
            StringComparison.OrdinalIgnoreCase) &&
        actual.RenderedPromptUtf8ByteCount == expected.RenderedPromptUtf8ByteCount &&
        actual.InputTokenIdsSha256.Equals(
            expected.InputTokenIdsSha256,
            StringComparison.OrdinalIgnoreCase) &&
        actual.InputTokenCount == expected.InputTokenCount;
}
