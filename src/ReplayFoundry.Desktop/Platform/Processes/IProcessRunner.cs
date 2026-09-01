using System.Threading;
using System.Threading.Tasks;

namespace ReplayFoundry.Desktop.Platform.Processes;

internal interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken);
}
