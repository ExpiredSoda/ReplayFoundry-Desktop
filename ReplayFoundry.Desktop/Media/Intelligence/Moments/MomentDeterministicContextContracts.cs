using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Enrichment;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Media.Intelligence.Moments;

public sealed class MomentDeterministicCandidateSnapshot
{
    private readonly ReadOnlyCollection<
        MomentDeterministicScoreComponentSnapshot> _scoreComponents;
    private readonly ReadOnlyCollection<string> _evidenceReferenceIds;

    public MomentDeterministicCandidateSnapshot(
        string id,
        TimeSpan start,
        TimeSpan end,
        TimeSpan sourceDuration,
        string outputMode,
        double heuristicScore,
        string disposition,
        int proposalOrder,
        int? selectedRank,
        bool isSelected,
        IEnumerable<MomentDeterministicScoreComponentSnapshot>
            scoreComponents,
        IEnumerable<string> evidenceReferenceIds,
        string deterministicPayloadJson,
        string deterministicPayloadSha256)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            sourceDuration <= TimeSpan.Zero ||
            start < TimeSpan.Zero ||
            end <= start ||
            end > sourceDuration ||
            string.IsNullOrWhiteSpace(outputMode) ||
            !double.IsFinite(heuristicScore) ||
            heuristicScore is < 0 or > 100 ||
            string.IsNullOrWhiteSpace(disposition) ||
            proposalOrder < 0 ||
            selectedRank is <= 0 ||
            isSelected != selectedRank.HasValue ||
            string.IsNullOrWhiteSpace(deterministicPayloadJson))
        {
            throw new ArgumentException(
                "A deterministic candidate snapshot requires a complete, bounded, internally consistent candidate.");
        }

        ArgumentNullException.ThrowIfNull(scoreComponents);
        ArgumentNullException.ThrowIfNull(evidenceReferenceIds);

        MomentDeterministicScoreComponentSnapshot[] componentSnapshot =
            scoreComponents.ToArray();
        string[] referenceSnapshot =
            evidenceReferenceIds
                .Select(Required)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();

        if (componentSnapshot.Any(static item => item is null) ||
            componentSnapshot
                .GroupBy(static item => item.Code, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Candidate score components must be non-null and unique.",
                nameof(scoreComponents));
        }

        string expectedPayloadHash =
            HashText(deterministicPayloadJson);

        if (!string.Equals(
                expectedPayloadHash,
                NormalizeSha256(deterministicPayloadSha256),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The deterministic payload hash does not match its JSON.",
                nameof(deterministicPayloadSha256));
        }

        Id = id.Trim();
        Start = start;
        End = end;
        SourceDuration = sourceDuration;
        OutputMode = outputMode.Trim();
        HeuristicScore = heuristicScore;
        Disposition = disposition.Trim();
        ProposalOrder = proposalOrder;
        SelectedRank = selectedRank;
        IsSelected = isSelected;
        DeterministicPayloadJson = deterministicPayloadJson;
        DeterministicPayloadSha256 = expectedPayloadHash;
        _scoreComponents = Array.AsReadOnly(componentSnapshot);
        _evidenceReferenceIds = Array.AsReadOnly(referenceSnapshot);
    }

    public string Id { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration => End - Start;

    public TimeSpan SourceDuration { get; }

    public string OutputMode { get; }

    public double HeuristicScore { get; }

    public string Disposition { get; }

    public int ProposalOrder { get; }

    public int? SelectedRank { get; }

    public bool IsSelected { get; }

    public IReadOnlyList<MomentDeterministicScoreComponentSnapshot>
        ScoreComponents =>
        _scoreComponents;

    public IReadOnlyList<string> EvidenceReferenceIds =>
        _evidenceReferenceIds;

    public string DeterministicPayloadJson { get; }

    public string DeterministicPayloadSha256 { get; }

    public static string HashText(string value) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)));

    public static string NormalizeSha256(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 64 ||
            value.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A SHA-256 value must contain exactly 64 hexadecimal characters.");
        }

        return value.ToUpperInvariant();
    }

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Snapshot text cannot be blank.")
            : value.Trim();
}

public sealed class MomentDeterministicContext
{
    private readonly ReadOnlyCollection<
        MomentDeterministicCandidateSnapshot> _candidates;
    private readonly ReadOnlyCollection<string> _warnings;

    public MomentDeterministicContext(
        string sourcePath,
        TimeSpan sourceDuration,
        string finderName,
        string finderVersion,
        string policyHash,
        string outputMode,
        IEnumerable<MomentDeterministicCandidateSnapshot> candidates,
        IEnumerable<string>? warnings = null,
        string? inputArtifactSha256 = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !Path.IsPathFullyQualified(sourcePath) ||
            sourceDuration <= TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(finderName) ||
            string.IsNullOrWhiteSpace(finderVersion) ||
            string.IsNullOrWhiteSpace(policyHash) ||
            string.IsNullOrWhiteSpace(outputMode))
        {
            throw new ArgumentException(
                "Deterministic context requires source, finder, policy, and output-mode identity.");
        }

        ArgumentNullException.ThrowIfNull(candidates);

        MomentDeterministicCandidateSnapshot[] candidateSnapshot =
            candidates
                .OrderBy(static candidate => candidate.ProposalOrder)
                .ToArray();
        string[] warningSnapshot =
            warnings?.Select(Required).ToArray() ??
            [];

