using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed class VisualSemanticBatchRequest
{
    private readonly ReadOnlyCollection<VisualSemanticRequest> _requests;

    public VisualSemanticBatchRequest(
        IEnumerable<VisualSemanticRequest> requests,
        VisualSemanticVideoInputPolicy videoPolicy)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(videoPolicy);
        VisualSemanticRequest[] snapshot = requests.ToArray();

        if (snapshot.Length == 0 ||
            snapshot.Any(static value => value is null) ||
            snapshot
                .GroupBy(static value => value.CaseId, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            snapshot
                .GroupBy(static value => value.CandidateId, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            snapshot.Any(
                value =>
                    value.Input.ReviewVideoDuration >
                    videoPolicy.MaximumReviewDuration) ||
            snapshot.Any(
                value =>
                    !string.Equals(
                        value.Prompt.Sha256,
                        snapshot[0].Prompt.Sha256,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        value.Model.ManifestSha256,
                        snapshot[0].Model.ManifestSha256,
                        StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "A visual-semantic batch requires ordered unique cases with one prompt and model identity.",
                nameof(requests));
        }

        _requests = Array.AsReadOnly(snapshot);
        VideoPolicy = videoPolicy;
    }

    public IReadOnlyList<VisualSemanticRequest> Requests => _requests;

    public VisualSemanticPromptManifest Prompt => _requests[0].Prompt;

    public VisualSemanticModelManifest Model => _requests[0].Model;

    public VisualSemanticVideoInputPolicy VideoPolicy { get; }
}
