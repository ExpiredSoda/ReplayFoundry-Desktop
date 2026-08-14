using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

public interface IGenerationRunner
{
    Task<GenerationResult> RunAsync(
        GenerationRequest request,
        IProgress<GenerationProgressUpdate> progress,
        CancellationToken cancellationToken);
}
