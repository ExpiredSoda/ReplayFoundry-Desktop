using System.Buffers.Binary;
using System.IO;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class PngDimensionsReader
{
    private const int PngHeaderWithIhdrLength = 33;
    private const int IhdrDataLength = 13;

    private static readonly byte[] PngSignature =
    [
        0x89,
        0x50,
        0x4E,
        0x47,
        0x0D,
        0x0A,
        0x1A,
        0x0A,
    ];

    public static (int Width, int Height) Read(
        ReadOnlySpan<byte> data)
    {
        if (data.Length < PngHeaderWithIhdrLength)
        {
            throw new InvalidDataException(
                "The preview output is too short to contain a PNG IHDR chunk.");
        }

        if (!data[..PngSignature.Length]
                .SequenceEqual(PngSignature))
        {
            throw new InvalidDataException(
                "The preview output does not contain a PNG signature.");
        }

        int firstChunkLength =
            BinaryPrimitives.ReadInt32BigEndian(
                data.Slice(8, 4));

        if (firstChunkLength != IhdrDataLength ||
            !data.Slice(12, 4)
                .SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException(
                "The preview output does not begin with a valid PNG IHDR chunk.");
        }

        int width =
            BinaryPrimitives.ReadInt32BigEndian(
                data.Slice(16, 4));

        int height =
            BinaryPrimitives.ReadInt32BigEndian(
                data.Slice(20, 4));

        if (width <= 0 ||
            height <= 0)
        {
            throw new InvalidDataException(
                "The preview PNG reports invalid dimensions.");
        }

        return (
            width,
            height);
    }
}
