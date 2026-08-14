using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;

namespace ReplayFoundry.Desktop.Features.Generate.Handoff;

public sealed class GenerationOutputProject
{
    private readonly ReadOnlyCollection<GenerationOutputAsset> _assets;
    private readonly ReadOnlyCollection<GenerationHiddenMoment> _hiddenMoments;

    public GenerationOutputProject(
        string id,
        GenerationMode mode,
        string outputDirectory,
        int requestedCount,
        ClipFulfillmentPreference fulfillmentPreference,
        GenerationClipFulfillmentOutcome fulfillmentOutcome,
        IEnumerable<GenerationOutputAsset> assets,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? finalizedAtUtc = null,
        GenerationResultCountMode resultCountMode =
            GenerationResultCountMode.Exact,
        IEnumerable<GenerationHiddenMoment>? hiddenMoments = null,
        string? candidateSetFingerprint = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A generated project requires an identifier.",
                nameof(id));
        }
        if (!Enum.IsDefined(mode) ||
            !Enum.IsDefined(fulfillmentPreference) ||
            !Enum.IsDefined(fulfillmentOutcome))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (!Path.IsPathFullyQualified(outputDirectory))
        {
            throw new ArgumentException(
                "A generated project requires a fully qualified output directory.",
                nameof(outputDirectory));
        }
        if (requestedCount <= 0 ||
            createdAtUtc.Offset != TimeSpan.Zero ||
            finalizedAtUtc.HasValue &&
            (finalizedAtUtc.Value.Offset != TimeSpan.Zero ||
             finalizedAtUtc.Value < createdAtUtc) ||
            !Enum.IsDefined(resultCountMode) ||
            resultCountMode == GenerationResultCountMode.Auto &&
            requestedCount != 30)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedCount));
        }
        ArgumentNullException.ThrowIfNull(assets);
        GenerationOutputAsset[] snapshot = assets.ToArray();
        if (snapshot.Length == 0 ||
            snapshot.Any(static asset => asset is null) ||
            snapshot.Select(static asset => asset.Id)
                .Distinct(StringComparer.Ordinal).Count() != snapshot.Length ||
            !snapshot.Select(static asset => asset.Rank)
                .SequenceEqual(Enumerable.Range(1, snapshot.Length)))
        {
            throw new ArgumentException(
                "Generated project assets must be nonempty, unique, and rank ordered.",
                nameof(assets));
        }
        bool finalizationIsConsistent = finalizedAtUtc.HasValue
            ? snapshot.Any(static asset => asset.IsIncludedInFinalRender) &&
              snapshot.All(static asset =>
                  asset.IsIncludedInFinalRender == asset.IsRendered)
            : snapshot.All(static asset => !asset.IsRendered);
        if (!finalizationIsConsistent)
        {
            throw new ArgumentException(
                "A Studio project must be an unrendered draft or a finalized project whose included candidates alone are rendered.",
                nameof(assets));
        }

        GenerationHiddenMoment[] hiddenSnapshot =
            hiddenMoments?.ToArray() ?? [];
        if (hiddenSnapshot.Any(static value => value is null) ||
            hiddenSnapshot.Select(static value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() != hiddenSnapshot.Length ||
            hiddenSnapshot.Any(hidden => snapshot.Any(asset =>
                asset.Id.Equals(hidden.Id, StringComparison.Ordinal))))
        {
            throw new ArgumentException(
                "Hidden moments must be nonnull, unique, and separate from Studio assets.",
                nameof(hiddenMoments));
        }

        Id = id.Trim();
        Mode = mode;
        OutputDirectory = Path.GetFullPath(outputDirectory);
        RequestedCount = requestedCount;
        FulfillmentPreference = fulfillmentPreference;
        FulfillmentOutcome = fulfillmentOutcome;
        _assets = Array.AsReadOnly(snapshot);
        _hiddenMoments = Array.AsReadOnly(hiddenSnapshot);
        CreatedAtUtc = createdAtUtc;
        FinalizedAtUtc = finalizedAtUtc;
        ResultCountMode = resultCountMode;
        CandidateSetFingerprint = string.IsNullOrWhiteSpace(
            candidateSetFingerprint)
                ? CreateCandidateSetFingerprint(mode, snapshot)
                : candidateSetFingerprint.Trim();
    }

    public string Id { get; }
    public GenerationMode Mode { get; }
    public string OutputDirectory { get; }
    public int RequestedCount { get; }
    public int SelectedCount => _assets.Count;
    public bool IsRequestedCountMet =>
        ResultCountMode == GenerationResultCountMode.Auto ||
        SelectedCount >= RequestedCount;
    public GenerationResultCountMode ResultCountMode { get; }
    public ClipFulfillmentPreference FulfillmentPreference { get; }
    public GenerationClipFulfillmentOutcome FulfillmentOutcome { get; }
    public IReadOnlyList<GenerationOutputAsset> Assets => _assets;
    public IReadOnlyList<GenerationHiddenMoment> HiddenMoments =>
        _hiddenMoments;
    public int HiddenMomentCount => _hiddenMoments.Count;
    public IReadOnlyList<GenerationOutputAsset> IncludedAssets =>
        _assets.Where(static asset => asset.IsIncludedInFinalRender).ToArray();
    public IReadOnlyList<GenerationOutputAsset> ExcludedAssets =>
        _assets.Where(static asset => !asset.IsIncludedInFinalRender).ToArray();
    public int IncludedCount => _assets.Count(static asset => asset.IsIncludedInFinalRender);
    public int ExcludedCount => _assets.Count - IncludedCount;
    public bool HasPublishReadyEditorialMetadata =>
        IncludedAssets.All(static asset =>
            asset.EditorialMetadata?.IsPublishReady == true &&
            asset.IsEditorialMetadataCurrentForCut);
    public GenerationOutputAsset PrimaryAsset => _assets[0];
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? FinalizedAtUtc { get; }
    public bool IsFinalized => FinalizedAtUtc is not null;
    public string CandidateSetFingerprint { get; }

    internal GenerationOutputProject ReopenAsDraft()
    {
        if (!IsFinalized)
        {
            return this;
        }

        string revisionToken = Guid.NewGuid().ToString("N");

        GenerationOutputAsset[] editableAssets = _assets
            .Select(static asset => asset.WithStudioEdits(
                asset.SourceStart,
                asset.SourceEnd,
                asset.Appearance))
            .ToArray();
        return new GenerationOutputProject(
            $"{Id}-revision-{revisionToken}",
            Mode,
            FindAvailableRevisionOutputDirectory(revisionToken),
            RequestedCount,
            FulfillmentPreference,
            FulfillmentOutcome,
            editableAssets,
            CreatedAtUtc,
            resultCountMode: ResultCountMode,
            hiddenMoments: HiddenMoments,
            candidateSetFingerprint: CandidateSetFingerprint);
    }

    internal GenerationOutputProject CreateRenderBatch(string renderToken)
    {
        if (IsFinalized)
        {
            throw new InvalidOperationException(
                "A finalized project cannot create another render batch.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(renderToken);
        string normalizedToken = new(
            renderToken.Where(char.IsAsciiLetterOrDigit).ToArray());
        if (normalizedToken.Length < 8)
        {
            throw new ArgumentException(
                "A render batch token must contain at least eight letters or digits.",
                nameof(renderToken));
        }

        string token = normalizedToken.ToLowerInvariant();
        return new GenerationOutputProject(
            $"{Id}-render-{token}",
            Mode,
            FindAvailableRenderOutputDirectory(token),
            RequestedCount,
            FulfillmentPreference,
            FulfillmentOutcome,
            Assets,
            CreatedAtUtc,
            resultCountMode: ResultCountMode,
            hiddenMoments: HiddenMoments,
            candidateSetFingerprint: CandidateSetFingerprint);
    }

    private string FindAvailableRevisionOutputDirectory(
        string revisionToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionToken);
        string normalized = Path.TrimEndingDirectorySeparator(OutputDirectory);
        string? parent = Path.GetDirectoryName(normalized);
        string name = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "A finalized Studio project has no valid revision directory.");
        }

        // The final renderer requires a nonexistent destination directory.
        // A per-reopen token therefore reserves identity without creating an
        // empty folder that would make rendering fail. It also prevents two
        // abandoned in-memory revisions from targeting the same path.
        string candidate = Path.Combine(
            parent,
            $"{name}-revision-{revisionToken[..8]}");
        if (Directory.Exists(candidate) || File.Exists(candidate))
        {
            throw new IOException(
                "Replay Foundry could not reserve a unique Studio revision directory.");
        }
        return candidate;
    }

    private string FindAvailableRenderOutputDirectory(string renderToken)
    {
        string normalized = Path.TrimEndingDirectorySeparator(OutputDirectory);
        string? parent = Path.GetDirectoryName(normalized);
        string name = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "The Studio project has no valid render output directory.");
        }

        string candidate = Path.Combine(
            parent,
            $"{name}-render-{renderToken[..8]}");
        if (Directory.Exists(candidate) || File.Exists(candidate))
        {
            throw new IOException(
                "Replay Foundry could not reserve a unique render output directory.");
        }
        return candidate;
    }

    internal GenerationOutputProject ReplaceAsset(
        GenerationOutputAsset replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        int index = _assets
            .Select(static asset => asset.Id)
            .ToList()
            .FindIndex(
                id => string.Equals(
                    id,
                    replacement.Id,
                    StringComparison.Ordinal));
        if (index < 0 ||
            replacement.Rank != _assets[index].Rank ||
            !replacement.SourceFullPath.Equals(
                _assets[index].SourceFullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A replacement must preserve the current asset identity, rank, and source.",
                nameof(replacement));
        }

        GenerationOutputAsset[] assets = _assets.ToArray();
        assets[index] = replacement;
        if (IsFinalized)
        {
            throw new InvalidOperationException(
                "A finalized project cannot accept additional Studio edits.");
        }

        return new GenerationOutputProject(
            Id,
            Mode,
            OutputDirectory,
            RequestedCount,
            FulfillmentPreference,
            FulfillmentOutcome,
            assets,
            CreatedAtUtc,
            resultCountMode: ResultCountMode,
            hiddenMoments: HiddenMoments,
            candidateSetFingerprint: CandidateSetFingerprint);
    }

    internal GenerationOutputProject ReplaceAssets(
        IReadOnlyList<GenerationOutputAsset> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        if (replacements.Count == 0 ||
            replacements.Any(static value => value is null) ||
            replacements.Select(static value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() != replacements.Count)
        {
            throw new ArgumentException(
                "A Studio batch edit requires at least one unique replacement.",
                nameof(replacements));
        }

        GenerationOutputProject current = this;
        foreach (GenerationOutputAsset replacement in replacements)
        {
            current = current.ReplaceAsset(replacement);
        }
        return current;
    }

    internal GenerationOutputProject AcceptHiddenMoment(
        string hiddenMomentId,
        GenerationCandidateCaptionTrack? captions = null,
        ClipEditorialContext? editorialContext = null,
        ClipEditorialMetadataDraft? editorialMetadata = null)
    {
        if (IsFinalized)
        {
            throw new InvalidOperationException(
                "A finalized Studio project cannot accept another moment.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(hiddenMomentId);
        GenerationHiddenMoment hidden = _hiddenMoments.SingleOrDefault(
            value => value.Id.Equals(hiddenMomentId, StringComparison.Ordinal)) ??
            throw new ArgumentException(
                "The hidden moment does not belong to this project.",
                nameof(hiddenMomentId));
        if (captions is not null &&
            !captions.CandidateId.Equals(hidden.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Hidden-moment captions must belong to the accepted candidate.",
                nameof(captions));
        }
        if ((editorialContext is null) != (editorialMetadata is null) ||
            editorialContext is not null &&
            !editorialContext.CandidateId.Equals(
                hidden.Id,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Accepted Hidden Moment metadata must be complete and belong to the same candidate.",
                nameof(editorialContext));
        }
        var accepted = new GenerationOutputAsset(
            hidden.Id,
            _assets.Count + 1,
            hidden.SourceMedia,
            outputFullPath: null,
            hidden.SourceStart,
            hidden.SourceEnd,
            hidden.FinalScore,
            hidden.QualityTarget,
            GenerationCandidateSelectionReason.HiddenMomentRecovery,
            hidden.Explanation,
            captions?.HasRenderableSegments == true
                ? captions.ToStudioHandoff()
                : null,
            editorialContext: editorialContext ?? hidden.EditorialContext,
            editorialMetadata: editorialMetadata ?? hidden.EditorialMetadata,
            preferenceFeatures: hidden.PreferenceFeatures);

        return new GenerationOutputProject(
            Id,
            Mode,
            OutputDirectory,
            RequestedCount,
            FulfillmentPreference,
            FulfillmentOutcome,
            [.. _assets, accepted],
            CreatedAtUtc,
            resultCountMode: ResultCountMode,
            hiddenMoments: _hiddenMoments.Where(value =>
                !value.Id.Equals(hiddenMomentId, StringComparison.Ordinal)),
            candidateSetFingerprint: CandidateSetFingerprint);
    }

    internal GenerationOutputProject Finalize(
        IEnumerable<GenerationOutputAsset> renderedAssets,
        DateTimeOffset finalizedAtUtc)
    {
        if (IsFinalized)
        {
            throw new InvalidOperationException(
                "The Studio project is already finalized.");
        }
        ArgumentNullException.ThrowIfNull(renderedAssets);
        GenerationOutputAsset[] snapshot = renderedAssets.ToArray();
        GenerationOutputAsset[] expected = _assets
            .Where(static asset => asset.IsIncludedInFinalRender)
            .ToArray();
        if (expected.Length == 0 ||
            snapshot.Length != expected.Length ||
            snapshot.Any(static asset => !asset.IsRendered) ||
            snapshot.Where((asset, index) =>
                    !asset.Id.Equals(
                        expected[index].Id,
                        StringComparison.Ordinal) ||
                    asset.Rank != expected[index].Rank ||
                    !asset.SourceFullPath.Equals(
                        expected[index].SourceFullPath,
                        StringComparison.OrdinalIgnoreCase))
                .Any())
        {
            throw new ArgumentException(
                "Finalized assets must preserve every included draft identity, rank, and source in order.",
                nameof(renderedAssets));
        }

        var renderedById = snapshot.ToDictionary(
            static asset => asset.Id,
            StringComparer.Ordinal);
        GenerationOutputAsset[] finalizedAssets = _assets
            .Select(asset => asset.IsIncludedInFinalRender
                ? renderedById[asset.Id]
                : asset)
            .ToArray();

        return new GenerationOutputProject(
            Id,
            Mode,
            OutputDirectory,
            RequestedCount,
            FulfillmentPreference,
            FulfillmentOutcome,
            finalizedAssets,
            CreatedAtUtc,
            finalizedAtUtc,
            ResultCountMode,
            HiddenMoments,
            CandidateSetFingerprint);
    }

    public static GenerationOutputProject FromResult(
        GenerationResult result,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(result);
        GenerationOutputAsset[] assets =
            result.Candidates
                .Select(candidate =>
                {
                    GenerationCandidateCaptionTrack? captions =
                        result.Captions?.FindTrack(candidate.Id);
                    return new GenerationOutputAsset(
                            candidate.Id,
                            candidate.GlobalRank,
                            result.Moments.Sources
                                .Single(
                                    source =>
                                        source.AnalyzedSource.PreparedSource.Media.FullPath.Equals(
                                            candidate.SourceFullPath,
                                            StringComparison.OrdinalIgnoreCase))
                                .AnalyzedSource.PreparedSource.Media,
                            outputFullPath: null,
                            candidate.Start,
                            candidate.End,
                            candidate.Score,
                            candidate.QualityTarget,
                            candidate.SelectionReason,
                            candidate.Reason,
                            captions?.HasRenderableSegments == true
                                ? captions.ToStudioHandoff()
                                : null,
                            editorialContext: result.EditorialMetadata?
                                .Find(candidate.Id).Context,
                            editorialMetadata: result.EditorialMetadata?
                                .Find(candidate.Id).Draft,
                            preferenceFeatures:
                                candidate.PreferenceFeatures);
                })
                .ToArray();
        string canonical =
            string.Join(
                "\n",
                assets.Select(
                    static asset =>
                        asset.Id + "|" +
                        asset.SourceFullPath.ToUpperInvariant() + "|" +
                        asset.SourceStart.Ticks + "|" +
                        asset.SourceEnd.Ticks));
        string candidateSetFingerprint = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical)));

        return new GenerationOutputProject(
            $"project-{Guid.NewGuid():N}",
            result.Mode,
            outputDirectory,
            result.Moments.RequestedCount,
            result.Request.SetupOptions.ClipFulfillmentPreference,
            result.Moments.FulfillmentOutcome,
            assets,
            DateTimeOffset.UtcNow,
            resultCountMode:
                result.Request.SetupOptions.ResultCountMode,
            hiddenMoments: result.HiddenMoments.Moments.Select(
                static hidden => hidden.ToStudioHandoff()),
            candidateSetFingerprint:
                $"candidates-{candidateSetFingerprint[..20].ToLowerInvariant()}");
    }

    private static string CreateCandidateSetFingerprint(
        GenerationMode mode,
        IEnumerable<GenerationOutputAsset> assets)
    {
        string canonical = string.Join(
            "\n",
            assets.Select(asset =>
                $"{mode}|{asset.Id}|{asset.SourceFullPath.ToUpperInvariant()}|" +
                $"{asset.OriginalSourceStart.Ticks}|" +
                asset.OriginalSourceEnd.Ticks));
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return $"candidates-{hash[..20].ToLowerInvariant()}";
    }
}
