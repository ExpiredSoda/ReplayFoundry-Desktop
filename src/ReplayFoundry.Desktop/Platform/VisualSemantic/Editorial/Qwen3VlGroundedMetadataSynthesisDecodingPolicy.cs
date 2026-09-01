namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataSynthesisDecodingPolicy
{
    internal const string Version =
        "grounded-editorial-sampled-synthesis-1.0";
    internal const string Sha256 =
        "9484C8DAFAAFDD7C839A867CC45F58520FFD623676C6846E1F66820FA5E83930";
    internal const string Trigger =
        "DuplicateGreedyRecoveryActivated";
    internal const int PassOrdinal = 4;
    internal const int BatchSize = 1;
    internal const bool DoSample = true;
    internal const int NumberOfBeams = 1;
    internal const bool UseCache = true;
    internal const int Seed = 3407;
    internal const double Temperature = 0.7;
    internal const double TopP = 0.8;
    internal const int TopK = 20;
    internal const bool UnconstrainedFallbackPermitted = false;
    internal const bool SemanticRepairPermitted = false;
}
