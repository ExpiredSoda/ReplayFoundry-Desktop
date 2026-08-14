using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

public sealed class Qwen3VlHostFailureEnvelope
{
    public const string SupportedSchemaVersion =
        "visual-semantic-host-failure-1.4";
    public const string PreviousSupportedSchemaVersion =
        "visual-semantic-host-failure-1.3";
    public const string PriorSupportedSchemaVersion =
        "visual-semantic-host-failure-1.2";
    public const string FoundationalSupportedSchemaVersion =
        "visual-semantic-host-failure-1.1";
    public const string SupportedHostVersion = "0.5A.9";

    private readonly ReadOnlyCollection<string> _diagnostics;
    private readonly ReadOnlyCollection<
        Qwen3VlHostFailureRecoveryPoolLedgerEntry> _recoveryPoolLedger;

    internal Qwen3VlHostFailureEnvelope(
        string schemaVersion,
        Qwen3VlHostCommand command,
        Qwen3VlHostFailureStage stage,
        Qwen3VlHostFailureCase? @case,
        Qwen3VlHostFailureVideoArtifact? videoArtifact,
        Qwen3VlHostFailureTiming? timing,
        Qwen3VlHostFailureSampling sampling,
        Qwen3VlHostFailureGeneration? generation,
        Qwen3VlHostFailureGenerationWatchdog? generationWatchdog,
        Qwen3VlGroundedMemoryPolicyAudit? groundedMemoryPolicy,
        Qwen3VlHostFailureIdentity identity,
        Qwen3VlHostFailureDetails failure,
        DateTimeOffset createdAtUtc,
        string[] diagnostics,
        Qwen3VlHostFailureRecoveryPoolLedgerEntry[] recoveryPoolLedger)
    {
        SchemaVersion = schemaVersion;
        Command = command;
        Stage = stage;
        Case = @case;
        VideoArtifact = videoArtifact;
        Timing = timing;
        Sampling = sampling;
        Generation = generation;
        GenerationWatchdog = generationWatchdog;
        GroundedMemoryPolicy = groundedMemoryPolicy;
        Identity = identity;
        Failure = failure;
        CreatedAtUtc = createdAtUtc;
        _diagnostics = Array.AsReadOnly(diagnostics);
        _recoveryPoolLedger = Array.AsReadOnly(recoveryPoolLedger);
    }

    public string SchemaVersion { get; }

    public string HostVersion => SupportedHostVersion;

    public Qwen3VlHostCommand Command { get; }

    public Qwen3VlHostFailureStage Stage { get; }

    public Qwen3VlHostFailureCase? Case { get; }

    public Qwen3VlHostFailureVideoArtifact? VideoArtifact { get; }

    public Qwen3VlHostFailureTiming? Timing { get; }

    public Qwen3VlHostFailureSampling Sampling { get; }

    public Qwen3VlHostFailureGeneration? Generation { get; }

    public Qwen3VlHostFailureGenerationWatchdog? GenerationWatchdog { get; }

    public Qwen3VlGroundedMemoryPolicyAudit? GroundedMemoryPolicy { get; }

    public Qwen3VlHostFailureIdentity Identity { get; }

    public Qwen3VlHostFailureDetails Failure { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public IReadOnlyList<Qwen3VlHostFailureRecoveryPoolLedgerEntry>
        RecoveryPoolLedger => _recoveryPoolLedger;
}