        if (candidateSnapshot.Length == 0 ||
            candidateSnapshot.Any(static candidate => candidate is null) ||
            candidateSnapshot.Any(
                candidate =>
                    candidate.SourceDuration != sourceDuration ||
                    !string.Equals(
                        candidate.OutputMode,
                        outputMode,
                        StringComparison.Ordinal)) ||
            candidateSnapshot
                .GroupBy(static candidate => candidate.Id, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            candidateSnapshot
                .Select(static candidate => candidate.ProposalOrder)
                .Distinct()
                .Count() != candidateSnapshot.Length ||
            candidateSnapshot
                .Where(static candidate => candidate.IsSelected)
                .Select(static candidate => candidate.SelectedRank!.Value)
                .OrderBy(static rank => rank)
                .Where((rank, index) => rank != index + 1)
                .Any())
        {
            throw new ArgumentException(
                "Deterministic candidates must be complete, unique, ordered, and bound to the source.",
                nameof(candidates));
        }

        SourcePath = Path.GetFullPath(sourcePath);
        SourceDuration = sourceDuration;
        FinderName = finderName.Trim();
        FinderVersion = finderVersion.Trim();
        PolicyHash = policyHash.Trim();
        OutputMode = outputMode.Trim();
        InputArtifactSha256 =
            inputArtifactSha256 is null
                ? null
                : MomentDeterministicCandidateSnapshot.NormalizeSha256(
                    inputArtifactSha256);
        _candidates = Array.AsReadOnly(candidateSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string SourcePath { get; }

    public TimeSpan SourceDuration { get; }

    public string FinderName { get; }

    public string FinderVersion { get; }

    public string PolicyHash { get; }

    public string OutputMode { get; }

    public string? InputArtifactSha256 { get; }

    public IReadOnlyList<MomentDeterministicCandidateSnapshot>
        Candidates =>
        _candidates;

    public IReadOnlyList<string> Warnings => _warnings;

    public static MomentDeterministicContext FromResult(
        MediaMomentFindingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var selectedRanks =
            result.SelectedCandidates
                .Select(
                    (candidate, index) =>
                        new
                        {
                            candidate.Id,
                            Rank = index + 1,
                        })
                .ToDictionary(
                    static item => item.Id,
                    static item => item.Rank,
                    StringComparer.Ordinal);
        string outputMode =
            result.Request.Options.OutputKind.ToString();

        MomentDeterministicCandidateSnapshot[] candidates =
            result.Proposals
                .Select(
                    (candidate, index) =>
                    {
                        var payload =
                            new
                            {
                                candidate.Id,
                                candidate.Window,
                                candidate.ConstructionReason,
                                candidate.EventNeighborhoodId,
                                candidate.EpisodeId,
                                candidate.HeuristicScore,
                                candidate.Disposition,
                                score = candidate.Score,
                                candidate.Anchors,
                                candidate.IntegrityEvidenceReferences,
                            };
                        string json =
                            JsonSerializer.Serialize(payload);
                        string[] evidenceIds =
                            candidate.Score.Components
                                .SelectMany(
                                    static component =>
                                        component.EvidenceReferences)
                                .Concat(
                                    candidate.IntegrityEvidenceReferences)
                                .Select(CreateEvidenceReferenceId)
                                .Distinct(StringComparer.Ordinal)
                                .ToArray();
                        MomentDeterministicScoreComponentSnapshot[] components =
                            candidate.Score.Components
                                .Select(
                                    component =>
                                        new MomentDeterministicScoreComponentSnapshot(
                                            component.Code.ToString(),
                                            component.RawMeasuredValue,
                                            component.NormalizedValue,
                                            component.ConfiguredSignedWeight,
                                            component.SignedContribution,
                                            component.Explanation,
                                            component.EvidenceReferences.Select(
                                                CreateEvidenceReferenceId)))
                                .ToArray();

                        return new MomentDeterministicCandidateSnapshot(
                            candidate.Id,
                            candidate.Window.Start,
                            candidate.Window.End,
                            candidate.Window.SourceDuration,
                            outputMode,
                            candidate.HeuristicScore,
                            candidate.Disposition.ToString(),
                            index,
                            selectedRanks.GetValueOrDefault(candidate.Id),
                            selectedRanks.ContainsKey(candidate.Id),
                            components,
                            evidenceIds,
                            json,
                            MomentDeterministicCandidateSnapshot.HashText(json));
                    })
                .ToArray();

        return new MomentDeterministicContext(
            result.Request.Media.FullPath,
            result.Request.Media.Duration,
            result.Manifest.FinderIdentity.Name,
            result.Manifest.FinderIdentity.Version,
            result.Manifest.PolicyHash,
            outputMode,
            candidates,
            result.Warnings.Select(
                warning => $"{warning.Code}: {warning.Message}"));
    }

    private static string CreateEvidenceReferenceId(
        MomentEvidenceReference reference) =>
        TranscriptSpan.StableId(
            "evidence",
            reference.Kind.ToString(),
            reference.Start.Ticks.ToString(),
            reference.End.Ticks.ToString(),
            reference.SourceDescription,
            reference.VisualTargetKey ?? string.Empty,
            reference.AudioStreamIndex?.ToString() ?? string.Empty);

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Context text cannot be blank.")
            : value.Trim();
}
