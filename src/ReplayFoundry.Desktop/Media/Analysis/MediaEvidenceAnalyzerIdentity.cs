using System;

namespace ReplayFoundry.Desktop.Media.Analysis;

/// <summary>
/// Stable process-local identity for an evidence-analyzer implementation.
/// </summary>
public sealed class MediaEvidenceAnalyzerIdentity :
    IEquatable<MediaEvidenceAnalyzerIdentity>
{
    public MediaEvidenceAnalyzerIdentity(
        string name,
        string version)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "An analyzer identity requires a name.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException(
                "An analyzer identity requires a version.",
                nameof(version));
        }

        Name = name.Trim();
        Version = version.Trim();
    }

    public string Name { get; }

    public string Version { get; }

    public bool Equals(
        MediaEvidenceAnalyzerIdentity? other)
    {
        return other is not null &&
               string.Equals(
                   Name,
                   other.Name,
                   StringComparison.Ordinal) &&
               string.Equals(
                   Version,
                   other.Version,
                   StringComparison.Ordinal);
    }

    public override bool Equals(
        object? obj)
    {
        return Equals(
            obj as MediaEvidenceAnalyzerIdentity);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(Name),
            StringComparer.Ordinal.GetHashCode(Version));
    }

    public override string ToString()
    {
        return $"{Name} {Version}";
    }
}
