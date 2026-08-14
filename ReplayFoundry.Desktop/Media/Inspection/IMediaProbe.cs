using System.Threading;
using System.Threading.Tasks;

namespace ReplayFoundry.Desktop.Media.Inspection;

public interface IMediaProbe
{
    Task<MediaProbeResult> ProbeAsync(
        string fullPath,
        CancellationToken cancellationToken);
}
