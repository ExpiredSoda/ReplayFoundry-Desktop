using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlObservationCanonicalizer;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonCollections;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed record Qwen3VlParsedEditorialObservation(
    VisualSemanticEditorialObservation Observation,
    VisualSemanticEditorialCanonicalizationAudit CanonicalizationAudit);

internal static class Qwen3VlEditorialObservationParser
{
    public static Qwen3VlParsedEditorialObservation
        Parse(
            string json,
            TimeSpan reviewDuration,
            TimeSpan candidateStart,
            TimeSpan candidateEnd)
    {
        if (reviewDuration <= TimeSpan.Zero ||
            candidateStart < TimeSpan.Zero ||
            candidateEnd <= candidateStart ||
            candidateEnd > reviewDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateEnd),
                "Prompt 2.0 parsing requires a bounded review and candidate interval.");
        }

        if (string.IsNullOrEmpty(json) ||
            !string.Equals(
                json,
                json.Trim(),
                StringComparison.Ordinal))
        {
            throw new Qwen3VlOutputParseException(
                "Prompt 2.0 output must be one bare JSON object without surrounding text.");
        }

        try
        {
            using JsonDocument document = Open(json);
            JsonElement root = document.RootElement;
            RejectProhibitedReasoning(root, "$");
            RequireExactProperties(
                root,
                "$",
                "observableContentType",
                "hasDistinctEvent",
                "hasObservablePayoff",
                "routineTraversalOrMenuOnly",
                "candidateRequiresMissingContext",
                "candidateContainsOnlyAmbientChange",
                "transcriptContextSupport",
                "observedChanges",
                "evidenceIntervals",
                "uncertaintyReasons",
                "editorialDisposition",
                "rejectReason",
                "dispositionRationale");

            VisualSemanticEditorialEvidenceInterval[] rawIntervals =
                RequireArray(
                        root,
                        "evidenceIntervals",
                        "$")
                    .Select(
                        (element, index) =>
                            ParsePrompt2EvidenceInterval(
                                element,
                                reviewDuration,
                                $"$.evidenceIntervals[{index}]"))
                    .ToArray();
            int nestedCanonicalizationCount = 0;
            JsonElement[] rawChangeElements =
                RequireArray(
                    root,
                    "observedChanges",
                    "$");
            var rawChangeList =
                new List<VisualSemanticEditorialObservedChange>(
                    rawChangeElements.Length);

            for (int index = 0;
                 index < rawChangeElements.Length;
                 index++)
            {
                rawChangeList.Add(
                    ParsePrompt2ObservedChange(
                        rawChangeElements[index],
                        $"$.observedChanges[{index}]",
                        ref nestedCanonicalizationCount));
            }

            VisualSemanticEditorialObservedChange[] rawChanges =
                rawChangeList.ToArray();
            VisualSemanticEditorialUncertainty[] rawUncertainties =
                RequireArray(
                        root,
                        "uncertaintyReasons",
                        "$")
                    .Select(
                        (element, index) =>
                            ParsePrompt2Uncertainty(
                                element,
                                $"$.uncertaintyReasons[{index}]"))
                    .ToArray();

            if (rawChanges.Length >
                    VisualSemanticEditorialObservation
                        .MaximumObservedChanges ||
                rawIntervals.Length >
                    VisualSemanticEditorialObservation
                        .MaximumEvidenceIntervals ||
                rawUncertainties.Length >
                    VisualSemanticEditorialObservation
                        .MaximumUncertaintyReasons)
            {
                throw Failure(
                    "Prompt 2.0 semantic collections exceed their raw cardinality limits.");
            }

            VisualSemanticEditorialCanonicalizationResult canonical =
                VisualSemanticEditorialCanonicalizer.Canonicalize(
                    rawChanges,
                    rawIntervals,
                    rawUncertainties,
                    nestedCanonicalizationCount);
            var observation =
                new VisualSemanticEditorialObservation(
                    RequireEnum<
                        VisualSemanticObservableContentType>(
                        root,
                        "observableContentType",
                        "$"),
                    RequireEnum<VisualSemanticTernary>(
                        root,
                        "hasDistinctEvent",
                        "$"),
                    RequireEnum<VisualSemanticTernary>(
                        root,
                        "hasObservablePayoff",
                        "$"),
                    RequireEnum<VisualSemanticTernary>(
                        root,
                        "routineTraversalOrMenuOnly",
                        "$"),
                    RequireEnum<VisualSemanticTernary>(
                        root,
                        "candidateRequiresMissingContext",
                        "$"),
                    RequireEnum<VisualSemanticTernary>(
                        root,
                        "candidateContainsOnlyAmbientChange",
                        "$"),
                    RequireEnum<
                        VisualSemanticTranscriptContextSupport>(
                        root,
                        "transcriptContextSupport",
                        "$"),
                    canonical.ObservedChanges,
                    canonical.EvidenceIntervals,
                    canonical.UncertaintyReasons,
                    RequireEnum<
                        VisualSemanticEditorialDisposition>(
                        root,
                        "editorialDisposition",
                        "$"),
                    RequireEnum<
                        VisualSemanticEditorialRejectReason>(
                        root,
                        "rejectReason",
                        "$"),
                    RequireString(
                        root,
                        "dispositionRationale",
                        "$",
                        VisualSemanticEditorialObservation
                            .MaximumRationaleLength));

            VisualSemanticEditorialTruthTableValidator.Validate(
                observation,
                candidateStart,
                candidateEnd);

            return new Qwen3VlParsedEditorialObservation(
                observation,
                canonical.Audit);
        }
        catch (Qwen3VlOutputParseException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  JsonException or
                  ArgumentException or
                  InvalidOperationException or
                  FormatException or
                  OverflowException)
        {
            throw new Qwen3VlOutputParseException(
                "The Qwen host returned invalid Prompt 2.0 semantic output.",
                innerException: exception);
        }
    }

    private static VisualSemanticEditorialEvidenceInterval
        ParsePrompt2EvidenceInterval(
            JsonElement value,
            TimeSpan reviewDuration,
            string path)
    {
        RequireExactProperties(
            value,
            path,
            "id",
            "startSeconds",
            "endSeconds",
            "description",
            "evidenceBasis");
        TimeSpan start =
            Prompt2Seconds(
                value,
                "startSeconds",
                path);
        TimeSpan end =
            Prompt2Seconds(
                value,
                "endSeconds",
                path);

        if (end > reviewDuration)
        {
            throw Failure(
                $"{path} falls outside the bounded review video.");
        }

        return new VisualSemanticEditorialEvidenceInterval(
            RequireString(value, "id", path, 32),
            start,
            end,
            RequireString(
                value,
                "description",
                path,
                240),
            RequireEnum<VisualSemanticEvidenceBasis>(
                value,
                "evidenceBasis",
                path));
    }

    private static VisualSemanticEditorialObservedChange
        ParsePrompt2ObservedChange(
            JsonElement value,
            string path,
            ref int nestedCanonicalizationCount)
    {
        RequireExactProperties(
            value,
            path,
            "description",
            "evidenceBasis",
            "evidenceIntervalIds");
        string[] rawIdentifiers =
            RequireArray(
                    value,
                    "evidenceIntervalIds",
                    path)
                .Select(
                    (element, index) =>
                        RequireStringValue(
                            element,
                            $"{path}.evidenceIntervalIds[{index}]",
                            32))
                .ToArray();

        if (rawIdentifiers.Length is < 1 or > 6)
        {
            throw Failure(
                $"{path}.evidenceIntervalIds must contain one to six references.");
        }

        string[] canonicalIdentifiers =
            rawIdentifiers
                .Distinct(StringComparer.Ordinal)
                .OrderBy(
                    static value => value,
                    StringComparer.Ordinal)
                .ToArray();

        if (!rawIdentifiers.SequenceEqual(
                canonicalIdentifiers,
                StringComparer.Ordinal))
        {
            nestedCanonicalizationCount++;
        }

        return new VisualSemanticEditorialObservedChange(
            RequireString(
                value,
                "description",
                path,
                240),
            RequireEnum<VisualSemanticEvidenceBasis>(
                value,
                "evidenceBasis",
                path),
            canonicalIdentifiers);
    }

    private static VisualSemanticEditorialUncertainty
        ParsePrompt2Uncertainty(
            JsonElement value,
            string path)
    {
        RequireExactProperties(
            value,
            path,
            "code",
            "description");

        return new VisualSemanticEditorialUncertainty(
            RequireEnum<
                VisualSemanticEditorialUncertaintyCode>(
                value,
                "code",
                path),
            RequireString(
                value,
                "description",
                path,
                240));
    }

    private static TimeSpan Prompt2Seconds(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(
                name,
                out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            throw Failure(
                $"{path}.{name} must be a finite JSON number.");
        }

        string raw = value.GetRawText();

        if (!decimal.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal seconds) ||
            seconds < 0 ||
            ((decimal.GetBits(seconds)[3] >> 16) & 0x7F) > 3)
        {
            throw Failure(
                $"{path}.{name} must be non-negative seconds with at most three decimal places.");
        }

        try
        {
            return TimeSpan.FromTicks(
                checked(
                    (long)(
                        seconds *
                        TimeSpan.TicksPerSecond)));
        }
        catch (OverflowException)
        {
            throw Failure(
                $"{path}.{name} is outside the supported timestamp range.");
        }
    }
}
