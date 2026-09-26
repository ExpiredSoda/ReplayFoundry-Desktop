using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Storage.Streams;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.Desktop.Platform.Media;

public sealed class WindowsCompositionLayoutSuggestionService : ICompositionLayoutSuggestionService
{
    public async Task<CompositionLayoutSuggestion?> SuggestAsync(VideoPreviewFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        if (!FaceDetector.IsSupported) return null;
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(frame.PngData.ToArray());
            await writer.StoreAsync().AsTask(cancellationToken);
            await writer.FlushAsync().AsTask(cancellationToken);
            writer.DetachStream();
        }
        stream.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        // One small, current frame on the CPU. No model download or GPU allocation.
        double scale = Math.Min(1, 960d / Math.Max(frame.Width, frame.Height));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(frame.Width * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(frame.Height * scale)),
        };
        // PNG decoding cannot directly produce Gray8 on every Windows codec.
        // Decode a supported color format first, then convert for FaceDetector.
        using SoftwareBitmap decoded = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(cancellationToken);
        using SoftwareBitmap bitmap = SoftwareBitmap.Convert(decoded, BitmapPixelFormat.Gray8);
        FaceDetector detector = await FaceDetector.CreateAsync().AsTask(cancellationToken);
        IList<DetectedFace> faces = await detector.DetectFacesAsync(bitmap).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        NormalizedRectangle[] boxes = faces.Select(face => new NormalizedRectangle(
            (double)face.FaceBox.X / bitmap.PixelWidth, (double)face.FaceBox.Y / bitmap.PixelHeight,
            (double)face.FaceBox.Width / bitmap.PixelWidth, (double)face.FaceBox.Height / bitmap.PixelHeight)).ToArray();
        return CompositionLayoutSuggestionPolicy.FromFaces(boxes, frame.Height > frame.Width);
    }
}
