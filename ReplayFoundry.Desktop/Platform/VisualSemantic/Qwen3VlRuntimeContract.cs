namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlRuntimeContract
{
    internal const string AdapterVersion = "0.5.9-research";
    internal const string ExecutionBackend = "transformers+torchcodec";
    internal const string HostVersion = "0.5A.9";
    internal const string RepositoryId = "Qwen/Qwen3-VL-4B-Instruct";
    internal const string LicenseIdentifier = "Apache-2.0";

    internal static IReadOnlyList<string> RequiredFfmpegLibraryPrefixes { get; } =
    [
        "avcodec-",
        "avdevice-",
        "avfilter-",
        "avformat-",
        "avutil-",
        "swresample-",
        "swscale-",
    ];
}
