using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureJsonReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureParserValidation;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

public sealed record Qwen3VlGroundedMemoryPolicyAudit(
    long TotalDeviceMemoryBytes,
    long StartupFreeMemoryBytes,
    long AllocatorLimitBytes,
    double AllocatorFraction,
    double? ObservedAllocatorFraction,
    int PreGenerationAdmissionCount,
    long? MinimumPreGenerationFreeDeviceMemoryBytes,
    long? LastPreGenerationFreeDeviceMemoryBytes,
    long? PeakAllocatedGpuBytes,
    long? PeakReservedGpuBytes,
    long? EndAllocatedGpuBytes,
    long? EndReservedGpuBytes,
    long? EndFreeDeviceMemoryBytes,
    string RuntimeOutcome,
    string? FailureReason,
    string? AttentionImplementation = null,
    string? SdpaBackend = null,
    bool SdpaBackendForced = false);

internal static class Qwen3VlGroundedMemoryPolicy
{
    internal const string Version = "grounded-editorial-cuda-memory-1.5";
    internal const string Sha256 =
        "732b33e80cb0e8a50c44f75b1f84e16aefe8044ae3cd88189b18a19e01e4220b";
    internal const string PreviousVersion =
        "grounded-editorial-cuda-memory-1.4";
    internal const string PreviousSha256 =
        "e220ba244f5956aa39e6a5b8ab91d21fef7aee6938a2e367f2068c823905e670";
    internal const string PriorVersion =
        "grounded-editorial-cuda-memory-1.3";
    internal const string PriorSha256 =
        "bad95d6adf572843a63510b3ce1fa68daa04f6e64cf513bc3a12ef51d60a732d";
    internal const string LegacyVersion =
        "grounded-editorial-cuda-memory-1.2";
    internal const string LegacySha256 =
        "bb378491d38acde8b48539941b82c8c40049af164162b5d06f4f916111ad7a4f";
    internal const string EarlierVersion =
        "grounded-editorial-cuda-memory-1.1";
    internal const string EarlierSha256 =
        "ee8b8f27b182d765543d1a00a2433ca890be6c9143ef706d6156f687e50d045d";
    internal const string OriginalVersion =
        "grounded-editorial-cuda-memory-1.0";
    internal const string OriginalSha256 =
        "b01a53bb8cb05c367ace6c7be7013efeba965be7e6301824d4a064b27567363c";
    internal const long ReservedAllocatorHeadroomBytes =
        3L * 1024 * 1024 * 1024;
    internal const long QualificationReferencePeakAllocatedBytes =
        11_705_485_312;
    internal const string QualificationReferenceArtifactName =
        "real-qwen-metadata-v1.6.json";
    internal const string QualificationReferenceArtifactSchema =
        "replayfoundry-editorial-metadata-real-quality-1.0";
    internal const string QualificationReferenceArtifactSha256 =
        "0EC7F4BE4DD3664091D6808176B2FEA36B7FE016B277422DACA00C4C9D28EC70";
    internal const long MinimumViableAllocatorLimitBytes =
        QualificationReferencePeakAllocatedBytes + 1;

    private static readonly string[] Fields =
    [
        "policyVersion", "policySha256", "cudaDeviceIndex",
        "cacheImplementation", "allocatorScope", "startupGate",
        "preGenerationGate", "totalDeviceMemoryBytes",
        "startupFreeMemoryBytes", "startupExternallyOccupiedMemoryBytes",
        "requiredStartupFreeMemoryBytes", "reservedAllocatorHeadroomBytes",
        "allocatorLimitBytes", "minimumViableAllocatorLimitBytes",
        "allocatorFraction", "observedAllocatorFraction",
        "qualificationReferencePeakAllocatedBytes",
        "qualificationReferenceArtifactName",
        "qualificationReferenceArtifactSchema",
        "qualificationReferenceArtifactSha256",
        "preGenerationAdmissionCount",
        "minimumPreGenerationFreeDeviceMemoryBytes",
        "lastPreGenerationFreeDeviceMemoryBytes", "peakAllocatedGpuBytes",
        "peakReservedGpuBytes", "endAllocatedGpuBytes",
        "endReservedGpuBytes", "endFreeDeviceMemoryBytes",
        "runtimeOutcome", "failureReason", "globalFreeMemoryGuaranteed",
        "cpuModelOffloadPermitted", "quantizationPermitted",
        "automaticFallbackPermitted",
    ];
    private static readonly string[] CurrentFields =
    [
        .. Fields,
        "attentionImplementation", "sdpaBackend", "sdpaBackendForced",
        "attentionFallbackPermitted",
    ];

