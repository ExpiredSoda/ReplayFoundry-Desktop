namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal enum Qwen3VlEditorialStructuredDecodingRepresentation
{
    JsonSchema,
}

internal static class Qwen3VlEditorialStructuredDecodingPolicy
{
    public const string Version =
        "visual-semantic-editorial-structured-decoding-1.7";
    public const string BackendName = "XGrammar";
    public const string BackendVersion = "0.2.2";
    public const string SourceTag = "v0.2.2";
    public const string SourceCommit =
        "4d145cc13d878c751ebeed36af1c013074be76bc";
    public const string WindowsWheelFileName =
        "xgrammar-0.2.2-cp311-cp311-win_amd64.whl";
    public const string WindowsWheelSha256 =
        "EEFB94F9DD84B0D79885943318B0FBF3E6FD23B86AE3DFE6D0E48F090F431E6B";
    public const string LicenseIdentifier = "Apache-2.0";
    public const string SchemaVersion =
        "visual-semantic-editorial-constrained-schema-1.7";
    public const string CudaMaskBackend = "torch_native";
    public const Qwen3VlEditorialStructuredDecodingRepresentation
        Representation =
            Qwen3VlEditorialStructuredDecodingRepresentation.JsonSchema;
    public const bool UnconstrainedFallbackPermitted = false;
    public const bool SemanticRepairPermitted = false;

    public static void RequireFrozen(
        string backendName,
        string backendVersion,
        string wheelFileName,
        string wheelSha256,
        string schemaVersion,
        Qwen3VlEditorialStructuredDecodingRepresentation representation,
        string cudaMaskBackend,
        bool unconstrainedFallbackPermitted,
        bool semanticRepairPermitted)
    {
        if (!string.Equals(
                backendName,
                BackendName,
                StringComparison.Ordinal) ||
            !string.Equals(
                backendVersion,
                BackendVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                wheelFileName,
                WindowsWheelFileName,
                StringComparison.Ordinal) ||
            !string.Equals(
                wheelSha256,
                WindowsWheelSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                schemaVersion,
                SchemaVersion,
                StringComparison.Ordinal) ||
            representation != Representation ||
            !string.Equals(
                cudaMaskBackend,
                CudaMaskBackend,
                StringComparison.Ordinal) ||
            unconstrainedFallbackPermitted ||
            semanticRepairPermitted)
        {
            throw new Qwen3VlInitializationException(
                "Prompt 2.6 structured decoding differs from the " +
                "capability-tested frozen policy.");
        }
    }
}
