using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed record Qwen3VlProbeResult(
    string SchemaVersion,
    string ModelRepository,
    string ModelRevision,
    string Device,
    string Backend,
    IReadOnlyDictionary<string, string> Packages);

internal sealed record Qwen3VlParsedCaseResult(
    VisualSemanticObservation Observation,
    TimeSpan Elapsed,
    VisualSemanticIdentityBindingAudit IdentityBindingAudit,
    VisualSemanticOutputNormalizationAudit?
        NormalizationAudit);

internal sealed record Qwen3VlParsedBatchResult(
    string Device,
    string Backend,
    long? PeakAllocatedGpuBytes,
    TimeSpan TotalElapsed,
    IReadOnlyList<Qwen3VlParsedCaseResult> Results,
    VisualSemanticGenerationManifest Generation,
    VisualSemanticExecutionTimingManifest ExecutionTiming);
