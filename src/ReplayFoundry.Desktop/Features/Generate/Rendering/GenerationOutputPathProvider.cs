using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Rendering;

internal interface IGenerationOutputPathProvider
{
    string CreateOutputDirectoryPath(
        GenerationMomentFindingResult moments);
}

internal sealed class SystemGenerationOutputPathProvider :
    IGenerationOutputPathProvider
{
    private readonly GenerationOutputLocationState _location;

    public SystemGenerationOutputPathProvider()
        : this(
            new GenerationOutputLocationState(
                new InMemoryGenerationOutputLocationStore()))
    {
    }

    public SystemGenerationOutputPathProvider(
        GenerationOutputLocationState location)
    {
        _location = location ??
            throw new ArgumentNullException(nameof(location));
    }

    public string CreateOutputDirectoryPath(
        GenerationMomentFindingResult moments)
    {
        ArgumentNullException.ThrowIfNull(moments);
        _location.EnsureCurrentRootIsWritable();

        return Path.Combine(
            _location.OutputRootDirectory,
            DateTimeOffset.UtcNow.ToString(
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture) +
            "-" +
            Guid.NewGuid().ToString("N")[..8]);
    }
}
