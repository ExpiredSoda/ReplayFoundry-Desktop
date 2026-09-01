using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureArrayReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureJsonReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureParserValidation;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlHostFailureParser
{
    public static Qwen3VlHostFailureEnvelope Parse(
        string json,
        Qwen3VlHostCommand expectedCommand,
        VisualSemanticBatchRequest request,
        int expectedExitCode)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Parse(
            json,
            expectedCommand,
            Qwen3VlHostFailureParseContext.FromBatchRequest(request),
            expectedExitCode);
    }

    internal static Qwen3VlHostFailureEnvelope Parse(
        string json,
        Qwen3VlHostCommand expectedCommand,
        Qwen3VlHostFailureParseContext request,
        int expectedExitCode)
    {
        ArgumentNullException.ThrowIfNull(request);

        using JsonDocument document = Open(json);
        JsonElement root = document.RootElement;
        Qwen3VlHostFailureSchema schema =
            Qwen3VlHostFailureSchemaParser.Parse(root);
        string schemaVersion = schema.Version;
        bool currentFailureSchema = schema.IsCurrent;
        RequireExact(
            Text(root, "hostVersion", "$", 64),
            Qwen3VlHostFailureEnvelope.SupportedHostVersion,
            "$.hostVersion");
        Qwen3VlHostCommand command = ParseCommand(
            Text(root, "command", "$", 64));

        if (command != expectedCommand)
        {
            throw Failure(
                "$.command does not identify the failed host command.");
        }

        Qwen3VlHostFailureStage stage = EnumValue<Qwen3VlHostFailureStage>(
            root, "stage", "$");
        Qwen3VlHostFailureCase? failureCase =
            Qwen3VlHostFailurePayloadParser.ParseCase(root, request);
        Qwen3VlHostFailureSubmittedCase? ownedRequest =
            failureCase is null
                ? null
                : request.Cases[
                    failureCase.CaseOrdinal - 1];
        Qwen3VlHostFailureVideoArtifact? videoArtifact =
            Qwen3VlHostFailurePayloadParser.ParseVideoArtifact(root, ownedRequest);
        Qwen3VlHostFailureTiming? timing =
            Qwen3VlHostFailurePayloadParser.ParseTiming(root, ownedRequest);
        Qwen3VlHostFailureSampling sampling =
            Qwen3VlHostFailurePayloadParser.ParseSampling(root, stage);
        Qwen3VlHostFailureGeneration? generation =
            Qwen3VlHostFailureGenerationParser.ParseGeneration(
                root,
                failureCase);
        Qwen3VlHostFailureGenerationWatchdog? generationWatchdog =
            currentFailureSchema
                ? Qwen3VlHostFailureGenerationWatchdogParser.Parse(
                    root,
                    failureCase)
                : null;
        Qwen3VlGroundedMemoryPolicyAudit? groundedMemoryPolicy =
            currentFailureSchema
                ? Qwen3VlGroundedMemoryPolicy.ParseNullableFailure(root)
                : null;
        Qwen3VlHostFailureIdentity identity =
            Qwen3VlHostFailureGenerationParser.ParseIdentity(
                root,
                request);
        Qwen3VlHostFailureDetails details =
            Qwen3VlHostFailureGenerationParser.ParseDetails(
                root,
                expectedExitCode);
        DateTimeOffset createdAtUtc = Utc(root, "createdAtUtc", "$");
        string[] diagnostics =
            StringArray(
                root,
                "diagnostics",
                "$",
                maximumCount: 64,
                maximumItemLength: 1024,
                nullable: false)!;
        Qwen3VlHostFailureRecoveryPoolLedgerEntry[] recoveryPoolLedger =
            schema.HasRecoveryPoolLedger
                ? Qwen3VlHostFailureRecoveryPoolLedgerParser.Parse(
                    root,
                    schema.RequiresCurrentRecoveryPoolLedger)
                : [];

        Qwen3VlHostFailureReconciliation.RequireContextCompleteness(
            stage,
            failureCase,
            videoArtifact,
            timing,
            identity,
            details);
        Qwen3VlHostFailureReconciliation.RequireSamplingReconciles(
            sampling,
            timing);
        Qwen3VlHostFailureReconciliation.RequireGenerationReconciles(
            stage,
            failureCase,
            generation,
            generationWatchdog,
            details);

        return new Qwen3VlHostFailureEnvelope(
            schemaVersion,
            command,
            stage,
            failureCase,
            videoArtifact,
            timing,
            sampling,
            generation,
            generationWatchdog,
            groundedMemoryPolicy,
            identity,
            details,
            createdAtUtc,
            diagnostics,
            recoveryPoolLedger);
    }
}
