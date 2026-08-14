using System.Globalization;
using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlBatchRequestJsonWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public const string SchemaVersion =
        "visual-semantic-input-batch-1.0";

    public static async Task WriteAsync(
        string path,
        VisualSemanticBatchRequest request,
        CancellationToken cancellationToken) =>
        await WriteAsync(
            path,
            request,
            SchemaVersion,
            cancellationToken);

    public static async Task WriteQualifiedEditorialAsync(
        string path,
        VisualSemanticBatchRequest request,
        CancellationToken cancellationToken) =>
        await WriteAsync(
            path,
            request,
            "visual-semantic-qualified-input-batch-1.0",
            cancellationToken);

    private static async Task WriteAsync(
        string path,
        VisualSemanticBatchRequest request,
        string schemaVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        object payload = new
        {
            schemaVersion,
            prompt = new
            {
                request.Prompt.SchemaVersion,
                request.Prompt.Name,
                request.Prompt.Version,
                request.Prompt.Text,
                request.Prompt.Sha256,
                frozenAtUtc =
                    request.Prompt.FrozenAtUtc.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
            },
            model = new
            {
                request.Model.SchemaVersion,
                request.Model.RepositoryId,
                request.Model.Revision,
                request.Model.ManifestSha256,
            },
            videoPolicy = new
            {
                request.VideoPolicy.SchemaVersion,
                maximumReviewDurationSeconds =
                    request.VideoPolicy.MaximumReviewDuration.TotalSeconds,
                request.VideoPolicy.MaximumWidth,
                request.VideoPolicy.MaximumHeight,
                request.VideoPolicy.MaximumPixelsPerFrame,
                request.VideoPolicy.MinimumFrames,
                request.VideoPolicy.MaximumFrames,
                request.VideoPolicy.MaximumTotalPixels,
                request.VideoPolicy.FramesPerSecond,
                request.VideoPolicy.AudioSupplied,
                request.VideoPolicy.VideoBackend,
                request.VideoPolicy.SamplingPolicyVersion,
                request.VideoPolicy.TrimPolicyVersion,
            },
            requests =
                request.Requests.Select(CreateRequest).ToArray(),
        };
        string json =
            JsonSerializer.Serialize(
                payload,
                JsonOptions);

        await File.WriteAllTextAsync(
            path,
            json,
            cancellationToken);
    }

    private static object CreateRequest(
        VisualSemanticRequest request) =>
        new
        {
            request.CaseId,
            request.CaseHash,
            request.SourceId,
            reviewVideo = new
            {
                path = request.Input.ReviewVideoPath,
                sha256 = request.Input.ReviewVideoSha256,
                byteLength = request.Input.ReviewVideoByteLength,
                lastWriteTimeUtc =
                    request.Input.ReviewVideoLastWriteTimeUtc.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
                reviewVideoDurationSeconds =
                    request.Input.ReviewVideoDuration.TotalSeconds,
            },
            candidate = new
            {
                id = request.CandidateId,
                mode = request.CandidateMode.ToString(),
                startRelativeSeconds =
                    request.CandidateStartRelative.TotalSeconds,
                endRelativeSeconds =
                    request.CandidateEndRelative.TotalSeconds,
                sourceAbsoluteOffsetSeconds =
                    request.SourceAbsoluteOffset.TotalSeconds,
            },
            composition = new
            {
                request.Composition.LayoutDescription,
                coordinateSpace =
                    request.Composition.CoordinateSpace.ToString(),
                regions =
                    request.Composition.Regions
                        .Select(
                            static region =>
                                new
                                {
                                    region.Id,
                                    role = region.Role.ToString(),
                                    geometry =
                                        CreateCanonicalGeometry(
                                            region.Geometry.X,
                                            region.Geometry.Y,
                                            region.Geometry.Width,
                                            region.Geometry.Height),
                                    geometrySource =
                                        region.GeometrySource.ToString(),
                                    roleSource =
                                        region.RoleSource.ToString(),
                                })
                        .ToArray(),
            },
            transcript = new
            {
                policy = request.Transcript.Policy.ToString(),
                evidenceStatus =
                    request.Transcript.EvidenceStatus?.ToString(),
                spans =
                    request.Transcript.Spans
                        .Select(
                            static span =>
                                new
                                {
                                    span.Id,
                                    span.Text,
                                    startSeconds =
                                        span.ReviewRelativeStart
                                            .TotalSeconds,
                                    endSeconds =
                                        span.ReviewRelativeEnd
                                            .TotalSeconds,
                                    span.IsNonSpeech,
                                    timingPrecision =
                                        span.TimingPrecision.ToString(),
                                })
                        .ToArray(),
                accuracyWarning =
                    request.Transcript.TranscriptAccuracyWarning,
            },
            deterministicSummary =
                CreateSummary(request.DeterministicSummary),
        };

    private static object CreateCanonicalGeometry(
        double xValue,
        double yValue,
        double widthValue,
        double heightValue)
    {
        decimal x = ToCanonicalDecimal(xValue);
        decimal y = ToCanonicalDecimal(yValue);
        decimal width = ToCanonicalDecimal(widthValue);
        decimal height = ToCanonicalDecimal(heightValue);

        // NormalizedRectangle validates binary doubles. Independently emitted
        // shortest decimal values can add to slightly more than one even when
        // the original binary edge is valid. Keep the origin exact and trim
        // only that serialization artifact so the strict host sees the same
        // bounded rectangle.
        width = Math.Min(width, 1m - x);
        height = Math.Min(height, 1m - y);

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException(
                "Visual-semantic composition geometry could not be represented as a positive bounded decimal rectangle.");
        }

        return new
        {
            x,
            y,
            width,
            height,
        };
    }

    private static decimal ToCanonicalDecimal(
        double value) =>
        decimal.Parse(
            value.ToString("R", CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);

    private static object? CreateSummary(
        VisualSemanticDeterministicSummary? summary) =>
        summary is null
            ? null
            : new
            {
                candidateDurationSeconds =
                    summary.CandidateDuration.TotalSeconds,
                summary.SceneBoundaryCount,
                summary.GameplayActivityBurstCount,
                summary.AudioNoveltyEventCount,
                summary.PresenterSupportEventCount,
                integrityStatus = summary.IntegrityStatus.ToString(),
                eventNeighborhood =
                    summary.EventNeighborhoodStart.HasValue
                        ? (object)new
                        {
                            startSeconds =
                                summary.EventNeighborhoodStart.Value
                                    .TotalSeconds,
                            peakSeconds =
                                summary.EventNeighborhoodPeak!.Value
                                    .TotalSeconds,
                            endSeconds =
                                summary.EventNeighborhoodEnd!.Value
                                    .TotalSeconds,
                        }
                        : null,
                mode = summary.Mode.ToString(),
                confirmedRegionRoles =
                    summary.ConfirmedRegionRoles
                        .Select(static value => value.ToString())
                        .ToArray(),
            };
}