    internal static Qwen3VlGroundedMemoryPolicyAudit Parse(
        JsonElement value,
        bool requireCompleted,
        long? expectedPeakAllocatedBytes = null,
        bool requireCurrentPolicy = false)
    {
        string policyVersion = Text(value, "policyVersion", "$", 128);
        string policyHash = Hash(value, "policySha256", "$");
        bool currentPolicy =
            policyVersion.Equals(Version, StringComparison.Ordinal) &&
            policyHash.Equals(Sha256, StringComparison.OrdinalIgnoreCase);
        bool previousPolicy =
            policyVersion.Equals(PreviousVersion, StringComparison.Ordinal) &&
            policyHash.Equals(PreviousSha256, StringComparison.OrdinalIgnoreCase);
        bool priorPolicy =
            policyVersion.Equals(PriorVersion, StringComparison.Ordinal) &&
            policyHash.Equals(PriorSha256, StringComparison.OrdinalIgnoreCase);
        bool legacyPolicy =
            policyVersion.Equals(LegacyVersion, StringComparison.Ordinal) &&
            policyHash.Equals(LegacySha256, StringComparison.OrdinalIgnoreCase);
        bool originalPolicy =
            policyVersion.Equals(OriginalVersion, StringComparison.Ordinal) &&
            policyHash.Equals(OriginalSha256, StringComparison.OrdinalIgnoreCase);
        bool earlierPolicy =
            policyVersion.Equals(EarlierVersion, StringComparison.Ordinal) &&
            policyHash.Equals(EarlierSha256, StringComparison.OrdinalIgnoreCase);
        bool attentionPolicy = currentPolicy || previousPolicy;
        Exact(
            value,
            "$.groundedMemoryPolicy",
            attentionPolicy ? CurrentFields : Fields);
        int device = Integer(value, "cudaDeviceIndex", "$");
        string cache = Text(value, "cacheImplementation", "$", 64);
        string? attentionImplementation = attentionPolicy
            ? Text(value, "attentionImplementation", "$", 64)
            : null;
        string? sdpaBackend = attentionPolicy
            ? Text(value, "sdpaBackend", "$", 64)
            : null;
        bool sdpaBackendForced = attentionPolicy &&
            Boolean(value, "sdpaBackendForced", "$");
        bool attentionFallbackPermitted = attentionPolicy &&
            Boolean(value, "attentionFallbackPermitted", "$");
        string allocatorScope = Text(value, "allocatorScope", "$", 128);
        string startupGate = Text(value, "startupGate", "$", 128);
        string preGenerationGate = Text(value, "preGenerationGate", "$", 128);
        long total = Int64(value, "totalDeviceMemoryBytes", "$");
        long startupFree = Int64(value, "startupFreeMemoryBytes", "$");
        long external = Int64(value, "startupExternallyOccupiedMemoryBytes", "$");
        long required = Int64(value, "requiredStartupFreeMemoryBytes", "$");
        long reserve = Int64(value, "reservedAllocatorHeadroomBytes", "$");
        long limit = Int64(value, "allocatorLimitBytes", "$");
        long minimum = Int64(value, "minimumViableAllocatorLimitBytes", "$");
        double fraction = Number(value, "allocatorFraction", "$");
        double? observed = NullableNumber(value, "observedAllocatorFraction", "$");
        long qualificationPeak = Int64(
            value, "qualificationReferencePeakAllocatedBytes", "$");
        string qualificationArtifactName = Text(
            value, "qualificationReferenceArtifactName", "$", 128);
        string qualificationArtifactSchema = Text(
            value, "qualificationReferenceArtifactSchema", "$", 128);
        string qualificationArtifactHash = Hash(
            value, "qualificationReferenceArtifactSha256", "$");
        int admissionCount = Integer(value, "preGenerationAdmissionCount", "$");
        long? minimumFree = NullableInt64(
            value, "minimumPreGenerationFreeDeviceMemoryBytes");
        long? lastFree = NullableInt64(
            value, "lastPreGenerationFreeDeviceMemoryBytes");
        long? peakAllocated = NullableInt64(value, "peakAllocatedGpuBytes");
        long? peakReserved = NullableInt64(value, "peakReservedGpuBytes");
        long? endAllocated = NullableInt64(value, "endAllocatedGpuBytes");
        long? endReserved = NullableInt64(value, "endReservedGpuBytes");
        long? endFree = NullableInt64(value, "endFreeDeviceMemoryBytes");
        string outcome = Text(value, "runtimeOutcome", "$", 64);
        string? failureReason = NullableText(value, "failureReason", "$", 128);

        bool baseValid = (currentPolicy || !requireCurrentPolicy &&
                (previousPolicy || priorPolicy || legacyPolicy ||
                    earlierPolicy || originalPolicy)) &&
            (!attentionPolicy ||
                attentionImplementation == "sdpa" &&
                sdpaBackend == "CudnnAttention" &&
                sdpaBackendForced && !attentionFallbackPermitted) &&
            device == 0 && cache.Equals("offloaded", StringComparison.Ordinal) &&
            allocatorScope.Equals(
                "PyTorchNativeCudaCachingAllocator", StringComparison.Ordinal) &&
            startupGate.Equals(
                "FreeMemoryMinusReserveExceedsQualificationPeak",
                StringComparison.Ordinal) &&
            preGenerationGate.Equals(
                "CurrentFreeMemoryAtLeastFixedReserve", StringComparison.Ordinal) &&
            total > 0 && startupFree >= 0 && startupFree <= total &&
            external == total - startupFree &&
            reserve == ReservedAllocatorHeadroomBytes &&
            qualificationPeak == QualificationReferencePeakAllocatedBytes &&
            qualificationArtifactName.Equals(
                QualificationReferenceArtifactName,
                StringComparison.Ordinal) &&
            qualificationArtifactSchema.Equals(
                QualificationReferenceArtifactSchema,
                StringComparison.Ordinal) &&
            qualificationArtifactHash.Equals(
                QualificationReferenceArtifactSha256,
                StringComparison.OrdinalIgnoreCase) &&
            minimum == MinimumViableAllocatorLimitBytes &&
            required == reserve + minimum && limit == startupFree - reserve &&
            Math.Abs(fraction - (double)limit / total) <= 1e-12 &&
            admissionCount >= 0 &&
            AllNonNegative(minimumFree, lastFree, peakAllocated, peakReserved,
                endAllocated, endReserved, endFree) &&
            (!minimumFree.HasValue || minimumFree <= total) &&
            (!lastFree.HasValue || lastFree <= total) &&
            (!endFree.HasValue || endFree <= total) &&
            (!minimumFree.HasValue || !lastFree.HasValue ||
                minimumFree <= lastFree) &&
            !Boolean(value, "globalFreeMemoryGuaranteed", "$") &&
            Boolean(value, "cpuModelOffloadPermitted", "$") ==
                (currentPolicy || previousPolicy || priorPolicy ||
                    legacyPolicy || earlierPolicy) &&
            !Boolean(value, "quantizationPermitted", "$") &&
            !Boolean(value, "automaticFallbackPermitted", "$") &&
            (!peakAllocated.HasValue || !peakReserved.HasValue ||
                peakAllocated <= peakReserved) &&
            (!peakReserved.HasValue || peakReserved <= limit) &&
            (!endAllocated.HasValue || !peakAllocated.HasValue ||
                endAllocated <= peakAllocated) &&
            (!endReserved.HasValue || !peakReserved.HasValue ||
                endReserved <= peakReserved) &&
            (!endAllocated.HasValue || !endReserved.HasValue ||
                endAllocated <= endReserved);
        if (!baseValid)
        {
            throw Failure("Grounded CUDA memory policy telemetry is invalid.");
        }

        bool observedExact = observed.HasValue &&
            Math.Abs(observed.Value - fraction) <= 1e-12;
        bool completed = outcome.Equals("Completed", StringComparison.Ordinal) &&
            failureReason is null && observedExact && admissionCount > 0 &&
            minimumFree >= reserve && lastFree >= reserve &&
            peakAllocated.HasValue && peakReserved.HasValue &&
            endAllocated.HasValue && endReserved.HasValue && endFree.HasValue &&
            (!requireCompleted ||
                expectedPeakAllocatedBytes.HasValue &&
                expectedPeakAllocatedBytes == peakAllocated);
        if (requireCompleted ? !completed : !ValidFailureOutcome(completed))
        {
            throw Failure("Grounded CUDA memory runtime outcome is invalid.");
        }

        return new Qwen3VlGroundedMemoryPolicyAudit(
            total, startupFree, limit, fraction, observed, admissionCount,
            minimumFree, lastFree, peakAllocated, peakReserved, endAllocated,
            endReserved, endFree, outcome, failureReason,
            attentionImplementation, sdpaBackend, sdpaBackendForced);

        bool ValidFailureOutcome(bool validCompleted) => outcome switch
        {
            "Configured" => failureReason is null && admissionCount == 0 &&
                minimumFree is null && lastFree is null &&
                peakAllocated is null && peakReserved is null &&
                endAllocated is null && endReserved is null && endFree is null &&
                (observed is null || observedExact),
            "GenerationAdmitted" => failureReason is null && observedExact &&
                admissionCount > 0 && minimumFree >= reserve && lastFree >= reserve &&
                peakAllocated is null && peakReserved is null &&
                endAllocated is null && endReserved is null && endFree is null,
            "StartupAdmissionRejected" =>
                failureReason == "InsufficientStartupFreeMemory" &&
                observed is null && admissionCount == 0 && limit < minimum &&
                minimumFree is null && lastFree is null &&
                peakAllocated is null && peakReserved is null &&
                endAllocated is null && endReserved is null && endFree is null,
            "PreGenerationAdmissionRejected" =>
                observedExact &&
                (failureReason == "InsufficientPreGenerationFreeMemory" ||
                    failureReason == "AllocatorLimitExceeded") &&
                minimumFree.HasValue && lastFree.HasValue &&
                peakAllocated is null && peakReserved is null &&
                endAllocated is null && endReserved is null && endFree is null,
            "CudaAllocatorOutOfMemory" => observedExact &&
                failureReason == "CudaAllocatorOutOfMemory",
            "Completed" => validCompleted,
            _ => false,
        };
    }

    internal static Qwen3VlGroundedMemoryPolicyAudit? ParseNullableFailure(
        JsonElement root)
    {
        JsonElement value = Property(root, "groundedMemoryPolicy", "$");
        return value.ValueKind == JsonValueKind.Null
            ? null
            : Parse(value, requireCompleted: false);
    }

    private static long? NullableInt64(JsonElement value, string name) =>
        Property(value, name, "$").ValueKind == JsonValueKind.Null
            ? null
            : Int64(value, name, "$");

    private static bool AllNonNegative(params long?[] values) =>
        values.All(static value => !value.HasValue || value.Value >= 0);
}
