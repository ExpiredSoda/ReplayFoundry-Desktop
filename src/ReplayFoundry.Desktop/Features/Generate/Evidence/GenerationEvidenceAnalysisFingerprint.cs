using System.Globalization;
using System.IO;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Signals;
using ReplayFoundry.Desktop.Media.Analysis.Summaries;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

internal sealed class GenerationEvidenceAnalysisFingerprint :
    IEquatable<GenerationEvidenceAnalysisFingerprint>
{
    private readonly string _canonicalValue;

    private GenerationEvidenceAnalysisFingerprint(
        string canonicalValue)
    {
        _canonicalValue = canonicalValue;
    }

    public static GenerationEvidenceAnalysisFingerprint Create(
        GenerationEvidenceAnalysisRequest request,
        MediaEvidenceAnalyzerIdentity analyzerIdentity)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(analyzerIdentity);

        var builder =
            new CanonicalBuilder();

        builder.Add("ReplayFoundry.GenerationEvidenceFingerprint");
        builder.Add(request.Settings.PolicyVersion);
        builder.Add(
            MediaSignalEvidencePolicy
                .CurrentSchemaVersion);
        builder.Add(analyzerIdentity.Name);
        builder.Add(analyzerIdentity.Version);

        builder.Add(request.SourceCount);
        builder.AddPath(
            request.ReferenceSource.Source.FullPath);

        for (int index = 0;
             index < request.SourceCount;
             index++)
        {
            PreparedGenerationSource source =
                request.PreparedSources[index];

            builder.Add(index);
            builder.AddPath(source.Source.FullPath);
            builder.Add(source.Source.IsReference);
            builder.Add(source.FileSnapshot.FileLength);
            builder.Add(
                source.FileSnapshot
                    .LastWriteTimeUtc.UtcTicks);
            builder.AddPath(source.Media.FullPath);
            builder.Add(source.Media.Duration.Ticks);

            AddPlan(
                builder,
                request.SourcePlans[index]);
        }

        AddOptions(
            builder,
            request.Settings.Options);

        builder.Add(
            request.Settings
                .IncludedRegionRoles.Count);

        foreach (CompositionRegionRole role in
                 request.Settings.IncludedRegionRoles)
        {
            builder.Add((int)role);
        }

        AddSummaryOptions(
            builder,
            request.Settings.SummaryOptions);

        return new GenerationEvidenceAnalysisFingerprint(
            builder.ToString());
    }

    public bool Equals(
        GenerationEvidenceAnalysisFingerprint? other)
    {
        return other is not null &&
               string.Equals(
                   _canonicalValue,
                   other._canonicalValue,
                   StringComparison.Ordinal);
    }

    public override bool Equals(
        object? obj)
    {
        return Equals(
            obj as GenerationEvidenceAnalysisFingerprint);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(
            _canonicalValue);
    }

    private static void AddPlan(
        CanonicalBuilder builder,
        PreparedSourceCompositionPlan sourcePlan)
    {
        CompositionPlan plan =
            sourcePlan.Plan;

        builder.AddPath(plan.SourcePath);
        builder.Add(plan.SourceDuration.Ticks);
        builder.Add((int)plan.CoordinateSpace);
        builder.Add(plan.Manifest.SchemaVersion);
        builder.Add(
            plan.Manifest.CoordinateSpaceVersion);
        builder.Add((int)plan.Manifest.Origin);
        builder.Add(plan.Intervals.Count);

        for (int intervalIndex = 0;
             intervalIndex < plan.Intervals.Count;
             intervalIndex++)
        {
            CompositionLayoutInterval interval =
                plan.Intervals[intervalIndex];

            builder.Add(intervalIndex);
            builder.Add(interval.Start.Ticks);
            builder.Add(interval.End.Ticks);
            builder.Add(interval.Regions.Count);

            foreach (CompositionRegion region in
                     interval.Regions)
            {
                builder.AddCaseInsensitive(
                    region.Id);
                builder.AddDouble(region.Geometry.X);
                builder.AddDouble(region.Geometry.Y);
                builder.AddDouble(region.Geometry.Width);
                builder.AddDouble(region.Geometry.Height);
                builder.Add((int)region.Role);
                builder.Add((int)region.Traits);
                builder.AddDouble(
                    region.GeometryConfidence.Value);
                builder.AddDouble(
                    region.RoleConfidence.Value);
                builder.Add((int)region.GeometrySource);
                builder.Add((int)region.RoleSource);
            }
        }
    }

    private static void AddOptions(
        CanonicalBuilder builder,
        MediaEvidenceAnalysisOptions options)
    {
        builder.AddDouble(
            options.SceneThresholdPercent);
        builder.Add(
            options.MinimumBlackDuration.Ticks);
        builder.AddDouble(
            options.BlackPixelThreshold);
        builder.AddDouble(
            options.BlackPictureRatio);
        builder.Add(
            options.MinimumFreezeDuration.Ticks);
        builder.AddDouble(
            options.FreezeNoiseToleranceDb);
        builder.Add(
            options.MinimumSilenceDuration.Ticks);
        builder.AddDouble(
            options.SilenceNoiseThresholdDb);
        builder.Add(
            options.ProcessTimeout.Ticks);
        builder.Add(
            options.VisualSignalSampleInterval.Ticks);
        builder.Add(
            options.AudioSignalWindowDuration.Ticks);
    }

    private static void AddSummaryOptions(
        CanonicalBuilder builder,
        MediaEvidenceSummaryOptions options)
    {
        builder.Add(
            options.SceneClusterMaximumGap.Ticks);
        builder.Add(
            options.SceneDensityBucketDuration.Ticks);
        builder.Add(
            options.SilenceMergeTolerance.Ticks);
        builder.Add(
            options.ShortSilenceMaximum.Ticks);
        builder.Add(
            options.LongSilenceMinimum.Ticks);
        builder.AddDouble(
            options.DarkLumaThreshold);
        builder.AddDouble(
            options.BrightLumaThreshold);
        builder.Add(
            options.SignalSummaryPolicyVersion);
    }

    private sealed class CanonicalBuilder
    {
        private readonly StringBuilder _builder =
            new();

        public void Add(
            string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            _builder
                .Append(
                    value.Length
                        .ToString(
                            CultureInfo.InvariantCulture))
                .Append(':')
                .Append(value)
                .Append(';');
        }

        public void Add(
            long value)
        {
            Add(
                value.ToString(
                    CultureInfo.InvariantCulture));
        }

        public void Add(
            int value)
        {
            Add((long)value);
        }

        public void Add(
            bool value)
        {
            Add(value ? 1 : 0);
        }

        public void AddDouble(
            double value)
        {
            Add(
                BitConverter
                    .DoubleToInt64Bits(value));
        }

        public void AddPath(
            string value)
        {
            AddCaseInsensitive(
                Path.GetFullPath(value));
        }

        public void AddCaseInsensitive(
            string value)
        {
            Add(value.ToUpperInvariant());
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
