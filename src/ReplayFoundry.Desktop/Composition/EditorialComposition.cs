using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record EditorialCompositionDependencies(
    LocalVisualReviewServices VisualReview,
    GenerationExperienceServices Experience,
    GenerationWorkspaceServices Workspace);

internal sealed record EditorialServices(
    ClipEditorialProfileSession ProfileSession,
    Qwen3VlGroundedMetadataGenerator? OwnedAiProvider,
    ClipEditorialMetadataGenerationService MetadataGenerator,
    GenerationEditorialMetadataService GenerationMetadata);

internal static class EditorialComposition
{
    internal const string AiProviderStartupFailureReason =
        "Local AI title writing could not start because Advanced AI is for " +
        "a different version of Replay Foundry. Update or repair Advanced " +
        "AI, restart Replay Foundry, and try again.";

    public static EditorialServices Create(
        EditorialCompositionDependencies dependencies)
    {
        var profileSession = new ClipEditorialProfileSession();
        Qwen3VlGroundedMetadataGenerator? aiProvider =
            dependencies.VisualReview.EditorialAiProvider;
        var metadataGenerator = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            aiProvider,
            dependencies.VisualReview.Materializer,
            dependencies.VisualReview.RuntimeCapabilities.EditorialAiUnavailableReason);
        var generationMetadata = new GenerationEditorialMetadataService(
            metadataGenerator,
            profileSession,
            dependencies.Experience.GameKnowledge,
            dependencies.Experience.VisualText);
        return new(
            profileSession,
            aiProvider,
            metadataGenerator,
            generationMetadata);
    }

    internal static Qwen3VlGroundedMetadataGenerator? TryCreateAiProvider(
        Qwen3VlQualifiedEditorialRuntime runtime)
    {
        return TryCreateAiProvider(
            runtime,
            out _);
    }

    internal static Qwen3VlGroundedMetadataGenerator? TryCreateAiProvider(
        Qwen3VlQualifiedEditorialRuntime runtime,
        out string? unavailableReason)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        try
        {
            Qwen3VlGroundedMetadataGenerator provider =
                new(runtime, new JsonEditorialWriterLearningStore());
            unavailableReason = null;
            return provider;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                IOException or
                InvalidDataException or
                UnauthorizedAccessException)
        {
            SafeDiagnosticTrace.Write(
                "Grounded editorial AI is unavailable",
                exception);
            unavailableReason = AiProviderStartupFailureReason;
            return null;
        }
    }
}
