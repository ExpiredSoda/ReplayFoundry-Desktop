using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed class VisualSemanticRequest
{
    public VisualSemanticRequest(
        string caseId,
        string caseHash,
        string sourceId,
        VisualSemanticInputManifest input,
        string candidateId,
        MomentOutputKind candidateMode,
        TimeSpan candidateStartRelative,
        TimeSpan candidateEndRelative,
        TimeSpan sourceAbsoluteOffset,
        VisualSemanticCompositionMetadata composition,
        VisualSemanticTranscriptContext transcript,
        VisualSemanticDeterministicSummary? deterministicSummary,
        VisualSemanticPromptManifest prompt,
        VisualSemanticModelManifest model)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(model);

        if (!Enum.IsDefined(candidateMode) ||
            candidateStartRelative < TimeSpan.Zero ||
            candidateEndRelative <= candidateStartRelative ||
            candidateEndRelative > input.ReviewVideoDuration ||
            sourceAbsoluteOffset < TimeSpan.Zero ||
            transcript.Spans.Any(
                span =>
                    span.ReviewRelativeEnd >
                    input.ReviewVideoDuration))
        {
            throw new ArgumentException(
                "A visual-semantic request requires bounded candidate and transcript timestamps.");
        }

        if (transcript.Policy ==
                VisualSemanticTranscriptContextPolicy.FullContextV1 &&
            (
                deterministicSummary is null ||
                deterministicSummary.CandidateDuration !=
                    candidateEndRelative - candidateStartRelative ||
                deterministicSummary.Mode != candidateMode ||
                deterministicSummary.EventNeighborhoodEnd >
                    input.ReviewVideoDuration
            ) ||
            transcript.Policy ==
                VisualSemanticTranscriptContextPolicy.VisualOnlyV1 &&
            deterministicSummary is not null)
        {
            throw new ArgumentException(
                "FullContextV1 requires a matching neutral summary; VisualOnlyV1 must omit it.",
                nameof(deterministicSummary));
        }

        EnsureReadable(input.ReviewVideoPath);
        CaseId = VisualSemanticContractText.Required(
            caseId,
            nameof(caseId),
            128);
        CaseHash = ModelArtifactManifest.Sha256Value(
            caseHash,
            nameof(caseHash));
        SourceId = VisualSemanticContractText.Required(
            sourceId,
            nameof(sourceId),
            128);
        Input = input;
        CandidateId = VisualSemanticContractText.Required(
            candidateId,
            nameof(candidateId),
            128);
        CandidateMode = candidateMode;
        CandidateStartRelative = candidateStartRelative;
        CandidateEndRelative = candidateEndRelative;
        SourceAbsoluteOffset = sourceAbsoluteOffset;
        Composition = composition;
        Transcript = transcript;
        DeterministicSummary = deterministicSummary;
        Prompt = prompt;
        Model = model;
    }

    public string CaseId { get; }

    public string CaseHash { get; }

    public string SourceId { get; }

    public VisualSemanticInputManifest Input { get; }

    public string CandidateId { get; }

    public MomentOutputKind CandidateMode { get; }

    public TimeSpan CandidateStartRelative { get; }

    public TimeSpan CandidateEndRelative { get; }

    public TimeSpan SourceAbsoluteOffset { get; }

    public VisualSemanticCompositionMetadata Composition { get; }

    public VisualSemanticTranscriptContext Transcript { get; }

    public VisualSemanticDeterministicSummary? DeterministicSummary { get; }

    public VisualSemanticPromptManifest Prompt { get; }

    public VisualSemanticModelManifest Model { get; }

    private static void EnsureReadable(string path)
    {
        try
        {
            using FileStream _ =
                new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  UnauthorizedAccessException)
        {
            throw new ArgumentException(
                "The bounded visual-semantic review video is not locally readable.",
                nameof(path),
                exception);
        }
    }
}
