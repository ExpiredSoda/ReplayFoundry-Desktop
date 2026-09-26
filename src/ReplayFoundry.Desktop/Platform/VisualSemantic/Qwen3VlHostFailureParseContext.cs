using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed record Qwen3VlHostFailureSubmittedCase(
    string CaseId,
    string CandidateId,
    VisualSemanticInputManifest Input,
    TimeSpan SourceAbsoluteOffset,
    TimeSpan CandidateStartRelative,
    TimeSpan CandidateEndRelative);

internal sealed class Qwen3VlHostFailureParseContext
{
    private Qwen3VlHostFailureParseContext(
        IReadOnlyList<Qwen3VlHostFailureSubmittedCase> cases,
        string modelManifestSha256,
        string promptSha256)
    {
        Cases = cases;
        ModelManifestSha256 = modelManifestSha256;
        PromptSha256 = promptSha256;
    }

    public IReadOnlyList<Qwen3VlHostFailureSubmittedCase> Cases { get; }

    public string ModelManifestSha256 { get; }

    public string PromptSha256 { get; }

    public static Qwen3VlHostFailureParseContext FromBatchRequest(
        VisualSemanticBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Qwen3VlHostFailureParseContext(
            request.Requests.Select(static value =>
                new Qwen3VlHostFailureSubmittedCase(
                    value.CaseId,
                    value.CandidateId,
                    value.Input,
                    value.SourceAbsoluteOffset,
                    value.CandidateStartRelative,
                    value.CandidateEndRelative)).ToArray(),
            request.Model.ManifestSha256,
            request.Prompt.Sha256);
    }

    public static Qwen3VlHostFailureParseContext FromGroundedMetadata(
        IReadOnlyList<ClipEditorialMetadataRequest> requests,
        Qwen3VlQualifiedEditorialRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(runtime);
        if (requests.Count == 0 ||
            requests.Any(static value => value is null ||
                value.ReviewVideo is null))
        {
            throw new ArgumentException(
                "Grounded metadata failure validation requires every submitted review video.",
                nameof(requests));
        }

        return new Qwen3VlHostFailureParseContext(
            requests.Select(static value =>
                new Qwen3VlHostFailureSubmittedCase(
                    value.Context.CandidateId,
                    value.Context.CandidateId,
                    value.ReviewVideo!,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    value.ReviewVideo!.ReviewVideoDuration)).ToArray(),
            runtime.Model.ManifestSha256,
            Qwen3VlGroundedMetadataGenerator.PromptSha256);
    }
}
