using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed record VisualSemanticResult
{
    private readonly ReadOnlyCollection<VisualSemanticWarning> _warnings;

    public VisualSemanticResult(
        VisualSemanticRequest request,
        VisualSemanticObservation observation,
        TimeSpan elapsed,
        IEnumerable<VisualSemanticWarning>? warnings = null,
        VisualSemanticOutputNormalizationAudit?
            normalizationAudit = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observation);

        if (elapsed < TimeSpan.Zero ||
            !string.Equals(
                request.CaseId,
                observation.CaseId,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.CandidateId,
                observation.CandidateId,
                StringComparison.Ordinal) ||
            observation.EvidenceIntervals.Any(
                interval =>
                    interval.End >
                    request.Input.ReviewVideoDuration))
        {
            throw new ArgumentException(
                "A visual-semantic result must match its request and remain inside review media.");
        }

        VisualSemanticWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (warningSnapshot.Any(static value => value is null) ||
            warningSnapshot.Any(
                value =>
                    value.CaseId is not null &&
                    !string.Equals(
                        value.CaseId,
                        request.CaseId,
                        StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Result warnings must be non-null and owned by the result case.",
                nameof(warnings));
        }

        if (normalizationAudit is not null &&
            !string.Equals(
                normalizationAudit.CaseId,
                request.CaseId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A normalization audit must be owned by the result case.",
                nameof(normalizationAudit));
        }

        Request = request;
        Observation = observation;
        Elapsed = elapsed;
        NormalizationAudit = normalizationAudit;
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public VisualSemanticRequest Request { get; }

    public VisualSemanticObservation Observation { get; }

    public TimeSpan Elapsed { get; }

    public VisualSemanticOutputNormalizationAudit?
        NormalizationAudit
    { get; }

    public IReadOnlyList<VisualSemanticWarning> Warnings => _warnings;
}
