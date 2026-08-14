using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.Desktop.Platform.VisualText;

/// <summary>
/// Low-cost Windows CPU OCR adapter. It uses only language packs already
/// installed for the current user and never downloads a runtime or model.
/// </summary>
public sealed class WindowsMediaOcrProvider : IVisualTextProvider
{
    public const string ProviderName =
        "ReplayFoundry.WindowsMediaOcrProvider";
    public const string ProviderVersion = "1.0.0";

    private readonly OcrEngine? _engine;
    private readonly VisualTextProviderIdentity? _identity;

    public WindowsMediaOcrProvider(string preferredLanguageTag = "en-US")
    {
        if (string.IsNullOrWhiteSpace(preferredLanguageTag))
        {
            throw new ArgumentException(
                "A preferred OCR language tag is required.",
                nameof(preferredLanguageTag));
        }

        Windows.Globalization.Language? language = OcrEngine
            .AvailableRecognizerLanguages
            .FirstOrDefault(value => value.LanguageTag.Equals(
                preferredLanguageTag,
                StringComparison.OrdinalIgnoreCase)) ??
            OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
        if (language is null)
        {
            return;
        }

        _engine = OcrEngine.TryCreateFromLanguage(language);
        if (_engine is not null)
        {
            _identity = new VisualTextProviderIdentity(
                ProviderName,
                ProviderVersion,
                "Windows.Media.Ocr CPU",
                Environment.OSVersion.VersionString,
                language.LanguageTag);
        }
    }

    public string Name => ProviderName;
    public string Version => ProviderVersion;
    public bool IsAvailable => _engine is not null;

    public async Task<VisualTextFrameObservation> RecognizeAsync(
        VisualTextFrameRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        OcrEngine engine = _engine ?? throw new InvalidOperationException(
            "No compatible Windows OCR language is installed for the current user.");
        VisualTextProviderIdentity identity = _identity!;
        VideoPreviewFrame frame = request.Frame;
        if (frame.Width > OcrEngine.MaxImageDimension ||
            frame.Height > OcrEngine.MaxImageDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"OCR frames cannot exceed {OcrEngine.MaxImageDimension} pixels per dimension.");
        }

        var stopwatch = Stopwatch.StartNew();
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(frame.PngData.ToArray());
            await writer.StoreAsync().AsTask(cancellationToken);
            await writer.FlushAsync().AsTask(cancellationToken);
            writer.DetachStream();
        }
        stream.Seek(0);

        BitmapDecoder decoder = await BitmapDecoder
            .CreateAsync(stream)
            .AsTask(cancellationToken);
        using SoftwareBitmap bitmap = await decoder
            .GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied)
            .AsTask(cancellationToken);
        OcrResult result = await engine
            .RecognizeAsync(bitmap)
            .AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        VisualTextLine[] lines = result.Lines
            .Select(line => CreateLine(line, frame.Width, frame.Height))
            .Where(static line => line is not null)
            .Cast<VisualTextLine>()
            .ToArray();
        stopwatch.Stop();
        return new VisualTextFrameObservation(
            request,
            identity,
            lines,
            stopwatch.Elapsed);
    }

    private static VisualTextLine? CreateLine(
        OcrLine line,
        int frameWidth,
        int frameHeight)
    {
        VisualTextWord[] words = line.Words
            .Select(word => CreateWord(word, frameWidth, frameHeight))
            .Where(static word => word is not null)
            .Cast<VisualTextWord>()
            .ToArray();
        string text = line.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 || words.Length == 0)
        {
            return null;
        }
        if (text.Length > 1_000)
        {
            text = text[..1_000].TrimEnd();
        }
        return new VisualTextLine(text, words);
    }

    private static VisualTextWord? CreateWord(
        OcrWord word,
        int frameWidth,
        int frameHeight)
    {
        string text = word.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }
        if (text.Length > 256)
        {
            text = text[..256].TrimEnd();
        }

        Windows.Foundation.Rect rect = word.BoundingRect;
        double x = Math.Clamp(rect.X / frameWidth, 0, 1);
        double y = Math.Clamp(rect.Y / frameHeight, 0, 1);
        double width = Math.Clamp(rect.Width / frameWidth, 0, 1 - x);
        double height = Math.Clamp(rect.Height / frameHeight, 0, 1 - y);
        if (width <= 0 || height <= 0 || x >= 1 || y >= 1)
        {
            return null;
        }
        return new VisualTextWord(
            text,
            new VisualTextBoundingBox(x, y, width, height));
    }
}
