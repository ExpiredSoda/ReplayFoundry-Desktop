using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed class VisualSemanticBatchResult
{
    private readonly ReadOnlyCollection<VisualSemanticResult> _results;
    private readonly ReadOnlyCollection<VisualSemanticWarning> _warnings;

    public VisualSemanticBatchResult(
        VisualSemanticBatchRequest request,
        IEnumerable<VisualSemanticResult> results,
        VisualSemanticExecutionManifest execution,
        VisualSemanticGenerationManifest generation,
        IEnumerable<VisualSemanticWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(generation);
        VisualSemanticResult[] resultSnapshot = results.ToArray();
        VisualSemanticWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (resultSnapshot.Length != request.Requests.Count ||
            resultSnapshot.Any(static value => value is null) ||
            warningSnapshot.Any(static value => value is null) ||
            !resultSnapshot
                .Select(static value => value.Request)
                .SequenceEqual(request.Requests) ||
            generation.Cases.Count != request.Requests.Count ||
            !generation.Cases
                .Select(static value => value.CaseId)
                .SequenceEqual(
                    request.Requests.Select(
                        static value => value.CaseId),
                    StringComparer.Ordinal) ||
            !generation.Cases
                .Select(static value => value.CandidateId)
                .SequenceEqual(
                    request.Requests.Select(
                        static value => value.CandidateId),
                    StringComparer.Ordinal) ||
            generation.Cases
                .Zip(
                    resultSnapshot,
                    static (telemetry, result) =>
                        result.NormalizationAudit is null ||
                        string.Equals(
                            telemetry.DecodedTextSha256,
                            result.NormalizationAudit
                                .RawGeneratedTextSha256,
                            StringComparison.OrdinalIgnoreCase))
                .Any(static value => !value))
        {
            throw new ArgumentException(
                "A visual-semantic batch result must preserve every request, generation case, and normalized raw-output identity in original order.",
                nameof(results));
        }

        Request = request;
        Execution = execution;
        Generation = generation;
        _results = Array.AsReadOnly(resultSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public VisualSemanticBatchRequest Request { get; }

    public IReadOnlyList<VisualSemanticResult> Results => _results;

    public VisualSemanticExecutionManifest Execution { get; }

    public VisualSemanticGenerationManifest Generation { get; }

    public IReadOnlyList<VisualSemanticWarning> Warnings => _warnings;
}
