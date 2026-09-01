namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MediaMomentFinderIdentity
{
    public MediaMomentFinderIdentity(
        string name,
        string version)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A moment finder identity requires a name.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException(
                "A moment finder identity requires a version.",
                nameof(version));
        }

        Name = name.Trim();
        Version = version.Trim();
    }

    public string Name { get; }

    public string Version { get; }
}
