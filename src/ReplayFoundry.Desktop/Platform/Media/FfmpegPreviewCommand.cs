using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegPreviewCommand
{
    private readonly ReadOnlyCollection<string> _arguments;

    public FfmpegPreviewCommand(
        IEnumerable<string> arguments,
        int expectedWidth,
        int expectedHeight)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string[] snapshot =
            arguments.ToArray();

        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A preview command requires arguments.",
                nameof(arguments));
        }

        if (snapshot.Any(static argument => argument is null))
        {
            throw new ArgumentException(
                "Preview command arguments cannot contain null values.",
                nameof(arguments));
        }

        if (expectedWidth <= 0 ||
            expectedHeight <= 0 ||
            (expectedWidth & 1) != 0 ||
            (expectedHeight & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedWidth),
                "Expected preview dimensions must be positive even values.");
        }

        _arguments =
            Array.AsReadOnly(snapshot);
        ExpectedWidth = expectedWidth;
        ExpectedHeight = expectedHeight;
    }

    public IReadOnlyList<string> Arguments =>
        _arguments;

    public int ExpectedWidth { get; }

    public int ExpectedHeight { get; }
}
