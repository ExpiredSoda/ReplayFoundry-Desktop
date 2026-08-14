using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed class Qwen3VlVerifiedModelLease : IDisposable
{
    private const int HashBufferSize = 1024 * 1024;

    private readonly VisualSemanticModelManifest _model;
    private readonly object _sync = new();
    private IReadOnlyList<VerifiedFile>? _verifiedFiles;
    private bool _disposed;

    internal Qwen3VlVerifiedModelLease(VisualSemanticModelManifest model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    internal int FullVerificationCount { get; private set; }

    internal void Verify(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_verifiedFiles is not null)
            {
                VerifyOpenFiles(_verifiedFiles);
                return;
            }

            IReadOnlyList<VerifiedFile> verified =
                OpenAndVerifyFiles(cancellationToken);
            _verifiedFiles = verified;
            FullVerificationCount++;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_verifiedFiles is not null)
            {
                foreach (VerifiedFile file in _verifiedFiles)
                {
                    file.Stream.Dispose();
                }
            }
            _verifiedFiles = null;
        }
    }

    private IReadOnlyList<VerifiedFile> OpenAndVerifyFiles(
        CancellationToken cancellationToken)
    {
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(_model.ModelDirectoryPath));
        string prefix = root + Path.DirectorySeparatorChar;
        var files = new List<VerifiedFile>(_model.Files.Count);
        try
        {
            foreach (VisualSemanticModelFile file in _model.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(
                    Path.Combine(
                        root,
                        file.RelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));
                if (!path.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "A model-manifest file is missing or escaped the model directory.",
                        path);
                }

                var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    HashBufferSize,
                    FileOptions.SequentialScan);
                try
                {
                    if (stream.Length != file.ByteLength)
                    {
                        throw new InvalidDataException(
                            $"Model file integrity failed for '{file.RelativePath}'.");
                    }

                    string hash = HashStream(
                        stream,
                        cancellationToken);
                    if (!hash.Equals(
                            file.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"Model file integrity failed for '{file.RelativePath}'.");
                    }

                    files.Add(new VerifiedFile(file, stream));
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }

            return files.AsReadOnly();
        }
        catch
        {
            foreach (VerifiedFile file in files)
            {
                file.Stream.Dispose();
            }
            throw;
        }
    }

    private static void VerifyOpenFiles(
        IReadOnlyList<VerifiedFile> files)
    {
        foreach (VerifiedFile file in files)
        {
            if (!file.Stream.CanRead ||
                file.Stream.Length != file.Manifest.ByteLength)
            {
                throw new InvalidDataException(
                    $"The verified model lease changed for '{file.Manifest.RelativePath}'.");
            }
        }
    }

    private static string HashStream(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed record VerifiedFile(
        VisualSemanticModelFile Manifest,
        FileStream Stream);
}
