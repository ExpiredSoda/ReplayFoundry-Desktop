using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReplayFoundry.Desktop.Presentation.Converters;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public sealed class LocalThumbnailImage : Image
{
    public static readonly DependencyProperty SourcePathProperty =
        DependencyProperty.Register(
            nameof(SourcePath),
            typeof(string),
            typeof(LocalThumbnailImage),
            new PropertyMetadata(null, OnLoadInputChanged));

    public static readonly DependencyProperty DecodePixelWidthProperty =
        DependencyProperty.Register(
            nameof(DecodePixelWidth),
            typeof(int),
            typeof(LocalThumbnailImage),
            new PropertyMetadata(0, OnLoadInputChanged));

    public static readonly DependencyProperty ReloadTokenProperty =
        DependencyProperty.Register(
            nameof(ReloadToken),
            typeof(int),
            typeof(LocalThumbnailImage),
            new PropertyMetadata(0, OnLoadInputChanged));

    private static readonly FileImageSourceConverter Loader = new();
    private CancellationTokenSource? _loadCancellation;

    public LocalThumbnailImage()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string? SourcePath
    {
        get => (string?)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public int DecodePixelWidth
    {
        get => (int)GetValue(DecodePixelWidthProperty);
        set => SetValue(DecodePixelWidthProperty, value);
    }

    public int ReloadToken
    {
        get => (int)GetValue(ReloadTokenProperty);
        set => SetValue(ReloadTokenProperty, value);
    }

    private static void OnLoadInputChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is LocalThumbnailImage image && image.IsLoaded)
        {
            if (e.Property == ReloadTokenProperty &&
                image.SourcePath is { } path)
            {
                FileImageSourceConverter.Invalidate(path);
            }
            image.BeginLoad();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => BeginLoad();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelLoad();
        Source = null;
    }

    private async void BeginLoad()
    {
        CancelLoad();
        Source = null;
        string? path = SourcePath;
        int decodePixelWidth = DecodePixelWidth;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        object? decoded;
        try
        {
            decoded = await Task.Run(
                () => Loader.Convert(
                    path,
                    typeof(ImageSource),
                    decodePixelWidth.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture),
                cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cancellation.IsCancellationRequested &&
            ReferenceEquals(_loadCancellation, cancellation))
        {
            Source = decoded as ImageSource;
            _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }
}
