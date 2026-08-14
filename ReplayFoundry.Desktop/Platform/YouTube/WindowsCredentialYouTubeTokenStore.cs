using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace ReplayFoundry.Desktop.Platform.YouTube;

internal sealed class WindowsCredentialYouTubeTokenStore :
    IYouTubeCredentialStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumBlobBytes = 2_560;
    private const string SchemaVersion =
        "replayfoundry-youtube-oauth-credential-1.0";

    private readonly string _targetName;

    public WindowsCredentialYouTubeTokenStore(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException(
                "The credential target requires a client ID.",
                nameof(clientId));
        }
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(clientId.Trim())));
        _targetName = $"ReplayFoundry/YouTube/{hash[..20]}";
    }

    public YouTubeStoredCredential? Read()
    {
        if (!CredRead(
                _targetName,
                CredentialTypeGeneric,
                flags: 0,
                out IntPtr pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }
            throw new Win32Exception(
                error,
                "Windows Credential Manager could not read the YouTube connection.");
        }

        try
        {
            NativeCredential native =
                Marshal.PtrToStructure<NativeCredential>(pointer);
            if (native.CredentialBlobSize is 0 or > MaximumBlobBytes ||
                native.CredentialBlob == IntPtr.Zero)
            {
                throw new InvalidDataException(
                    "The stored YouTube credential is empty or oversized.");
            }
            byte[] bytes = new byte[native.CredentialBlobSize];
            Marshal.Copy(native.CredentialBlob, bytes, 0, bytes.Length);
            CredentialDocument? document =
                JsonSerializer.Deserialize<CredentialDocument>(bytes);
            string[]? scopes = document?.Scopes;
            if (document is null ||
                document.SchemaVersion != SchemaVersion ||
                string.IsNullOrWhiteSpace(document.RefreshToken) ||
                document.CreatedAtUtc.Offset != TimeSpan.Zero ||
                scopes is null ||
                scopes.Length == 0 ||
                scopes.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException(
                    "The stored YouTube credential is invalid.");
            }
            return new YouTubeStoredCredential(
                document.RefreshToken,
                document.CreatedAtUtc,
                Array.AsReadOnly(scopes.ToArray()));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The stored YouTube credential is not valid JSON.",
                exception);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(YouTubeStoredCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (string.IsNullOrWhiteSpace(credential.RefreshToken) ||
            credential.CreatedAtUtc.Offset != TimeSpan.Zero ||
            credential.Scopes.Count == 0 ||
            credential.Scopes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "The YouTube credential cannot be stored.",
                nameof(credential));
        }
        byte[] blob = JsonSerializer.SerializeToUtf8Bytes(
            new CredentialDocument
            {
                SchemaVersion = SchemaVersion,
                RefreshToken = credential.RefreshToken,
                CreatedAtUtc = credential.CreatedAtUtc,
                Scopes = credential.Scopes.ToArray(),
            });
        if (blob.Length > MaximumBlobBytes)
        {
            throw new InvalidDataException(
                "The YouTube credential exceeds the Windows credential limit.");
        }

        IntPtr target = Marshal.StringToCoTaskMemUni(_targetName);
        IntPtr userName = Marshal.StringToCoTaskMemUni("YouTube OAuth");
        IntPtr blobPointer = Marshal.AllocCoTaskMem(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var native = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredentialPersistLocalMachine,
                UserName = userName,
            };
            if (!CredWrite(ref native, flags: 0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows Credential Manager could not save the YouTube connection.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
            Marshal.FreeCoTaskMem(blobPointer);
            Marshal.FreeCoTaskMem(userName);
            Marshal.FreeCoTaskMem(target);
        }
    }

    public void Delete()
    {
        if (!CredDelete(
                _targetName,
                CredentialTypeGeneric,
                flags: 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(
                    error,
                    "Windows Credential Manager could not remove the YouTube connection.");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    private sealed class CredentialDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public string[]? Scopes { get; set; }
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credentialPointer);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(
        ref NativeCredential credential,
        uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(
        string target,
        uint type,
        uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr credentialPointer);
}
