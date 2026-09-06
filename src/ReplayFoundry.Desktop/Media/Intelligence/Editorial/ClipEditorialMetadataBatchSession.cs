namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial;

/// <summary>Creates an isolated, disposable scope for one automatic retry operation.</summary>
public interface IClipEditorialMetadataBatchSessionFactory
{
    IClipEditorialMetadataBatchSession CreateBatchSession();
}

/// <summary>A session owns transient reuse state, never the shared provider runtime.</summary>
public interface IClipEditorialMetadataBatchSession :
    IClipEditorialMetadataFailSoftBatchGenerator, IDisposable
{
}
