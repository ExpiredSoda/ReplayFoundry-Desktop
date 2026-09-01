using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public interface IVisualSemanticProvider
{
    InferenceProviderIdentity Identity { get; }

    Task<VisualSemanticBatchResult> ObserveAsync(
        VisualSemanticBatchRequest request,
        CancellationToken cancellationToken);
}
